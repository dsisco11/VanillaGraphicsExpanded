using System.Collections.Generic;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks scheduler admission limits and probe lifecycle transitions.</summary>
public sealed class WorldProbeSchedulerBudgetTests
{
    #region Public API
    #region Budget admission
    /// <summary>Hybrid L0 admission and CPU higher levels consume their own atomic staging costs.</summary>
    [Theory]
    [InlineData(2095,0,1)]
    [InlineData(2096,1,0)]
    [InlineData(3671,1,0)]
    [InlineData(3672,1,1)]
    public void HybridBudgetAccountsForEachTraceBackend(int budget,int levelZero,int levelOne)
    {
        // Arrange
        var scheduler=new LumOnWorldProbeScheduler(2,4);
        scheduler.UpdateOrigins(new(0,0,0),1);
        // Act
        var admitted=scheduler.BuildUpdateList(0,new(0,0,0),1,[1,1],2,budget,64,l0UploadBytes:2096);
        // Assert
        Assert.Equal(levelZero,admitted.Count(request=>request.Level==0));
        Assert.Equal(levelOne,admitted.Count(request=>request.Level==1));
        Assert.True(admitted.Sum(request=>request.Level==0?2096:1576)<=budget);
    }

    /// <summary>Verifies the build update list respects global trace budget behavior.</summary>
    [Fact]
    public void BuildUpdateList_RespectsGlobalTraceBudget()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        // Act
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);
        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [100, 100, 100],
            traceMaxProbesPerFrame: 10,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);
        // Assert
        Assert.InRange(list.Count, 0, 10);
    }
    /// <summary>Verifies the update list admits only complete probe uploads within the byte budget.</summary>
    [Fact]
    public void BuildUpdateList_RespectsUploadBudgetBytesPerFrame()
    {
        // Arrange
        const int atlasTexelsPerUpdate = 32;
        // These payload sizes are the upload-format contract. Keep them authored here so a
        // change to the scheduler's estimate cannot silently change the test's expectation.
        const int probeResolveVertexBytes = 40;
        const int tileResolveVertexBytes = 24;
        int estimatedBytes = probeResolveVertexBytes + (atlasTexelsPerUpdate * tileResolveVertexBytes);

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        int expectedMax = 7;
        int uploadBudget = (estimatedBytes * expectedMax) + (estimatedBytes - 1);
        // Act
        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [100, 100, 100],
            traceMaxProbesPerFrame: 10_000,
            uploadBudgetBytesPerFrame: uploadBudget,
            atlasTexelsPerUpdate: atlasTexelsPerUpdate);
        // Assert
        Assert.Equal(expectedMax, list.Count);
    }
    /// <summary>Verifies the build update list respects per level budgets behavior.</summary>
    [Fact]
    public void BuildUpdateList_RespectsPerLevelBudgets()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 4);
        // Act
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);
        List<LumOnWorldProbeUpdateRequest> list = scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [2, 3],
            traceMaxProbesPerFrame: 100,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32);
        // Assert
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
    #endregion

    #region Lifecycle and validation
    /// <summary>Verifies the complete moves probe from in flight to valid behavior.</summary>
    [Fact]
    public void Complete_MovesProbeFromInFlightToValid()
    {
        // Arrange
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
        // Act
        scheduler.Complete(req, frameIndex: 1, success: true);
        // Assert
        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));
        Assert.Equal(LumOnWorldProbeLifecycleState.Valid, lifecycle[req.StorageLinearIndex]);
    }

    /// <summary>Verifies the build update list continuously refreshes valid probes behavior.</summary>
    [Fact]
    public void BuildUpdateList_ContinuouslyRefreshesValidProbes()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 1);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);
        // Act
        LumOnWorldProbeUpdateRequest initial = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 0,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));
        // Assert
        Assert.Equal(LumOnWorldProbeImportanceFlags.None, initial.ImportanceFlags);
        // Act
        bool claimed = scheduler.TryClaim(initial, frameIndex: 0);
        scheduler.Complete(initial, frameIndex: 0, success: true);
        // Assert
        Assert.True(claimed);

        // Act
        LumOnWorldProbeUpdateRequest refresh = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));
        // Assert
        Assert.Equal(initial.StorageLinearIndex, refresh.StorageLinearIndex);
    }

    /// <summary>Verifies the build update list prioritizes higher importance valid probe behavior.</summary>
    [Fact]
    public void BuildUpdateList_PrioritizesHigherImportanceValidProbe()
    {
        // Arrange
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

        // Act
        foreach (LumOnWorldProbeUpdateRequest request in initial)
        {
            scheduler.Complete(request, frameIndex: 0, success: true);
        }

        // Act
        foreach (LumOnWorldProbeUpdateRequest request in initial)
        {
            LumOnWorldProbeImportanceFlags importanceFlags = request.LocalIndex.X > 0
                ? LumOnWorldProbeImportanceFlags.IndirectSunlight
                : LumOnWorldProbeImportanceFlags.None;
            Assert.True(scheduler.UpdateImportanceFlags(
                request.Level,
                request.StorageLinearIndex,
                importanceFlags,
                LumOnWorldProbeImportanceFlags.IndirectSunlight));
        }

        // Act
        LumOnWorldProbeUpdateRequest refresh = Assert.Single(scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: new Vec3d(0, 0, 0),
            baseSpacing: 1.0,
            perLevelProbeBudgets: [1],
            traceMaxProbesPerFrame: 1,
            uploadBudgetBytesPerFrame: 1_000_000,
            atlasTexelsPerUpdate: 32));
        // Assert
        Assert.Equal(1, refresh.LocalIndex.X);
        Assert.Equal(LumOnWorldProbeImportanceFlags.IndirectSunlight, refresh.ImportanceFlags);
        Assert.Equal(2f, LumOnWorldProbeImportance.ComputeFactor(refresh.ImportanceFlags));
    }

    /// <summary>Verifies the validate probe centers disables probe without scheduling trace behavior.</summary>
    [Fact]
    public void ValidateProbeCenters_DisablesProbeWithoutSchedulingTrace()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 2);
        scheduler.UpdateOrigins(new Vec3d(0, 0, 0), baseSpacing: 1.0);

        var validationResults = new List<LumOnWorldProbeScheduler.ProbeCenterValidation>();
        // Act
        scheduler.ValidateProbeCenters(
            baseSpacing: 1.0,
            perLevelProbeBudgets: [8],
            classifyProbeCenter: (_, _) => LumOnWorldProbeCenterOccupancy.InsideCollision,
            validationResults);

        var lifecycle = new LumOnWorldProbeLifecycleState[scheduler.ProbesPerLevel];
        // Assert
        Assert.True(scheduler.TryCopyLifecycleStates(level: 0, lifecycle));
        Assert.All(lifecycle, state => Assert.Equal(LumOnWorldProbeLifecycleState.Disabled, state));
        Assert.Equal(scheduler.ProbesPerLevel, validationResults.Count);
    }
    #endregion

    #endregion
}
