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

    [Fact]
    public void MissingChunk_DoesNotDominate_DequeueResults()
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

        _ = sched.RefreshPriorities(in ctx, budget: 32);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 10, out ChunkKey first, out _));
        Assert.Equal(missing, first);

        sched.OnRequestIssued(first, version: 1, nowTick: 10);
        sched.OnRequestCompleted(first, ChunkWorkStatus.ChunkUnavailable, requestedVersion: 1, nowTick: 10);

        // At the same time, the missing chunk is cooled down and should not keep resurfacing.
        _ = sched.RefreshPriorities(in ctx, budget: 32);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 10, out ChunkKey second, out _));
        Assert.Equal(other, second);

        Assert.False(sched.TryDequeueNextEligible(nowTick: 10, out _, out _));
    }

    [Fact]
    public void SeenLoadedRecently_OverridesCooldown_AndSchedulesPromptly()
    {
        var sched = new TraceSceneRegionScheduler();
        sched.SetWindow(min: new VectorInt3(0, 0, 0), max: new VectorInt3(0, 0, 1));

        ChunkKey missing = ChunkKey.FromChunkCoords(0, 0, 0);
        ChunkKey other = ChunkKey.FromChunkCoords(0, 0, 1);

        // Make "other" already applied so it won't compete.
        sched.NotifyChunkDirty(other, currentVersion: 1, nowTick: 0);
        sched.OnRequestIssued(other, version: 1, nowTick: 0);
        sched.OnRequestCompleted(other, ChunkWorkStatus.Success, requestedVersion: 1, nowTick: 0);

        // Now mark missing dirty and fail it as unavailable to put it on cooldown.
        sched.NotifyChunkDirty(missing, currentVersion: 1, nowTick: 10);

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
        Assert.Equal(missing, first);

        sched.OnRequestIssued(first, version: 1, nowTick: 10);
        sched.OnRequestCompleted(first, ChunkWorkStatus.ChunkUnavailable, requestedVersion: 1, nowTick: 10);

        // Chunk arrives soon after; loaded hint should clear cooldown and missing streak.
        sched.NotifyChunkSeenLoaded(missing, nowTick: 20);

        var ctx2 = ctx with { NowTick = 20 };
        _ = sched.RefreshPriorities(in ctx2, budget: 16);

        Assert.True(sched.TryDequeueNextEligible(nowTick: 20, out ChunkKey next, out _));
        Assert.Equal(missing, next);
    }
}
