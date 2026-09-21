using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;
using VanillaGraphicsExpanded.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Exercises shared residency authorization for consumers retaining their domain work queues.</summary>
public sealed class PartitionDomainUpdateTests
{
    #region Publication and lifetime
    /// <summary>A dirty revision, teleport and world unload all reject writes from an old worker.</summary>
    [Theory]
    [InlineData("dirty")]
    [InlineData("teleport")]
    [InlineData("unload")]
    public void ObsoleteWorkCannotInvokeUpload(string change)
    {
        var fixture = new DomainPartitionFixture();
        Assert.True(fixture.Coordinator.TryBeginUpdate(fixture.Key, out PartitionRequest? request));
        if (change == "dirty") fixture.Coordinator.Dirty(fixture.Key);
        else if (change == "teleport") fixture.SetWindow(100);
        else fixture.Coordinator.UnloadWorld("test");
        bool wrote = false;
        Assert.False(fixture.Coordinator.TryPublishUpdate(request!, 1, () => true, () => wrote = true));
        Assert.False(wrote);
    }

    /// <summary>Leaving and returning to identical coordinates changes incarnation even when the source revision repeats.</summary>
    [Fact]
    public void RecreatedCellRejectsSameCoordinateOldRequest()
    {
        var f = new DomainPartitionFixture();
        Assert.True(f.Coordinator.TryBeginUpdate(f.Key, out PartitionRequest? old));
        f.SetWindow(100);
        f.SetWindow(0);
        Assert.True(f.Coordinator.TryBeginUpdate(f.Key, out PartitionRequest? current));
        Assert.NotEqual(old!.Incarnation, current!.Incarnation);
        Assert.False(f.Coordinator.TryPublishUpdate(old, 1, () => true, () => throw new Exception("stale upload")));
        Assert.True(f.Coordinator.TryPublishUpdate(current, 1, () => true, () => true));
    }

    /// <summary>Upload deferral retains the captured result and never exposes ready state prematurely.</summary>
    [Fact]
    public void SharedUploadBudgetDefersSecondPartitionWithoutRecapture()
    {
        var coordinator = new PartitionCoordinator(new(4, 4, 4, 4, 8));
        var a = new DomainPartitionFixture(coordinator);
        var b = new DomainPartitionFixture(coordinator);
        Assert.NotEqual(a.Key.Instance, b.Key.Instance);
        Assert.True(coordinator.TryBeginUpdate(a.Key, out PartitionRequest? first));
        Assert.True(coordinator.TryBeginUpdate(b.Key, out PartitionRequest? second));
        Assert.True(coordinator.TryPublishUpdate(first!, 8, () => true, () => true));
        Assert.False(coordinator.TryPublishUpdate(second!, 8, () => true, () => throw new Exception("budget bypass")));
        Assert.True(coordinator.IsCurrent(second!));
        Assert.True(coordinator.TryGetCell(b.Key, out PartitionCellInfo pending));
        Assert.False(pending.Ready);
        coordinator.Pump(1);
        Assert.True(coordinator.TryPublishUpdate(second!, 8, () => true, () => true));
        Assert.True(coordinator.TryGetCell(b.Key, out PartitionCellInfo published));
        Assert.True(published.Ready);
        Assert.Equal(PartitionResidency.Active, published.Actual);
    }

    /// <summary>A cancelled worker consumes in-flight credit until its acknowledgement is drained on the owning thread.</summary>
    [Fact]
    public void CancelledWorkerRetainsCreditUntilAcknowledged()
    {
        var f = new DomainPartitionFixture(new(new(4, 1, 4, 4, 8)));
        Assert.True(f.Coordinator.TryBeginUpdate(f.Key, out PartitionRequest? old));
        f.Coordinator.Dirty(f.Key);
        Assert.False(f.Coordinator.TryBeginUpdate(f.Key, out _));
        f.Coordinator.AcknowledgeDomainWorker(old!);
        f.Coordinator.Pump(1);
        Assert.True(f.Coordinator.TryBeginUpdate(f.Key, out _));
    }
    #endregion

    #region Sustained fairness
    /// <summary>Fixed caller order cannot monopolize one shared capture credit indefinitely.</summary>
    [Fact]
    public void DomainCaptureAdmissionAlternatesUnderSustainedPressure()
    {
        var coordinator = new PartitionCoordinator(new(4, 4, 1, 1, 8));
        var a = new DomainPartitionFixture(coordinator);
        var b = new DomainPartitionFixture(coordinator);
        int first = 0, second = 0;
        for (int tick = 0; tick < 12; tick++)
        {
            coordinator.Pump(tick);
            if (coordinator.TryBeginUpdate(a.Key, out PartitionRequest? ar))
            {
                Assert.True(coordinator.TryPublishUpdate(ar!, 0, () => true, () => true));
                first++;
            }
            if (coordinator.TryBeginUpdate(b.Key, out PartitionRequest? br))
            {
                Assert.True(coordinator.TryPublishUpdate(br!, 0, () => true, () => true));
                second++;
            }
        }
        Assert.InRange(first, 5, 7);
        Assert.InRange(second, 5, 7);
    }

    /// <summary>A completed domain upload retains its turn even when another consumer renews work every update.</summary>
    [Fact]
    public void DomainUploadReservationSurvivesContinuousEarlierCaller()
    {
        var coordinator = new PartitionCoordinator(new(4, 4, 4, 4, 8));
        var a = new DomainPartitionFixture(coordinator);
        var b = new DomainPartitionFixture(coordinator);
        Assert.True(coordinator.TryBeginUpdate(a.Key, out PartitionRequest? first));
        Assert.True(coordinator.TryBeginUpdate(b.Key, out PartitionRequest? second));
        Assert.True(coordinator.TryPublishUpdate(first!, 8, () => true, () => true));
        Assert.False(coordinator.TryPublishUpdate(second!, 8, () => true, () => true));
        coordinator.Pump(1);
        Assert.True(coordinator.TryBeginUpdate(a.Key, out first));
        Assert.False(coordinator.TryPublishUpdate(first!, 8, () => true, () => throw new Exception("stolen upload turn")));
        Assert.True(coordinator.TryPublishUpdate(second!, 8, () => true, () => true));
        coordinator.Pump(2);
        Assert.True(coordinator.TryPublishUpdate(first!, 8, () => true, () => true));
    }

    /// <summary>Automatic providers leave minimum admission service for an already-waiting domain consumer.</summary>
    [Fact]
    public void AutomaticProviderCannotStarveDomainCapture()
    {
        var coordinator = new PartitionCoordinator(new(4, 4, 1, 1, 8));
        var automatic = new PartitionProviderFixture { AutoComplete = true, UploadBytes = 0 };
        long id = coordinator.Register("automatic", "test", new(new(32, 32, 32)), new(0, 0, 0), new(4, 4, 4, 4, 8), automatic);
        coordinator.SetSource(new(0, id, "test", new(), new(new(), new(32, 32, 32))));
        var domain = new DomainPartitionFixture(coordinator);
        int published = 0;
        for (int tick = 0; tick < 12; tick++)
        {
            if (tick > 0) coordinator.Dirty(new(id, "test", new()));
            coordinator.Pump(tick);
            if (coordinator.TryBeginUpdate(domain.Key, out PartitionRequest? request))
            {
                Assert.True(coordinator.TryPublishUpdate(request!, 0, () => true, () => true));
                published++;
            }
        }
        Assert.InRange(published, 5, 7);
        Assert.InRange(automatic.Publications.Count, 5, 7);
    }

    /// <summary>Multiple domain waiters cannot reserve every future update against an automatic provider.</summary>
    [Fact]
    public void DomainContentionCannotStarveAutomaticCapture()
    {
        var coordinator = new PartitionCoordinator(new(8, 1, 1, 1, 8));
        var automatic = new PartitionProviderFixture { AutoComplete = true, UploadBytes = 0 };
        long id = coordinator.Register("automatic", "test", new(new(32, 32, 32)), new(0, 0, 0), new(8, 1, 1, 1, 8), automatic);
        coordinator.SetSource(new(0, id, "test", new(), new(new(), new(32, 32, 32))));
        var a = new DomainPartitionFixture(coordinator);
        var b = new DomainPartitionFixture(coordinator);
        int first = 0, second = 0;
        for (int tick = 0; tick < 18; tick++)
        {
            if (tick > 0) coordinator.Dirty(new(id, "test", new()));
            coordinator.Pump(tick);
            if (coordinator.TryBeginUpdate(a.Key, out PartitionRequest? ar))
            {
                Assert.True(coordinator.TryPublishUpdate(ar!, 0, () => true, () => true));
                first++;
            }
            if (coordinator.TryBeginUpdate(b.Key, out PartitionRequest? br))
            {
                Assert.True(coordinator.TryPublishUpdate(br!, 0, () => true, () => true));
                second++;
            }
        }
        Assert.InRange(automatic.Publications.Count, 8, 10);
        Assert.InRange(first, 4, 5);
        Assert.InRange(second, 4, 5);
    }
    #endregion

}
