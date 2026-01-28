using System;
using System.Numerics;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal sealed class LumOnWorldProbeTraceIntegrator
{
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

        int s = Math.Max(1, item.WorldProbeOctahedralTileSize);
        int dirCount = checked(s * s);
        int k = Math.Clamp(item.WorldProbeAtlasTexelsPerUpdate, 1, dirCount);

        var directions = LumOnWorldProbeAtlasDirections.GetDirections(s);
        var samples = new LumOnWorldProbeAtlasSample[k];

        Span<int> texelIndicesScratch = k <= 256 ? stackalloc int[256] : new int[k];
        texelIndicesScratch = texelIndicesScratch.Slice(0, k);

        int probeId = item.Request.StorageLinearIndex;
        int texelCount = LumOnWorldProbeAtlasDirectionSlicing.FillTexelIndicesForUpdate(
            frameIndex: item.FrameIndex,
            probeStorageLinearIndex: probeId,
            octahedralSize: s,
            texelsPerUpdate: k,
            destination: texelIndicesScratch);

        float skyIntensitySum = 0f;
        int skyIntensityCount = 0;

        Vector3 bent = Vector3.Zero;
        int unoccludedCount = 0;

        double hitDistSum = 0;
        int hitCount = 0;

        float missAlpha = -(float)Math.Log(item.MaxTraceDistanceWorld + 1.0);

        int usedSamples = 0;

        for (int i = 0; i < texelCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int texelIndex = texelIndicesScratch[i];

            int octX = texelIndex % s;
            int octY = texelIndex / s;

            Vector3 dir = directions[texelIndex];

            var outcome = scene.Trace(item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, cancellationToken, out var hitInfo);
            if (outcome == WorldProbeTraceOutcome.Aborted)
            {
                return new LumOnWorldProbeTraceResult(
                    FrameIndex: item.FrameIndex,
                    Request: item.Request,
                    Success: false,
                    FailureReason: WorldProbeTraceFailureReason.Aborted,
                    AtlasSamples: Array.Empty<LumOnWorldProbeAtlasSample>(),
                    SkyIntensity: 0f,
                    ShortRangeAoDirWorld: Vector3.UnitY,
                    ShortRangeAoConfidence: 0f,
                    Confidence: 0f,
                    MeanLogHitDistance: 0f);
            }

            bool hit = outcome == WorldProbeTraceOutcome.Hit;
            double hitDist = hitInfo.HitDistance;

            Vector3 radianceRgb;
            float alphaSigned;

            if (hit)
            {
                Vector3 specularF0 = Vector3.Zero;
                radianceRgb = EvaluateHitRadiance(scene, item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, hitInfo, cancellationToken, out specularF0);
                alphaSigned = (float)Math.Log(Math.Max(0.0, hitDist) + 1.0);

                skyIntensitySum += EvaluateSkyLightIntensity(hitInfo);
                skyIntensityCount++;
            }
            else
            {
                // Miss semantics: sky color is uniform-driven in shader; store sky visibility via signed alpha.
                radianceRgb = Vector3.Zero;
                alphaSigned = missAlpha;
            }

            samples[usedSamples++] = new LumOnWorldProbeAtlasSample(
                OctX: octX,
                OctY: octY,
                RadianceRgb: radianceRgb,
                AlphaEncodedDistSigned: alphaSigned);

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

        int sampleCountForConfidence = Math.Max(1, usedSamples);
        float aoConfidence = (float)unoccludedCount / sampleCountForConfidence;
        float confidence = ComputeUnifiedConfidence(aoConfidence, hitCount, sampleCountForConfidence);

        float skyIntensity;
        if (skyIntensityCount > 0)
        {
            skyIntensity = Math.Clamp(skyIntensitySum / skyIntensityCount, 0f, 1f);
        }
        else
        {
            skyIntensity = 1f;
        }

        float meanLogDist = 0;
        if (hitCount > 0)
        {
            double mean = hitDistSum / hitCount;
            meanLogDist = (float)Math.Log(mean + 1.0);
        }

        if (usedSamples != samples.Length)
        {
            Array.Resize(ref samples, usedSamples);
        }

        return new LumOnWorldProbeTraceResult(
            FrameIndex: item.FrameIndex,
            Request: item.Request,
            Success: true,
            FailureReason: WorldProbeTraceFailureReason.None,
            AtlasSamples: samples,
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

    // Sky radiance is uniform-driven in the shader; misses store zero radiance and signed-alpha sky visibility.

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
