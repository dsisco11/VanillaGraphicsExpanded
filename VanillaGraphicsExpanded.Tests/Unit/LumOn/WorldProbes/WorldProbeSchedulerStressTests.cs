using System;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSchedulerStressTests
{
    [Fact]
    public void BuildUpdateList_Stress_DoesNotExceedBudgetsOverManyFrames()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 3, resolution: 8);
        const double baseSpacing = 2.0;

        // Generous per-level budgets; global budgets should dominate.
        int[] perLevel = [10_000, 10_000, 10_000];
        const int traceMax = 256;
        const int uploadBudgetBytes = 256 * 64;

        var cam = new Vec3d(0, 0, 0);

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
                uploadBudgetBytesPerFrame: uploadBudgetBytes);

            Assert.InRange(list.Count, 0, traceMax);

            // Complete everything to avoid accumulating InFlight forever.
            foreach (var req in list)
            {
                scheduler.Complete(req, frameIndex: frame, success: true);
            }
        }
    }
}
