using System;
using System.Numerics;
using System.Threading;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal sealed class LumOnWorldProbeTraceIntegrator
{
    private const float ShC0 = 0.282095f;
    private const float ShC1 = 0.488603f;

    // Vintage Story light levels are commonly in the range [0, 32].
    // Keep this conservative; callers clamp and the scale can be refined later.
    private const float LightLevelMax = 32f;

    public LumOnWorldProbeTraceResult TraceProbe(IWorldProbeTraceScene scene, in LumOnWorldProbeTraceWorkItem item, CancellationToken cancellationToken)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        ReadOnlySpan<Vec3f> directions = LumOnWorldProbeTraceDirections.GetDirections();
        int n = directions.Length;

        // Uniform weights over the sphere.
        float w = (float)(4.0 * Math.PI / n);

        Vector4 shR = Vector4.Zero;
        Vector4 shG = Vector4.Zero;
        Vector4 shB = Vector4.Zero;
        Vector4 shSky = Vector4.Zero;

        Vec3f bent = new(0, 0, 0);
        int unoccludedCount = 0;

        double hitDistSum = 0;
        int hitCount = 0;

        for (int i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Vec3f dir = directions[i];

            bool hit = scene.Trace(item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, cancellationToken, out var hitInfo);
            double hitDist = hitInfo.HitDistance;

            Vec3f blockRadiance = hit
                ? EvaluateBlockLightRadiance(hitInfo)
                : new Vec3f(0, 0, 0);

            float skyIntensity = hit
                ? EvaluateSkyLightIntensity(hitInfo)
                : 1f;

            // SH L1 basis vector: (Y00, Y1-1(y), Y10(z), Y11(x))
            var basis = new Vector4(
                ShC0,
                ShC1 * dir.Y,
                ShC1 * dir.Z,
                ShC1 * dir.X);

            Vector4 bw = basis * w;

            shR += bw * blockRadiance.X;
            shG += bw * blockRadiance.Y;
            shB += bw * blockRadiance.Z;
            shSky += bw * skyIntensity;

            if (!hit)
            {
                bent.X += dir.X;
                bent.Y += dir.Y;
                bent.Z += dir.Z;
                unoccludedCount++;
            }
            else if (hitDist > 0)
            {
                hitDistSum += hitDist;
                hitCount++;
            }
        }

        Vec3f aoDir = unoccludedCount > 0 ? bent.Normalize() : new Vec3f(0, 1, 0);
        float aoConfidence = (float)unoccludedCount / n;

        float confidence = ComputeUnifiedConfidence(aoConfidence, hitCount, n);

        float meanLogDist = 0;
        if (hitCount > 0)
        {
            double mean = hitDistSum / hitCount;
            meanLogDist = (float)Math.Log(mean + 1.0);
        }

        return new LumOnWorldProbeTraceResult(
            FrameIndex: item.FrameIndex,
            Request: item.Request,
            ShR: shR,
            ShG: shG,
            ShB: shB,
            ShSky: shSky,
            ShortRangeAoDirWorld: aoDir,
            ShortRangeAoConfidence: aoConfidence,
            Confidence: confidence,
            MeanLogHitDistance: meanLogDist);
    }

    private static Vec3f EvaluateBlockLightRadiance(in LumOnWorldProbeTraceHit hit)
    {
        // hit.SampleLightRgbS: XYZ = block light rgb, W = sun brightness.
        Vec4f ls = hit.SampleLightRgbS;

        return new Vec3f(
            Math.Clamp(ls.X / LightLevelMax, 0f, 1f),
            Math.Clamp(ls.Y / LightLevelMax, 0f, 1f),
            Math.Clamp(ls.Z / LightLevelMax, 0f, 1f));
    }

    private static float EvaluateSkyLightIntensity(in LumOnWorldProbeTraceHit hit)
    {
        return Math.Clamp(hit.SampleLightRgbS.W / LightLevelMax, 0f, 1f);
    }

    private static Vec3f EvaluateSkyRadiance(Vec3f dir)
    {
        // Placeholder sky model for Phase 18.6 bring-up.
        // Later phases can sample game lighting / sky / time-of-day.
        float t = Math.Clamp(dir.Y * 0.5f + 0.5f, 0f, 1f);

        Vec3f horizon = new(0.25f, 0.28f, 0.30f);
        Vec3f zenith = new(0.55f, 0.65f, 0.85f);

        return new Vec3f(
            horizon.X + (zenith.X - horizon.X) * t,
            horizon.Y + (zenith.Y - horizon.Y) * t,
            horizon.Z + (zenith.Z - horizon.Z) * t);
    }

    private static float ComputeUnifiedConfidence(float aoConfidence, int hitCount, int sampleCount)
    {
        // For Phase 18.6, treat confidence as a warm-up + stability proxy.
        // - If everything is occluded or everything is unoccluded, we still have a stable estimate.
        // - Use aoConfidence as a continuous signal.
        float sampleFrac = sampleCount > 0 ? (float)hitCount / sampleCount : 0f;
        float stable = 1f - Math.Abs(sampleFrac - 0.5f) * 2f; // highest near 50/50, lowest near extremes

        // Bias toward being conservative early.
        return Math.Clamp(0.25f + 0.75f * Math.Min(aoConfidence, stable), 0f, 1f);
    }
}
