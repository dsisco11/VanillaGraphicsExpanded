using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Incremental coverage and shared-budget regressions under sustained work.</summary>
public sealed class PartitionSchedulingTests
{
    #region Coverage regressions
    /// <summary>Moving inside the same selected range performs no cell membership edits.</summary>
    [Fact]
    public void SubcellMovementDoesNotEnumerateCoverageAgain()
    {
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 10000));
        var p = new PartitionProviderFixture { AutoComplete = true };
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 16, 2), new(100, 100, 100, 100, 10000), p);
        c.SetSource(new(1, id, "w", new(1, 0, 0), new(new(1, 0, 0), new(2, 1, 1)))); c.Pump(0);
        long visits = c.Statistics(id).CoverageCellVisits;
        c.SetSource(new(1, id, "w", new(2, 0, 0), new(new(2, 0, 0), new(3, 1, 1)))); c.Pump(100);
        Assert.Equal(visits, c.Statistics(id).CoverageCellVisits);
        c.SetSource(new(1, id, "w", new(16, 0, 0), new(new(16, 0, 0), new(17, 1, 1)))); c.Pump(101);
        Assert.Equal(2, c.Cells(id).Length);
        Assert.Equal(PartitionResidency.Loaded, c.Cells(id).Single(x => x.Key.Coordinate.X == 0).Desired);
        c.Pump(104); Assert.Single(c.Cells(id));
    }

    /// <summary>Prefetched cells have coherent loaded content and activation does not upload again.</summary>
    [Fact]
    public void PrefetchUploadsBeforeActivation()
    {
        var p = new PartitionProviderFixture { AutoComplete = true };
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 10000));
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(1, 1, 0), new(100, 100, 100, 100, 10000), p);
        c.SetSource(new(1, id, "w", new(), new(new(1, 1, 1), new(16, 2, 2)))); c.Pump(0);
        var prefetched = c.Cells(id).Single(x => x.Key.Coordinate.X == 1);
        Assert.True(prefetched.Ready); Assert.Equal(PartitionResidency.Loaded, prefetched.Actual);
        int uploads = p.Publications.Count;
        c.SetSource(new(1, id, "w", new(), new(new(15, 1, 1), new(17, 2, 2)))); c.Pump(1);
        Assert.Equal(uploads, p.Publications.Count);
        Assert.All(c.Cells(id), x => Assert.Equal(PartitionResidency.Active, x.Actual));
    }
    #endregion

    #region Resource and publication regressions
    /// <summary>Impossible payloads release global work credit while retaining an explicit diagnostic.</summary>
    [Fact]
    public void OversizedPayloadDoesNotBlockAnotherPartition()
    {
        var c = new PartitionCoordinator(new(100, 1, 100, 100, 4));
        var huge = new PartitionProviderFixture { AutoComplete = true, UploadBytes = 8 };
        var small = new PartitionProviderFixture { AutoComplete = true };
        long a = c.Register("huge", "w", new(new(16,16,16)), new(0,0,0), new(100,100,100,100,100), huge);
        long b = c.Register("small", "w", new(new(16,16,16)), new(0,0,0), new(100,100,100,100,100), small);
        c.SetSource(new(1,a,"w",new(),new(new(),new(1,1,1))));
        c.SetSource(new(1,b,"w",new(),new(new(),new(1,1,1)))); c.Pump(0);
        Assert.True(c.Cells(b)[0].Ready);
        Assert.False(c.Cells(a)[0].Ready);
        Assert.Equal(4,c.Statistics(a).UploadBudgetShortfall);
        c.Pump(1); Assert.Single(huge.Captures);
        huge.UploadBytes = 4; c.Dirty(c.Cells(a)[0].Key); c.Pump(2);
        Assert.True(c.Cells(a)[0].Ready); Assert.Equal(0,c.Statistics(a).UploadBudgetShortfall);
    }

    /// <summary>Speculative snapshots and completed uploads yield in-flight credit to new required work.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void RequiredWorkPreemptsSpeculativeInFlightCredit(int dispatchBudget)
    {
        var c = new PartitionCoordinator(new(100,1,100,dispatchBudget,4));
        var p = new PartitionProviderFixture { AutoComplete=true };
        long id = c.Register("p","w",new(new(16,16,16)),new(1,1,0),new(100,100,100,100,100),p);
        c.SetSource(new(1,id,"w",new(),new(new(1,1,1),new(16,2,2))));c.Pump(0);
        Assert.Equal(1,c.Statistics(id).InFlight);
        c.SetSource(new(2,id,"w",new(160,1,1),new(new(160,1,1),new(161,2,2))));c.Pump(1);
        Assert.True(c.Cells(id).Single(x=>x.Key.Coordinate.X==10).Ready);
        Assert.False(c.Cells(id).Single(x=>x.Key.Coordinate.X==1).Ready);
    }

    /// <summary>New required work preempts a speculative upload's reservation for the next frame.</summary>
    [Fact]
    public void RequiredPublicationPreemptsSpeculativeUploadReservation()
    {
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 4));
        var p = new PartitionProviderFixture { AutoComplete = true };
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(1, 1, 0), new(100, 100, 100, 100, 4), p);
        c.SetSource(new(1, id, "w", new(), new(new(1, 1, 1), new(16, 2, 2))));
        c.Pump(0);
        Assert.Single(p.Publications);
        Assert.Equal(1, c.Statistics(id).UploadBacklog);
        c.Dirty(p.Publications[0].Request.Key);
        c.Pump(1);
        Assert.Equal(2, p.Publications.Count);
        Assert.Equal(0, p.Publications[1].Request.Key.Coordinate.X);
        Assert.False(c.Cells(id).Single(x => x.Key.Coordinate.X == 1).Ready);
    }

    /// <summary>Provider callbacks cannot mutate coordinator state during synchronous publication.</summary>
    [Fact]
    public void ReentrantPublicationMutationIsRejected()
    {
        var c = new PartitionCoordinator(new(10, 10, 10, 10, 100));
        var p = new PartitionProviderFixture { AutoComplete = true };
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 0, 0), new(10, 10, 10, 10, 100), p);
        p.DuringPublish = () => Assert.Throws<InvalidOperationException>(() => c.Unregister(id));
        c.SetSource(new(1, id, "w", new(), new(new(), new(1, 1, 1))));
        c.Pump(0);
        Assert.True(c.Cells(id)[0].Ready);
    }

    /// <summary>A dependency changed during backend upload leaves the cell hidden after acknowledgement.</summary>
    [Fact]
    public void DependencyChangedDuringPublishIsNotReady()
    {
        var p = new PartitionProviderFixture { AutoComplete = true };
        p.DuringPublish = () => p.DependencyRevision++;
        var c = new PartitionCoordinator(new(10, 10, 10, 10, 100));
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 0, 0), new(10, 10, 10, 10, 100), p);
        c.SetSource(new(1, id, "w", new(), new(new(), new(1, 1, 1)))); c.Pump(0);
        Assert.False(c.Cells(id)[0].Ready); Assert.Empty(p.Visible);
        p.DuringPublish = null; c.Pump(1); Assert.True(c.Cells(id)[0].Ready);
    }

    /// <summary>Completed content waiting for upload still consumes the in-flight cap.</summary>
    [Fact]
    public void UploadBacklogCannotGrowPastInFlightLimit()
    {
        var p = new PartitionProviderFixture { AutoComplete = true };
        var c = new PartitionCoordinator(new(100, 1, 100, 100, 4));
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 100, 100, 100), p);
        c.SetSource(new(1, id, "w", new(), new(new(), new(160, 1, 1))));
        c.Pump(0);
        Assert.Equal(2, p.Captures.Count); Assert.Equal(1, c.Statistics(id).UploadBacklog);
        Assert.Equal(1, c.Statistics(id).InFlight);
        Assert.Equal(0, c.Statistics(id).UploadBudgetShortfall);
    }

    /// <summary>Full-frame uploads eventually receive service despite an endless stream of cheap updates.</summary>
    [Fact]
    public void LargeUploadsAreNotStarvedByCheapUploads()
    {
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 10));
        var cheap = new PartitionProviderFixture { AutoComplete = true, UploadBytes = 1 };
        var expensive = new PartitionProviderFixture { AutoComplete = true, UploadBytes = 10 };
        long a = c.Register("cheap", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 100, 100, 10), cheap);
        long b = c.Register("expensive", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 100, 100, 10), expensive);
        c.SetSource(new(1, a, "w", new(), new(new(), new(160, 1, 1))));
        c.SetSource(new(1, b, "w", new(), new(new(), new(1, 1, 1))));
        for (int tick = 0; tick < 5; tick++)
        {
            foreach (var cell in c.Cells(a).Where(x => x.Ready)) c.Dirty(cell.Key);
            c.Pump(tick);
        }
        Assert.Single(expensive.Publications);
        Assert.True(c.Cells(b)[0].Ready);
    }

    /// <summary>Per-partition caps apply even when shared service has ample capacity.</summary>
    [Fact]
    public void PartitionBudgetCapsAreIndependentOfSharedLimits()
    {
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 1000));
        var p = new PartitionProviderFixture { AutoComplete = true };
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 1, 1, 4), p);
        c.SetSource(new(1, id, "w", new(), new(new(), new(160, 1, 1)))); c.Pump(0);
        Assert.Single(p.Captures); Assert.Single(p.Publications);
        c.Pump(1); Assert.Equal(2, p.Captures.Count); Assert.Equal(2, p.Publications.Count);
    }
    #endregion
}
