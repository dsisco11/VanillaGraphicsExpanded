using System.Numerics;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceResult(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    Vector4 ShR,
    Vector4 ShG,
    Vector4 ShB,
    Vector4 ShSky,
    Vec3f ShortRangeAoDirWorld,
    float ShortRangeAoConfidence,
    float Confidence,
    float MeanLogHitDistance);
