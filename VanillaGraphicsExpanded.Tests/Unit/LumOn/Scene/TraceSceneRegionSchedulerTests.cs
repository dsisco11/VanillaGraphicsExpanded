using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class TraceSceneRegionSchedulerTests
{
    [Fact]
    public void Dequeue_PrefersNearRegions()
    {
        var sched = new TraceSceneRegionScheduler();
        sched.SetWindow(min: new VectorInt3(0, 0, 0), max: new VectorInt3(16, 0, 0));

        // Mark two regions dirty so they get created and prioritized.
        sched.NotifyChunkDirty(ChunkKey.FromChunkCoords(0, 0, 0), currentVersion: 1, nowTick: 100);
        sched.NotifyChunkDirty(ChunkKey.FromChunkCoords(10, 0, 0), currentVersion: 1, nowTick: 100);

        var ctx = new WorldCellPriorityContext(
            CameraBlockPos: new VectorInt3(0, 0, 0),
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 100);

        _ = sched.RefreshPriorities(in ctx, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 100, out ChunkKey first, out _));
        Assert.Equal(ChunkKey.FromChunkCoords(0, 0, 0), first);
    }

    [Fact]
    public void InFlightCell_IsNotReturnedAgain()
    {
        var sched = new TraceSceneRegionScheduler();
        sched.SetWindow(min: new VectorInt3(0, 0, 0), max: new VectorInt3(0, 0, 1));

        ChunkKey a = ChunkKey.FromChunkCoords(0, 0, 0);
        ChunkKey b = ChunkKey.FromChunkCoords(0, 0, 1);

        sched.NotifyChunkDirty(a, currentVersion: 1, nowTick: 1);
        sched.NotifyChunkDirty(b, currentVersion: 1, nowTick: 1);

        var ctx = new WorldCellPriorityContext(
            CameraBlockPos: new VectorInt3(0, 0, 0),
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 1);

        _ = sched.RefreshPriorities(in ctx, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 1, out ChunkKey first, out _));
        sched.OnRequestIssued(first, version: 1, nowTick: 1);

        // Refresh to ensure heap updates reflect in-flight state.
        _ = sched.RefreshPriorities(in ctx, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 1, out ChunkKey second, out _));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ChunkUnavailable_AppliesCooldown_AndDoesNotBlockOtherWork()
    {
        var sched = new TraceSceneRegionScheduler();
        sched.SetWindow(min: new VectorInt3(0, 0, 0), max: new VectorInt3(0, 0, 1));

        ChunkKey missing = ChunkKey.FromChunkCoords(0, 0, 0);
        ChunkKey other = ChunkKey.FromChunkCoords(0, 0, 1);

        sched.NotifyChunkDirty(missing, currentVersion: 1, nowTick: 10);
        sched.NotifyChunkDirty(other, currentVersion: 1, nowTick: 10);

        var ctx = new WorldCellPriorityContext(
            CameraBlockPos: new VectorInt3(0, 0, 0),
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 10);

        _ = sched.RefreshPriorities(in ctx, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 10, out ChunkKey first, out _));
        sched.OnRequestIssued(first, version: 1, nowTick: 10);

        // Complete as unavailable; should apply a cooldown.
        sched.OnRequestCompleted(first, ChunkWorkStatus.ChunkUnavailable, requestedVersion: 1, nowTick: 10);

        // Recompute priorities at the same tick; the other cell should still be schedulable.
        _ = sched.RefreshPriorities(in ctx, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 10, out ChunkKey next, out _));
        Assert.Equal(other, next);
    }
}
