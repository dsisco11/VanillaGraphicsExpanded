using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies the production scene adapters against shared residency and existing domain eligibility.</summary>
public sealed class SceneResidencyMigrationTests
{
    #region Tracing adapter
    /// <summary>Overlapping movement preserves the incarnation while departure cancels and removes the old lifetime.</summary>
    [Theory]
    [InlineData(-524288)]
    [InlineData(-1)]
    [InlineData(524288)]
    public void TraceMovementRetainsOverlapAndRejectsDepartedUpload(int x)
    {
        var coordinator = Coordinator();
        var scheduler = new TraceSceneRegionScheduler(coordinator);
        scheduler.SetWindow(new(x, 0, 0), new(x + 2, 0, 0));
        ChunkKey overlap = ChunkKey.FromChunkCoords(x + 1, 0, 0);
        ChunkKey departing = ChunkKey.FromChunkCoords(x, 0, 0);
        scheduler.NotifyChunkDirty(overlap, 1, 1);
        scheduler.NotifyChunkDirty(departing, 1, 1);
        Assert.True(scheduler.OnRequestIssued(overlap, 1, 1));
        Assert.True(scheduler.OnRequestIssued(departing, 1, 1));
        PartitionRequest keep = scheduler.RequestFor(overlap);
        PartitionRequest obsolete = scheduler.RequestFor(departing);
        scheduler.SetWindow(new(x + 1, 0, 0), new(x + 3, 0, 0));
        Assert.True(scheduler.IsCurrent(keep));
        Assert.False(scheduler.IsCurrent(obsolete));
        Assert.False(scheduler.TryPublish(obsolete, 1, 1, () => true, () => throw new Exception("departed upload")));
        Assert.True(scheduler.TryPublish(keep, 1, 1, () => true, () => true));
        Assert.Equal(1, scheduler.AppliedCount);
        Assert.Equal(0, scheduler.InFlightCount);
    }

    /// <summary>Reset and same-coordinate re-registration cannot accept a previous world's completion.</summary>
    [Fact]
    public void TraceResetRejectsOldRegistrationAndKeepsInstancesIndependent()
    {
        var coordinator = Coordinator();
        var a = new TraceSceneRegionScheduler(coordinator);
        var b = new TraceSceneRegionScheduler(coordinator);
        a.SetWindow(default, default);
        b.SetWindow(default, default);
        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);
        Assert.True(a.OnRequestIssued(key, 1, 1));
        Assert.True(b.OnRequestIssued(key, 1, 1));
        PartitionRequest old = a.RequestFor(key);
        PartitionRequest other = b.RequestFor(key);
        Assert.NotEqual(old.Key.Instance, other.Key.Instance);
        a.Reset();
        a.SetWindow(default, default);
        Assert.False(a.TryPublish(old, 1, 0, () => true, () => throw new Exception("old world upload")));
        Assert.True(b.TryPublish(other, 1, 0, () => true, () => true));
        Assert.Equal(0, a.AppliedCount);
        Assert.Equal(1, b.AppliedCount);
    }
    #endregion

    #region Near-scene adapter
    /// <summary>A reserved byte upload cannot strand a scene's zero-byte slot acknowledgement.</summary>
    [Fact]
    public void SceneSlotAcknowledgementDoesNotConsumeAnotherUploadTurn()
    {
        var coordinator = new PartitionCoordinator(new(128, 16, 16, 16, 8));
        var a = new DomainPartitionFixture(coordinator);
        var b = new DomainPartitionFixture(coordinator);
        Assert.True(coordinator.TryBeginUpdate(a.Key, out PartitionRequest? first));
        Assert.True(coordinator.TryBeginUpdate(b.Key, out PartitionRequest? second));
        Assert.True(coordinator.TryPublishUpdate(first!, 8, () => true, () => true));
        Assert.False(coordinator.TryPublishUpdate(second!, 8, () => true, () => true));
        var scheduler = new LumonSceneRegionScheduler(coordinator);
        var cell = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(-1, 0, 0));
        scheduler.UpdateSlot(cell, 0, 1, 1);
        scheduler.UpdateResidency(Context(1), Priority(1));
        Assert.Equal(WorldCellActualState.Active, cell.ActualState);
        coordinator.Pump(1);
        Assert.True(coordinator.TryPublishUpdate(second!, 8, () => true, () => true));
    }

    /// <summary>Coordinator retirement reaches the storage backend once, before the caller remaps a ring slot.</summary>
    [Fact]
    public void SceneCoverageRetiresOnlyDepartingBackendOwner()
    {
        var coordinator = Coordinator();
        var retired = new List<LumonSceneChunkCoord>();
        var scheduler = new LumonSceneRegionScheduler(coordinator, retired.Add);
        var left = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(-1, 0, 0));
        var overlap = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(0, 0, 0));
        scheduler.UpdateSlot(left, 0, 1, 1);
        scheduler.UpdateSlot(overlap, 1, 1, 1);
        scheduler.UpdateResidency(Context(1), Priority(1));
        var next = Context(2) with
        {
            LoadedWindowMinRegion = new(0, 0, 0), LoadedWindowMaxRegion = new(1, 0, 0),
            ActiveWindowMinRegion = new(0, 0, 0), ActiveWindowMaxRegion = new(0, 0, 0)
        };
        scheduler.UpdateCoverage(next);
        Assert.Equal(new[] { left.ChunkCoord }, retired);
        Assert.True(scheduler.TryGetCell(overlap.Key, out _));
        scheduler.Reset(3);
        Assert.Equal(new[] { left.ChunkCoord, overlap.ChunkCoord }, retired);
    }

    /// <summary>Preserves the former pure coverage cases through the actual coordinator adapter.</summary>
    [Theory]
    [InlineData(10, false, (int)WorldCellDesiredState.Unloaded)]
    [InlineData(10, true, (int)WorldCellDesiredState.Unloaded)]
    [InlineData(3, false, (int)WorldCellDesiredState.Loaded)]
    [InlineData(3, true, (int)WorldCellDesiredState.Active)]
    [InlineData(2, false, (int)WorldCellDesiredState.Active)]
    [InlineData(0, false, (int)WorldCellDesiredState.Active)]
    public void SceneDesiredCoverageIsOwnedByCoordinator(int coordinate, bool hot, int expectedValue)
    {
        var coordinator = Coordinator();
        var scheduler = new LumonSceneRegionScheduler(coordinator);
        var cell = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(coordinate, 0, coordinate));
        if (hot) cell.ApplyHeatFromRequests(1, 100);
        var context = new WorldCellStateTransitionContext(default, default, false,
            new(0, 0, 0), new(4, 0, 4), true, new(0, 0, 0), new(2, 0, 2), true, 100);
        scheduler.UpdateResidency(context, Priority(100));
        Assert.Equal((WorldCellDesiredState)expectedValue, cell.DesiredState);
    }

    /// <summary>Coordinator loaded/active windows preserve heat promotion, expiry and page-work gating.</summary>
    [Fact]
    public void SceneHeatPromotionAndExpiryPreserveCaptureAndRelightQueues()
    {
        var coordinator = Coordinator();
        var scheduler = new LumonSceneRegionScheduler(coordinator);
        var active = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(-1, 0, 0));
        var fringe = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(0, 0, 0));
        scheduler.UpdateSlot(active, 0, 1, 1);
        scheduler.UpdateSlot(fringe, 1, 1, 1);
        active.ApplyHeatFromRequests(1, 1);
        var state = Context(1);
        var priority = Priority(1);
        scheduler.UpdateResidency(state, priority);
        Assert.Equal(WorldCellActualState.Active, active.ActualState);
        Assert.Equal(WorldCellActualState.Loaded, fringe.ActualState);
        Assert.Equal(1, scheduler.CaptureCount);

        fringe.ApplyHeatFromRequests(2, 2);
        scheduler.UpdateResidency(Context(2), Priority(2));
        coordinator.Pump(1);
        scheduler.UpdateResidency(Context(2), Priority(2));
        Assert.Equal(WorldCellActualState.Active, fringe.ActualState);
        Assert.Equal(2, scheduler.CaptureCount);

        scheduler.UpdateResidency(Context(20), Priority(20));
        coordinator.Pump(2);
        scheduler.UpdateResidency(Context(20), Priority(20));
        Assert.Equal(WorldCellDesiredState.Loaded, fringe.DesiredState);
        Assert.Equal(WorldCellActualState.Loaded, fringe.ActualState);
        Assert.Equal(1, scheduler.CaptureCount);
    }

    /// <summary>A recycled slot becomes unavailable and observes its settling delay before reactivation.</summary>
    [Fact]
    public void SceneSlotGenerationChangeInvalidatesAcknowledgedResidency()
    {
        var coordinator = Coordinator();
        var scheduler = new LumonSceneRegionScheduler(coordinator);
        var cell = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new(-1, 0, 0));
        scheduler.UpdateSlot(cell, 0, 1, 1);
        scheduler.UpdateResidency(Context(1), Priority(1));
        Assert.Equal(WorldCellActualState.Active, cell.ActualState);
        scheduler.UpdateSlot(cell, 0, 2, 2);
        Assert.Equal(WorldCellActualState.Unloaded, cell.ActualState);
        scheduler.UpdateResidency(Context(2), Priority(2));
        Assert.NotEqual(WorldCellActualState.Active, cell.ActualState);
        coordinator.Pump(1);
        scheduler.UpdateResidency(Context(4), Priority(4));
        Assert.Equal(WorldCellActualState.Active, cell.ActualState);
        scheduler.Reset(5);
        Assert.Empty(coordinator.Diagnostics());
    }
    #endregion

    #region Context construction
    /// <summary>Provides finite shared resources without requiring a GPU or game world.</summary>
    private static PartitionCoordinator Coordinator() => new(new(128, 64, 64, 64, 1024));
    /// <summary>Uses a required negative cell with one loaded neighbor.</summary>
    private static WorldCellStateTransitionContext Context(long tick) => new(default, default, false,
        new(-1, 0, 0), new(0, 0, 0), true, new(-1, 0, 0), new(-1, 0, 0), true, tick);
    /// <summary>Preserves the scene's distance and time inputs independently from lifecycle ticks.</summary>
    private static WorldCellPriorityContext Priority(long tick) => new(default, default, false, default, default, false, tick);
    #endregion
}
