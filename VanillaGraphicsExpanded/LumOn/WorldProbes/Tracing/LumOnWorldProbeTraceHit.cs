using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceHit(
    double HitDistance,
    VectorInt3 HitBlockPos,
    VectorInt3 HitFaceNormal,
    VectorInt3 SampleBlockPos,
    Vector4 SampleLightRgbS);
