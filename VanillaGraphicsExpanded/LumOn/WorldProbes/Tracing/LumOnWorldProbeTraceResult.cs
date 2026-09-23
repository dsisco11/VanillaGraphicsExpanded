using VanillaGraphicsExpanded.LumOn.Scene;
using System.Numerics;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>One directional result, optionally awaiting render-thread surface-cache lighting.</summary>
internal readonly record struct LumOnWorldProbeAtlasSample(
    int OctX,
    int OctY,
    Vector3 RadianceRgb,
    float AlphaEncodedDistSigned, SurfaceLightingQuery? SurfaceHit = null);

/// <summary>CPU trace geometry and metadata tagged with the surface dependency used for admission.</summary>
internal readonly record struct LumOnWorldProbeTraceResult(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    bool Success,
    WorldProbeTraceFailureReason FailureReason,
    LumOnWorldProbeAtlasSample[] AtlasSamples,
    float SkyIntensity,
    Vector3 ShortRangeAoDirWorld,
    float ShortRangeAoConfidence,
    float Confidence,
    float MeanLogHitDistance,
    LumOnWorldProbeImportanceFlags ImportanceFlags, long SurfaceRevision = 0);
