using System.Numerics;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceResult(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    bool Success,
    WorldProbeTraceFailureReason FailureReason,
    Vector4 ShR,
    Vector4 ShG,
    Vector4 ShB,
    Vector4 ShSky,
    Vector3 ShortRangeAoDirWorld,
    float ShortRangeAoConfidence,
    float Confidence,
    float MeanLogHitDistance);
