using System;
using System.Numerics;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal sealed class LumOnWorldProbeTraceIntegrator
{
    private const float ShC0 = 0.282095f;
    private const float ShC1 = 0.488603f;

    // Simple bounce model (Phase 18): treat skylight as an incident diffuse source and emit a Lambertian
    // reflected radiance from hit surfaces.
    //
    // The initial bring-up used a hard "up-only" gate (HitFaceNormal.Y), which systematically under-lit
    // vertical faces. We now estimate skylight visibility with a small set of secondary traces toward
    // the sky hemisphere and use that to drive bounce energy for all face orientations.
    private const float InvPi = 0.318309886f;
    private static readonly Vector3 SkyBounceTint = Vector3.One;

    // Secondary "sky visibility" traces per hit are intentionally low-count and deterministic.
    // These directions are uniform-ish over the +Y hemisphere; visibility is traced against the voxel scene.
    private const int SkyBounceSampleCount = 2;
    private const double SkyBounceMaxDistanceClamp = 16.0;
    private const double SkyBounceOriginEpsilon = 1e-3;

    private static readonly Vector3[] SkyBounceSampleDirections = BuildSkyBounceSampleDirections();
    private static readonly float SkyBounceNormalizationDenom = ComputeSkyBounceNormalizationDenom();

    public LumOnWorldProbeTraceResult TraceProbe(IWorldProbeTraceScene scene, in LumOnWorldProbeTraceWorkItem item, CancellationToken cancellationToken)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        ReadOnlySpan<Vector3> directions = LumOnWorldProbeTraceDirections.GetDirections();
        const int DirectionCount = LumOnWorldProbeTraceDirections.DirectionCount;

        // Uniform weights over the sphere.
        const float w = (float)(4.0 * Math.PI / LumOnWorldProbeTraceDirections.DirectionCount);

        Vector4 shR = Vector4.Zero;
        Vector4 shG = Vector4.Zero;
        Vector4 shB = Vector4.Zero;
        Vector4 shSky = Vector4.Zero;

        float skyIntensitySum = 0f;
        int skyIntensityCount = 0;

        Vector3 bent = Vector3.Zero;
        int unoccludedCount = 0;

        double hitDistSum = 0;
        int hitCount = 0;

        for (int i = 0; i < DirectionCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Vector3 dir = directions[i];

            var outcome = scene.Trace(item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, cancellationToken, out var hitInfo);
            if (outcome == WorldProbeTraceOutcome.Aborted)
            {
                // World data was not safely readable (e.g., placeholder chunk data). Don't upload synthetic lighting.
                // Mark this probe as failed so it is retried later.
                return new LumOnWorldProbeTraceResult(
                    FrameIndex: item.FrameIndex,
                    Request: item.Request,
                    Success: false,
                    FailureReason: WorldProbeTraceFailureReason.Aborted,
                    ShR: Vector4.Zero,
                    ShG: Vector4.Zero,
                    ShB: Vector4.Zero,
                    ShSky: Vector4.Zero,
                    SkyIntensity: 0f,
                    ShortRangeAoDirWorld: Vector3.UnitY,
                    ShortRangeAoConfidence: 0f,
                    Confidence: 0f,
                    MeanLogHitDistance: 0f);
            }

            bool hit = outcome == WorldProbeTraceOutcome.Hit;
            double hitDist = hitInfo.HitDistance;

            Vector3 specularF0 = Vector3.Zero;
            Vector3 blockRadiance = hit
                ? EvaluateHitRadiance(scene, item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, hitInfo, cancellationToken, out specularF0)
                : EvaluateSkyRadiance(dir);

            // @todo (WorldProbes): Define the correct specular-GI path (separate SH vs directional lobe)
            // before consuming specularF0. For now, we thread it through the hit evaluation so that
            // specular integration can be added without re-plumbing the hit shading path.

            float skyVisibility = hit ? 0f : 1f;
            if (hit)
            {
                skyIntensitySum += EvaluateSkyLightIntensity(hitInfo);
                skyIntensityCount++;
            }

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
            shSky += bw * skyVisibility;

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
        float aoConfidence = (float)unoccludedCount / DirectionCount;

        float confidence = ComputeUnifiedConfidence(aoConfidence, hitCount, DirectionCount);

        float skyIntensity;
        if (skyIntensityCount > 0)
        {
            skyIntensity = Math.Clamp(skyIntensitySum / skyIntensityCount, 0f, 1f);
        }
        else
        {
            // Pure-sky probes: treat as full sky intensity.
            skyIntensity = 1f;
        }

        float meanLogDist = 0;
        if (hitCount > 0)
        {
            double mean = hitDistSum / hitCount;
            meanLogDist = (float)Math.Log(mean + 1.0);
        }

        return new LumOnWorldProbeTraceResult(
            FrameIndex: item.FrameIndex,
            Request: item.Request,
            Success: true,
            FailureReason: WorldProbeTraceFailureReason.None,
            ShR: shR,
            ShG: shG,
            ShB: shB,
            ShSky: shSky,
            SkyIntensity: skyIntensity,
            ShortRangeAoDirWorld: aoDir,
            ShortRangeAoConfidence: aoConfidence,
            Confidence: confidence,
            MeanLogHitDistance: meanLogDist);
    }

    private static Vector3 EvaluateHitRadiance(
        IWorldProbeTraceScene scene,
        Vector3d probePosWorld,
        Vector3 primaryDirWorld,
        double maxTraceDistanceWorld,
        in LumOnWorldProbeTraceHit hit,
        CancellationToken cancellationToken,
        out Vector3 specularF0)
    {
        DerivedSurface ds;
        if (!PbrMaterialRegistry.Instance.TryGetDerivedSurface(hit.HitBlockId, (byte)hit.HitFace, out ds))
        {
            ds = DerivedSurface.Default;
        }

        specularF0 = ds.SpecularF0;

        // Vintage Story (client) encodes block light as HSV -> RGB already scaled by brightness,
        // returning normalized RGB in [0, 1]. W is sampled from SunLightLevels[level].
        Vector4 ls = hit.SampleLightRgbS;

        Vector3 blockLight = new(
            Math.Clamp(ls.X, 0f, 1f),
            Math.Clamp(ls.Y, 0f, 1f),
            Math.Clamp(ls.Z, 0f, 1f));

        float skyI = Math.Clamp(ls.W, 0f, 1f);

        float skyBounceFactor = 0f;
        if (skyI > 1e-6f && ds.DiffuseAlbedo.LengthSquared() > 1e-12f)
        {
            skyBounceFactor = EstimateSkyBounceFactor(
                scene,
                probePosWorld,
                primaryDirWorld,
                maxTraceDistanceWorld,
                hit,
                cancellationToken);
        }

        // Approximate outgoing radiance from a sun/sky lit diffuse surface.
        // Apply per-face diffuse albedo from the registry-derived surface terms.
        Vector3 skyBounce = SkyBounceTint * (skyI * InvPi * skyBounceFactor);
        skyBounce *= ds.DiffuseAlbedo;

        return blockLight + skyBounce;
    }

    private static float EstimateSkyBounceFactor(
        IWorldProbeTraceScene scene,
        Vector3d probePosWorld,
        Vector3 primaryDirWorld,
        double maxTraceDistanceWorld,
        in LumOnWorldProbeTraceHit hit,
        CancellationToken cancellationToken)
    {
        // NOTE: We assume "sky" is the +Y hemisphere in world space.
        // Estimate cosine-weighted visibility from the hit point toward that hemisphere and normalize
        // relative to an upward-facing surface with full sky visibility.
        //
        // For face normals with Y <= 0, dot(n, dirSky) is <= 0 for all dirSky.Y > 0, so the factor is 0
        // (down-facing surfaces don't receive direct skylight in this model).
        if (SkyBounceNormalizationDenom <= 1e-6f)
        {
            return 0f;
        }

        var n = new Vector3(hit.HitFaceNormal.X, hit.HitFaceNormal.Y, hit.HitFaceNormal.Z);
        if (n.LengthSquared() < 1e-12f)
        {
            return 0f;
        }

        double maxDist = Math.Min(maxTraceDistanceWorld, SkyBounceMaxDistanceClamp);
        if (maxDist <= 0)
        {
            return 0f;
        }

        // Primary ray hit world-space position, then offset slightly outward from the hit face.
        var hitPosWorld = probePosWorld + new Vector3d(primaryDirWorld.X * hit.HitDistance, primaryDirWorld.Y * hit.HitDistance, primaryDirWorld.Z * hit.HitDistance);
        var originWorld = hitPosWorld + new Vector3d(n.X * SkyBounceOriginEpsilon, n.Y * SkyBounceOriginEpsilon, n.Z * SkyBounceOriginEpsilon);

        float accum = 0f;

        for (int i = 0; i < SkyBounceSampleDirections.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Vector3 dir = SkyBounceSampleDirections[i];

            float cos = Vector3.Dot(n, dir);
            if (cos <= 0f)
            {
                continue;
            }

            // Secondary trace: treat a miss as "sky-visible" and any hit/abort as occluded.
            var outcome = scene.Trace(originWorld, dir, maxDist, cancellationToken, out _);
            if (outcome == WorldProbeTraceOutcome.Miss)
            {
                accum += cos;
            }
        }

        float factor = accum / SkyBounceNormalizationDenom;
        return Math.Clamp(factor, 0f, 1f);
    }

    private static Vector3[] BuildSkyBounceSampleDirections()
    {
        var dirs = new Vector3[SkyBounceSampleCount];

        // Fibonacci spiral on a hemisphere: deterministic, low-discrepancy-ish.
        // We map the hemisphere's polar axis to +Y so "sky" is world-up.
        const float goldenAngle = 2.39996323f; // ~pi*(3-sqrt(5))

        for (int i = 0; i < dirs.Length; i++)
        {
            float t = (i + 0.5f) / dirs.Length; // in (0,1)
            float y = t;                        // cos(theta) in [0,1]
            float r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));

            float phi = i * goldenAngle;

            float x = r * MathF.Cos(phi);
            float z = r * MathF.Sin(phi);

            dirs[i] = new Vector3(x, y, z);
        }

        return dirs;
    }

    private static float ComputeSkyBounceNormalizationDenom()
    {
        // Normalization target: cosine-weighted integral of the sky hemisphere for an upward-facing surface,
        // approximated with the discrete sample set. Since all samples are in +Y, dot(up,dir)=dir.Y.
        float sum = 0f;
        for (int i = 0; i < SkyBounceSampleDirections.Length; i++)
        {
            sum += Math.Max(SkyBounceSampleDirections[i].Y, 0f);
        }
        return sum;
    }

    private static float EvaluateSkyLightIntensity(in LumOnWorldProbeTraceHit hit)
    {
        return Math.Clamp(hit.SampleLightRgbS.W, 0f, 1f);
    }

    private static Vector3 EvaluateSkyRadiance(Vector3 dir)
    {
        // Phase 18 (Option B): keep *sky radiance* out of RGB SH and represent it only via:
        // - ShSky (sky visibility, projected into L1)
        // - SkyIntensity (separate scalar packed alongside AO)
        // - worldProbeSkyTint (published via LumOnWorldProbeUBO, time-of-day/weather/ambient tint)
        //
        // This avoids double-counting sky (RGB + ShSky) and keeps world-probe sky color consistent
        // with the engine's ambient/sky settings.
        _ = dir;
        return Vector3.Zero;
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
