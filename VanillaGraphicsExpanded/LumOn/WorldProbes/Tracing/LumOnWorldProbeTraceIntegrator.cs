using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene;
using System;
using System.Numerics;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Profiling;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Builds directional lighting batches only from resolved surface or sky rays.</summary>
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
    #region Public API
    /// <summary>Integrates resolved directions; incomplete primary traversal rejects the batch for retry.</summary>
    public LumOnWorldProbeTraceResult TraceProbe(IWorldProbeTraceScene scene, in LumOnWorldProbeTraceWorkItem item, CancellationToken cancellationToken)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        int s = Math.Max(1, item.WorldProbeOctahedralTileSize);
        int dirCount = checked(s * s);
        int k = Math.Clamp(item.WorldProbeAtlasTexelsPerUpdate, 1, dirCount);

        var directions = LumOnWorldProbeAtlasDirections.GetDirections(s);
        var samples = ImmutableArray.CreateBuilder<LumOnWorldProbeAtlasSample>(k);

        Span<int> texelIndicesScratch = k <= 256 ? stackalloc int[256] : new int[k];
        texelIndicesScratch = texelIndicesScratch.Slice(0, k);

        int texelCount;
        using (Profiler.BeginScope("LumOn.WorldProbe.DirectionSelect", "LumOn"))
            texelCount = WorldProbeTraceDirectionSelection.Fill(item, texelIndicesScratch);

        float skyIntensitySum = 0f;
        int skyIntensityCount = 0;

        Vector3 bent = Vector3.Zero;
        int unoccludedCount = 0;

        double hitDistSum = 0;
        int hitCount = 0;
        LumOnWorldProbeImportanceFlags importanceFlags = LumOnWorldProbeImportanceFlags.None;

        bool hasNearbySolidHit = (item.Request.ImportanceFlags & LumOnWorldProbeImportanceFlags.NearbySolidHit) != 0;
        if (item.NearbySolidHitDistance > 0d && !hasNearbySolidHit)
        {
            WorldProbeTraceOutcome nearbyOutcome = TraceNearbyCardinalSolid(
                scene,
                item,
                cancellationToken);
            // This query only asks about nearby occupancy: reaching its distance limit
            // answers that question, but never supplies sky lighting to the atlas.
            if (nearbyOutcome is not (WorldProbeTraceOutcome.Hit or WorldProbeTraceOutcome.Sky or WorldProbeTraceOutcome.DistanceLimit))
            {
                return CreateUnresolvedResult(item, nearbyOutcome);
            }

            if (nearbyOutcome == WorldProbeTraceOutcome.Hit)
            {
                importanceFlags |= LumOnWorldProbeImportanceFlags.NearbySolidHit;
            }
        }

        float skyAlpha = -(float)Math.Log(item.MaxTraceDistanceWorld + 1.0);

        int usedSamples = 0;

        for (int i = 0; i < texelCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int texelIndex = texelIndicesScratch[i];

            int octX = texelIndex % s;
            int octY = texelIndex / s;

            Vector3 dir = directions[texelIndex];

            var outcome = scene.Trace(item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, cancellationToken, out var hitInfo);
            if (outcome is not (WorldProbeTraceOutcome.Hit or WorldProbeTraceOutcome.Sky))
            {
                // There is no independent distant-light provider for world probes yet.
                // Preserve published history and retry instead of converting an incomplete ray to sky.
                return CreateUnresolvedResult(item, outcome);
            }

            bool hit = outcome == WorldProbeTraceOutcome.Hit;
            double hitDist = hitInfo.HitDistance;

            Vector3 radianceRgb;
            float alphaSigned;
            SurfaceLightingQuery? surfaceHit = null;

            if (hit)
            {
                if (item.NearbySolidHitDistance > 0d && hitDist <= item.NearbySolidHitDistance)
                {
                    importanceFlags |= LumOnWorldProbeImportanceFlags.NearbySolidHit;
                }
                Vector3 specularF0 = Vector3.Zero;
                if (item.DeferSurfaceLighting)
                {
                    // Preserve an integer anchor; only the within-cell hit fraction crosses as floats.
                    Vector3d point = item.ProbePosWorld + new Vector3d(dir.X * hitDist, dir.Y * hitDist, dir.Z * hitDist);
                    Vector3 fraction = new((float)(point.X-hitInfo.HitBlockPos.X), (float)(point.Y-hitInfo.HitBlockPos.Y), (float)(point.Z-hitInfo.HitBlockPos.Z));
                    surfaceHit = new SurfaceLightingQuery(hitInfo.HitBlockPos, hitInfo.HitFaceNormal, fraction, hitInfo.HitBlockId);
                    radianceRgb = Vector3.Zero;
                }
                else radianceRgb = EvaluateHitRadiance(scene, item.ProbePosWorld, dir, item.MaxTraceDistanceWorld, hitInfo, cancellationToken, out specularF0);
                // Zero alpha is the atlas's unresolved marker, including for valid black contact hits.
                alphaSigned = Math.Max(0.0001f, (float)Math.Log(Math.Max(0.0, hitDist) + 1.0));

                if (!item.DeferSurfaceLighting)
                {
                    skyIntensitySum += EvaluateSkyLightIntensity(hitInfo);
                    skyIntensityCount++;
                }
            }
            else
            {
                // Only established sky visibility may carry negative atlas alpha.
                radianceRgb = Vector3.Zero;
                alphaSigned = skyAlpha;
            }

            usedSamples++;
            samples.Add(new LumOnWorldProbeAtlasSample(
                OctX: octX,
                OctY: octY,
                RadianceRgb: radianceRgb,
                AlphaEncodedDistSigned: alphaSigned, SurfaceHit: surfaceHit));

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
        if (item.DeferSurfaceLighting || unoccludedCount > 0)
        {
            // Deferred hits carry no vanilla sunlight. Keep the sky multiplier neutral:
            // directional visibility controls sky, including directions retained from older batches.
            // Sky misses are encoded in the atlas as a special value and receive the
            // dynamic sky tint at gather time. Do not derive their intensity from
            // unrelated dark wall hits inside the probe's visibility field.
            skyIntensity = 1f;
        }
        else if (skyIntensityCount > 0)
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

        // Freeze the private builder before the result crosses the worker boundary.

        return new LumOnWorldProbeTraceResult(
            FrameIndex: item.FrameIndex,
            Request: item.Request,
            Success: true,
            FailureReason: WorldProbeTraceFailureReason.None,
            AtlasSamples: samples.Count == samples.Capacity ? samples.MoveToImmutable() : samples.ToImmutable(),
            SkyIntensity: skyIntensity,
            ShortRangeAoDirWorld: aoDir,
            ShortRangeAoConfidence: aoConfidence,
            Confidence: confidence,
            MeanLogHitDistance: meanLogDist,
            ImportanceFlags: importanceFlags, SurfaceRevision: item.SurfaceRevision);
    }

    #endregion

    #region Geometry completion
    /// <summary>Checks one nearby cardinal segment for scheduling importance, without evaluating distant lighting.</summary>
    private static WorldProbeTraceOutcome TraceNearbyCardinalSolid(
        IWorldProbeTraceScene scene,
        in LumOnWorldProbeTraceWorkItem item,
        CancellationToken cancellationToken)
    {
        return scene.Trace(
            item.ProbePosWorld,
            WorldProbeTraceDirectionSelection.CardinalDirection(WorldProbeTraceDirectionSelection.NearbyIndex(item)),
            item.NearbySolidHitDistance,
            cancellationToken,
            out _);
    }

    /// <summary>Retains the reason and admission revision for an incomplete batch without publishing samples.</summary>
    private static LumOnWorldProbeTraceResult CreateUnresolvedResult(in LumOnWorldProbeTraceWorkItem item, WorldProbeTraceOutcome outcome) => new(
        FrameIndex: item.FrameIndex,
        Request: item.Request,
        Success: false,
        FailureReason: outcome switch
        {
            WorldProbeTraceOutcome.Unavailable => WorldProbeTraceFailureReason.Aborted,
            WorldProbeTraceOutcome.DistanceLimit => WorldProbeTraceFailureReason.DistanceLimit,
            WorldProbeTraceOutcome.BudgetExhausted => WorldProbeTraceFailureReason.BudgetExhausted,
            _ => WorldProbeTraceFailureReason.Invalid,
        },
        AtlasSamples: ImmutableArray<LumOnWorldProbeAtlasSample>.Empty,
        SkyIntensity: 0f,
        ShortRangeAoDirWorld: Vector3.UnitY,
        ShortRangeAoConfidence: 0f,
        Confidence: 0f,
        MeanLogHitDistance: 0f,
        ImportanceFlags: LumOnWorldProbeImportanceFlags.None, SurfaceRevision: item.SurfaceRevision);
    #endregion

    #region Legacy lighting
    /// <summary>Evaluates the compatibility light estimate for consumers without deferred surface lighting.</summary>
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

    /// <summary>Estimates legacy sky bounce only from directions with established environment visibility.</summary>
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

            // Incomplete secondary rays provide no evidence of sky visibility.
            var outcome = scene.Trace(originWorld, dir, maxDist, cancellationToken, out _);
            if (outcome == WorldProbeTraceOutcome.Sky)
            {
                accum += cos;
            }
        }

        float factor = accum / SkyBounceNormalizationDenom;
        return Math.Clamp(factor, 0f, 1f);
    }

    /// <summary>Constructs deterministic directions over the upper hemisphere.</summary>
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

    /// <summary>Normalizes the legacy hemisphere samples for an upward-facing surface.</summary>
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

    /// <summary>Reads the bounded vanilla sunlight multiplier for the legacy path.</summary>
    private static float EvaluateSkyLightIntensity(in LumOnWorldProbeTraceHit hit)
    {
        return Math.Clamp(hit.SampleLightRgbS.W, 0f, 1f);
    }

    #endregion

    #region Confidence
    /// <summary>Estimates batch confidence from the distribution of resolved hits and sky directions.</summary>
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
    #endregion
}
