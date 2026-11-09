using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Component.WorldPartition.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.WorldPartition;

/// <summary>Adversarial lifecycle acknowledgements through a reusable delayed provider.</summary>
public sealed class PartitionLifecycleTests
{
    #region Public API
    /// <summary>Dirtying rejects the old callback even when its worker ignores cancellation.</summary>
    [Fact]
    public void DirtyDuringProcessingRejectsOldRevision()
    {
        // Arrange
        var (c, p, id) = Create(); c.Pump(0);
        PartitionRequest old = p.Pending[0].Request;
        // Act and Assert
        c.Dirty(old.Key); c.Pump(1);
        Assert.True(old.Cancellation.IsCancellationRequested);
        p.Complete(1); p.Complete(0); c.Pump(2);
        Assert.Single(p.Publications);
        Assert.Equal(2, p.Publications[0].Request.Revision);
        Assert.Equal(1, c.Statistics(id).StaleCompletions);
        c.Dirty(old.Key);
        Assert.Empty(p.Visible);
        Assert.False(c.Cells(id)[0].Ready);
        Assert.Equal(PartitionResidency.Active, c.Cells(id)[0].Actual);
    }

    /// <summary>Dependency changes after capture force retry before any upload.</summary>
    [Fact]
    public void DependencyRevalidationPreventsPublication()
    {
        // Arrange
        var (c, p, id) = Create(); c.Pump(0);
        // Act and Assert
        p.DependencyRevision++; p.CompleteAll(); c.Pump(1);
        Assert.Empty(p.Publications); Assert.False(c.Cells(id)[0].Ready);
        c.Pump(2); p.CompleteAll(); c.Pump(3);
        Assert.Equal(2, ((PartitionProviderFixture.Content)Assert.Single(p.Publications).Content!).DependencyRevision);
    }

    /// <summary>Removing and recreating a key cannot inherit a previous incarnation's work or slot data.</summary>
    [Fact]
    public void RecreatedKeyAndSlotReuseRejectLateCompletion()
    {
        // Arrange
        var (c, p, id) = Create(); c.Pump(0); p.CompleteAll(); c.Pump(1);
        PartitionCellKey key = c.Cells(id)[0].Key;
        int slot = p.Slots[key];
        // Act and Assert
        c.Dirty(key); c.Pump(2);
        PartitionRequest old = p.Pending[0].Request;
        c.RemoveSource(id, 1); c.Pump(3);
        Assert.Empty(p.Visible);
        c.SetSource(new(1, id, "w", new(), new(new(), new(1, 1, 1)))); c.Pump(4);
        Assert.NotEqual(old.Incarnation, c.Cells(id)[0].Incarnation);
        p.Complete(1); c.Pump(5);
        Assert.Equal(slot, p.Slots[key]);
        int count = p.Publications.Count;
        p.Complete(); c.Pump(6);
        Assert.Equal(count, p.Publications.Count);
        Assert.Equal(1, c.Statistics(id).StaleCompletions);
    }

    /// <summary>Layout generations and world teardown independently prevent obsolete publication.</summary>
    [Fact]
    public void LayoutReplacementAndWorldUnloadCancelWork()
    {
        // Arrange
        var (c, p, id) = Create(); c.Pump(0);
        long generation = p.Pending[0].Request.Generation;
        // Act and Assert
        c.ReplaceLayout(id, new(new(8, 8, 8))); c.Pump(1);
        Assert.NotEqual(generation, p.Pending[1].Request.Generation);
        p.Complete(0); c.Pump(2); Assert.Empty(p.Publications);
        c.UnloadWorld("w"); p.CompleteAll(); c.Pump(3);
        Assert.Empty(p.Visible); Assert.Empty(p.Publications);
    }

    /// <summary>Missing sources retry while unsupported current content remains classified and resident.</summary>
    [Fact]
    public void MissingDependenciesRetryButUnsupportedDoesNotLoop()
    {
        // Arrange
        var (c, p, id) = Create(); p.Missing = true;
        // Act and Assert
        c.Pump(0);
        Assert.Equal(PartitionContentStatus.MissingDependencies, c.Cells(id)[0].ContentStatus);
        Assert.Empty(p.Pending); Assert.False(c.Cells(id)[0].Ready);
        c.Pump(0); Assert.Single(p.Captures);
        p.Missing = false; p.Unsupported = true; c.Pump(1); p.CompleteAll(); c.Pump(2);
        Assert.True(c.Cells(id)[0].Ready);
        Assert.Equal(PartitionContentStatus.Unsupported, c.Cells(id)[0].ContentStatus);
        c.Pump(10); Assert.Equal(2, p.Captures.Count);
    }

    /// <summary>Failed uploads and activation acknowledgements never prematurely advance actual state.</summary>
    [Fact]
    public void PublicationAndActivationAreAcknowledged()
    {
        // Arrange
        var (c, p, id) = Create(); p.PublishSucceeds = false; c.Pump(0); p.CompleteAll(); c.Pump(1);
        Assert.False(c.Cells(id)[0].Ready);
        // Act and Assert
        p.PublishSucceeds = true; p.ActivationSucceeds = false;
        c.Pump(2); p.CompleteAll(); c.Pump(3);
        Assert.True(c.Cells(id)[0].Ready); Assert.Equal(PartitionResidency.Loaded, c.Cells(id)[0].Actual);
        p.ActivationSucceeds = true; c.Pump(4);
        Assert.Equal(PartitionResidency.Active, c.Cells(id)[0].Actual);
        Assert.Single(p.Publications);
    }

    /// <summary>Worker completion queues do not publish until the owner pumps, and foreign mutations fail.</summary>
    [Fact]
    public void WorkerCompletionCannotMutateOrPublish()
    {
        // Arrange
        var (c, p, id) = Create(); int owner = Environment.CurrentManagedThreadId; c.Pump(0);
        Exception? error = null;
        var worker = new Thread(() => { p.CompleteAll(); try { c.Pump(1); } catch (Exception ex) { error = ex; } });
        // Act and Assert
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(error); Assert.Empty(p.Publications);
        c.Pump(1); Assert.True(c.Cells(id)[0].Ready);
        Assert.All(p.CallbackThreads, thread => Assert.Equal(owner, thread));
    }

    /// <summary>Capture, dispatch, and upload budgets can independently stop the pipeline.</summary>
    [Theory]
    [InlineData(0, 10, 100, 0, 0)]
    [InlineData(10, 0, 100, 1, 0)]
    [InlineData(10, 10, 0, 1, 1)]
    public void IndependentPipelineBudgets(int captures, int dispatches, long bytes, int expectedCaptures, int expectedWorkers)
    {
        // Arrange the capture, dispatch and upload-byte budgets from each theory row.
        // Act by pumping once and assert admission counts against the selected limits.
        var (c, p, id) = Create(new(10, 10, captures, dispatches, bytes)); c.Pump(0);
        Assert.Equal(expectedCaptures, p.Captures.Count); Assert.Equal(expectedWorkers, p.Pending.Count);
        p.CompleteAll(); c.Pump(1);
        // Complete admitted work and assert no page publication or readiness follows this budget-limited batch.
        Assert.Empty(p.Publications); Assert.False(c.Cells(id)[0].Ready);
    }

    /// <summary>Cancelled workers retain their in-flight allocation until they acknowledge.</summary>
    [Fact]
    public void CancellationDoesNotOversubscribeWorkers()
    {
        // Arrange
        var (c, p, id) = Create(new(10, 1, 10, 10, 100)); c.Pump(0);
        // Act and Assert
        c.Dirty(c.Cells(id)[0].Key); c.Pump(1);
        Assert.Single(p.Pending);
        p.Complete(); c.Pump(2);
        Assert.Single(p.Pending);
        p.Complete(); c.Pump(3); Assert.True(c.Cells(id)[0].Ready);
    }

    /// <summary>Capacity reports preserve all desired required cells instead of silently cropping them.</summary>
    [Fact]
    public void CapacityShortfallPreservesDesiredCoverage()
    {
        // Arrange
        var (c, p, id) = Create(new(1, 10, 10, 10, 100));
        p.AutoComplete = true;
        // Act and Assert
        c.SetSource(new(1, id, "w", new(), new(new(), new(33, 1, 1)))); c.Pump(0);
        Assert.Equal(3, c.Statistics(id).Required); Assert.Equal(2, c.Statistics(id).CapacityShortfall);
        Assert.Equal(1, c.Statistics(id).Ready); Assert.Equal(3, c.Cells(id).Length);
    }

    /// <summary>A busy high-priority registration cannot consume every capture frame forever.</summary>
    [Fact]
    public void SharedAndPartitionLimitsProvideFairService()
    {
        // Arrange
        var c = new PartitionCoordinator(new(100, 100, 1, 1, 4));
        var a = new PartitionProviderFixture { AutoComplete = true };
        var b = new PartitionProviderFixture { AutoComplete = true };
        long first = c.Register("a", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 1, 1, 4), a);
        long second = c.Register("b", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 1, 1, 4), b);
        // Act and Assert
        c.SetSource(new(1, first, "w", new(), new(new(), new(160, 1, 1)), 10000));
        c.SetSource(new(1, second, "w", new(), new(new(), new(1, 1, 1)), -10000));
        for (int tick = 0; tick < 6; tick++) c.Pump(tick);
        Assert.Single(b.Publications);
        Assert.True(a.Publications.Count > 0);
    }
    #endregion

    #region Private
    /// <summary>Creates a one-cell coordinator scenario with independently adjustable budgets.</summary>
    private static (PartitionCoordinator Coordinator, PartitionProviderFixture Provider, long Id) Create(PartitionLimits? limits = null)
    {
        limits ??= new(10, 10, 10, 10, 100);
        var p = new PartitionProviderFixture();
        var c = new PartitionCoordinator(limits);
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 0, 0), limits, p);
        c.SetSource(new(1, id, "w", new(), new(new(), new(1, 1, 1))));
        return (c, p, id);
    }
    #endregion
}











