using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>One directional result, optionally awaiting render-thread surface-cache lighting.</summary>
internal readonly record struct LumOnWorldProbeAtlasSample(
    int OctX,
    int OctY,
    Vector3 RadianceRgb,
    float AlphaEncodedDistSigned, SurfaceLightingQuery? SurfaceHit = null, int GpuRayIndex = -1);

/// <summary>CPU trace metadata and immutable directional batches transferred from tracing workers to render-thread consumers.</summary>
internal readonly record struct LumOnWorldProbeTraceResult(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    bool Success,
    WorldProbeTraceFailureReason FailureReason,
    ImmutableArray<LumOnWorldProbeAtlasSample> AtlasSamples,
    float SkyIntensity,
    Vector3 ShortRangeAoDirWorld,
    float ShortRangeAoConfidence,
    float Confidence,
    float MeanLogHitDistance,
    LumOnWorldProbeImportanceFlags ImportanceFlags, long SurfaceRevision = 0,
    ImmutableArray<LumOnWorldProbeAtlasSample> RetrySamples = default, int SurfaceRetryCount = 0,
    WorldProbeGpuLease? GpuLease = null, WorldProbeCommitIdentity? GpuIdentity = null);
