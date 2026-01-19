using System;
using System.Linq;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSchedulerTests
{
    [Fact]
    public void BuildUpdateList_RespectsGlobalCpuBudget()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 8);

        Vec3d cam = new(0, 0, 0);
        scheduler.UpdateOrigins(cam, baseSpacing: 1.0);

        var list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1000],
            traceMaxProbesPerFrame: 10,
            uploadBudgetBytesPerFrame: int.MaxValue);

        Assert.True(list.Count <= 10);
    }

    [Fact]
    public void BuildUpdateList_RespectsUploadBudget()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 8);

        Vec3d cam = new(0, 0, 0);
        scheduler.UpdateOrigins(cam, baseSpacing: 1.0);

        // Scheduler uses an estimated 64 bytes/probe.
        var list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1000],
            traceMaxProbesPerFrame: 1000,
            uploadBudgetBytesPerFrame: 64 * 7);

        Assert.True(list.Count <= 7);
    }

    [Fact]
    public void OriginShift_MarksIntroducedSlabDirty_AndSelectsItFirst()
    {
        const int res = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: res);

        Vec3d cam0 = new(0.1, 0, 0);
        scheduler.UpdateOrigins(cam0, baseSpacing: 1.0);

        // Schedule and complete all probes to make the level fully valid.
        var all = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam0,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [res * res * res],
            traceMaxProbesPerFrame: 100000,
            uploadBudgetBytesPerFrame: int.MaxValue);

        Assert.Equal(res * res * res, all.Count);

        foreach (var r in all)
        {
            scheduler.Complete(r, frameIndex: 0, success: true);
        }

        // Move camera enough to snap anchor by +1 on X at spacing 1.0.
        Vec3d cam1 = new(1.1, 0, 0);
        scheduler.UpdateOrigins(cam1, baseSpacing: 1.0);

        // The newly introduced slab is local X == res-1, size res*res.
        var slab = scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: cam1,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [res * res],
            traceMaxProbesPerFrame: 100000,
            uploadBudgetBytesPerFrame: int.MaxValue);

        Assert.Equal(res * res, slab.Count);
        Assert.All(slab, r => Assert.Equal(res - 1, r.LocalIndex.X));
    }

    [Fact]
    public void Staleness_PromotesValidToStale_AndSchedulesRefresh()
    {
        const int res = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: res);

        Vec3d cam = new(0, 0, 0);
        scheduler.UpdateOrigins(cam, baseSpacing: 1.0);

        var all = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [res * res * res],
            traceMaxProbesPerFrame: 100000,
            uploadBudgetBytesPerFrame: int.MaxValue);

        foreach (var r in all)
        {
            scheduler.Complete(r, frameIndex: 0, success: true);
        }

        // Advance beyond the default stale threshold (~600 frames for spacing 1.0).
        var refresh = scheduler.BuildUpdateList(
            frameIndex: 1000,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [16],
            traceMaxProbesPerFrame: 16,
            uploadBudgetBytesPerFrame: int.MaxValue);

        Assert.Equal(16, refresh.Count);
    }

    [Fact]
    public void Determinism_SameInputsProduceSameLocalOrder_AfterReset()
    {
        const int res = 8;

        var schedulerA = new LumOnWorldProbeScheduler(levelCount: 1, resolution: res);
        var schedulerB = new LumOnWorldProbeScheduler(levelCount: 1, resolution: res);

        Vec3d cam = new(0, 0, 0);

        schedulerA.UpdateOrigins(cam, baseSpacing: 1.0);
        schedulerB.UpdateOrigins(cam, baseSpacing: 1.0);

        var listA = schedulerA.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [64],
            traceMaxProbesPerFrame: 64,
            uploadBudgetBytesPerFrame: int.MaxValue);

        var listB = schedulerB.BuildUpdateList(
            frameIndex: 0,
            cameraPos: cam,
            baseSpacing: 1.0,
            perLevelProbeBudgets: [64],
            traceMaxProbesPerFrame: 64,
            uploadBudgetBytesPerFrame: int.MaxValue);

        Assert.Equal(
            listA.Select(r => r.LocalIndex.X + "," + r.LocalIndex.Y + "," + r.LocalIndex.Z),
            listB.Select(r => r.LocalIndex.X + "," + r.LocalIndex.Y + "," + r.LocalIndex.Z));
    }
}
