using System;
using System.Collections.Generic;
using System.Reflection;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSchedulerBudgetTests
{
    [Fact]
    public void BuildUpdateList_RespectsGlobalTraceBudget()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [100, 100, 100],
            traceMaxProbesPerFrame: 10,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);

        Assert.InRange(list.Count, 0, 10);
    }

    [Fact]
    public void BuildUpdateList_RespectsUploadBudgetBytesPerFrame()
    {
        const int atlasTexelsPerUpdate = 32;
        int estimatedBytes = GetEstimatedUploadBytesPerProbe(atlasTexelsPerUpdate);

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        int expectedMax = 7;
        int uploadBudget = (estimatedBytes * expectedMax) + (estimatedBytes - 1);

        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [100, 100, 100],
            traceMaxProbesPerFrame: 10_000,
            uploadBudgetBytesPerFrame: uploadBudget,
            atlasTexelsPerUpdate: atlasTexelsPerUpdate);

        Assert.Equal(expectedMax, list.Count);
    }

    [Fact]
    public void BuildUpdateList_RespectsPerLevelBudgets()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [2, 3],
            traceMaxProbesPerFrame: 100,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);

        Assert.Equal(5, list.Count);

        int l0 = 0;
        int l1 = 0;
        int l2 = 0;
        foreach (var r in list)
        {
            if (r.Level == 0) l0++;
            else if (r.Level == 1) l1++;
            else if (r.Level == 2) l2++;
        }

        Assert.Equal(2, l0);
        Assert.Equal(3, l1);
        Assert.Equal(0, l2);
    }

    [Fact]
    public void Complete_MovesProbeFromInFlightToValid()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 4);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);

        Assert.Single(list);
        var req = list[0];

        var lifecycle = new LumOnWorldProbeLifecycleState[scheduler.ProbesPerLevel];
        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));

        // BuildUpdateList marks probes as Queued; they become InFlight once the worker claims them.
        Assert.Equal(LumOnWorldProbeLifecycleState.Queued, lifecycle[req.StorageLinearIndex]);
        Assert.True(scheduler.TryClaim(req, frameIndex: 0));

        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));
        Assert.Equal(LumOnWorldProbeLifecycleState.InFlight, lifecycle[req.StorageLinearIndex]);

        scheduler.Complete(req, frameIndex: 1, success: true);

        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));
        Assert.Equal(LumOnWorldProbeLifecycleState.Valid, lifecycle[req.StorageLinearIndex]);
    }

    [Fact]
    public void BuildUpdateList_ContinuouslyRefreshesValidProbes()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 1);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        LumOnWorldProbeUpdateRequest initial = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));

        Assert.Equal(1f, initial.ImportanceFactor);
        Assert.True(scheduler.TryClaim(initial, frameIndex: 0));
        scheduler.Complete(initial, frameIndex: 0, success: true);

        LumOnWorldProbeUpdateRequest refresh = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));

        Assert.Equal(initial.StorageLinearIndex, refresh.StorageLinearIndex);
    }

    [Fact]
    public void BuildUpdateList_PrioritizesHigherImportanceValidProbe()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 2);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        List<LumOnWorldProbeUpdateRequest> initial = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [8],
            traceMaxProbesPerFrame: 8,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);

        foreach (LumOnWorldProbeUpdateRequest request in initial)
        {
            scheduler.Complete(request, frameIndex: 0, success: true);
        }

        foreach (LumOnWorldProbeUpdateRequest request in initial)
        {
            float importanceFactor = request.LocalIndex.X > 0 ? 2f : 1f;
            Assert.True(scheduler.SetImportanceFactor(request.Level, request.StorageLinearIndex, importanceFactor));
        }

        LumOnWorldProbeUpdateRequest refresh = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));

        Assert.Equal(1, refresh.LocalIndex.X);
        Assert.Equal(2f, refresh.ImportanceFactor);
    }

    [Fact]
    public void ValidateProbeCenters_DisablesProbeWithoutSchedulingTrace()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 2);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        var validationResults = new List<LumOnWorldProbeScheduler.ProbeCenterValidation>();

        scheduler.ValidateProbeCenters(
            baseSpacing: 1.0,
            perLevelProbeBudgets: [8],
            classifyProbeCenter: (_, _) => LumOnWorldProbeCenterOccupancy.InsideCollision,
            validationResults);

        var lifecycle = new LumOnWorldProbeLifecycleState[scheduler.ProbesPerLevel];
        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));
        Assert.All(lifecycle, state => Assert.Equal(LumOnWorldProbeLifecycleState.Disabled, state));
        Assert.Equal(scheduler.ProbesPerLevel, validationResults.Count);
    }

    private static int GetEstimatedUploadBytesPerProbe(int atlasTexelsPerUpdate)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        FieldInfo? fProbe = typeof(LumOnWorldProbeScheduler).GetField("EstimatedBytesPerProbeResolveVertex", flags);
        FieldInfo? fTile = typeof(LumOnWorldProbeScheduler).GetField("EstimatedBytesPerTileResolveVertex", flags);

        Assert.NotNull(fProbe);
        Assert.NotNull(fTile);

        object? vProbe = fProbe!.GetRawConstantValue();
        object? vTile = fTile!.GetRawConstantValue();
        Assert.NotNull(vProbe);
        Assert.NotNull(vTile);

        int probeBytes = (int)vProbe!;
        int tileBytes = (int)vTile!;
        return probeBytes + (Math.Max(1, atlasTexelsPerUpdate) * tileBytes);
    }
}
