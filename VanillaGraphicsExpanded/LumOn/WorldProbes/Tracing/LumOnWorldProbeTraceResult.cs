using System.Numerics;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeAtlasSample(
    int OctX,
    int OctY,
    Vector3 RadianceRgb,
    float AlphaEncodedDistSigned);

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
    float MeanLogHitDistance);
