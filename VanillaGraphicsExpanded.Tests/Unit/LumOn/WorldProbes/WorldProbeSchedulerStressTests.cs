using System;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks world probe scheduler stress behavior and contract rules.</summary>
public sealed class WorldProbeSchedulerStressTests
{
    #region Public API
    /// <summary>Verifies the global trace-count cap remains respected over repeated scheduler frames.</summary>
    [Fact]
    public void BuildUpdateList_Stress_RespectsTraceCapOverManyFrames()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 8);
        const double baseSpacing = 2.0;

        // Per-level and upload budgets intentionally stay above the trace cap so this
        // repeated-frame case isolates the global trace-count limit.
        int[] perLevel = [10_000, 10_000, 10_000];
        const int traceMax = 256;
        const int uploadBudgetBytes = 256 * 1024;
        const int atlasTexelsPerUpdate = 32;

        var cam = new Vec3d(0, 0, 0);

        // Act
        for (int frame = 0; frame < 200; frame++)
        {
            // Small movement, then occasional boundary-crossing.
            if (frame % 40 == 0)
            {
                cam = new Vec3d(cam.X + baseSpacing, cam.Y, cam.Z);
            }
            else
            {
                cam = new Vec3d(cam.X + 0.1, cam.Y, cam.Z);
            }

            scheduler.UpdateOrigins(cam, baseSpacing);
            var list = scheduler.BuildUpdateList(
                frameIndex: frame,
                cameraPos: cam,
                baseSpacing: baseSpacing,
                perLevelProbeBudgets: perLevel,
                traceMaxProbesPerFrame: traceMax,
                uploadBudgetBytesPerFrame: uploadBudgetBytes,
                atlasTexelsPerUpdate: atlasTexelsPerUpdate);
            // Assert
            Assert.InRange(list.Count, 0, traceMax);

            // Complete everything to avoid accumulating InFlight forever.
            foreach (var req in list)
            {
                scheduler.Complete(req, frameIndex: frame, success: true);
            }
        }
    }
    #endregion
}
