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

        ReadOnlySpan<Vector3> directions = LumOnWorldProbeTraceDirections.GetDirections();
        int n = directions.Length;

        // Uniform weights over the sphere.
        float w = (float)(4.0 * Math.PI / n);

        Vector4 shR = Vector4.Zero;
        Vector4 shG = Vector4.Zero;
        Vector4 shB = Vector4.Zero;
        Vector4 shSky = Vector4.Zero;

        Vector3 bent = Vector3.Zero;
        int unoccludedCount = 0;

        double hitDistSum = 0;
        int hitCount = 0;

        for (int i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Vector3 dir = directions[i];

            bool hit = scene.Trace(item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, cancellationToken, out var hitInfo);
            double hitDist = hitInfo.HitDistance;

            Vector3 blockRadiance = hit
                ? EvaluateBlockLightRadiance(hitInfo)
                : Vector3.Zero;

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
                bent += dir;
                unoccludedCount++;
            }
            else if (hitDist > 0)
            {
                hitDistSum += hitDist;
                hitCount++;
            }
        }

        Vector3 aoDir;
        if (unoccludedCount > 0 && bent.LengthSquared() > 1e-12f)
        {
            aoDir = Vector3.Normalize(bent);
        }
        else
        {
            aoDir = Vector3.UnitY;
        }
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

    private static Vector3 EvaluateBlockLightRadiance(in LumOnWorldProbeTraceHit hit)
    {
        // hit.SampleLightRgbS: XYZ = block light rgb, W = sun brightness.
        Vector4 ls = hit.SampleLightRgbS;

        return new Vector3(
            Math.Clamp(ls.X / LightLevelMax, 0f, 1f),
            Math.Clamp(ls.Y / LightLevelMax, 0f, 1f),
            Math.Clamp(ls.Z / LightLevelMax, 0f, 1f));
    }

    private static float EvaluateSkyLightIntensity(in LumOnWorldProbeTraceHit hit)
    {
        return Math.Clamp(hit.SampleLightRgbS.W / LightLevelMax, 0f, 1f);
    }

    private static Vector3 EvaluateSkyRadiance(Vector3 dir)
    {
        // Placeholder sky model for Phase 18.6 bring-up.
        // Later phases can sample game lighting / sky / time-of-day.
        float t = Math.Clamp(dir.Y * 0.5f + 0.5f, 0f, 1f);

        Vector3 horizon = new(0.25f, 0.28f, 0.30f);
        Vector3 zenith = new(0.55f, 0.65f, 0.85f);

        return new Vector3(
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
