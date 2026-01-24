using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceHit(
    double HitDistance,
    int HitBlockId,
    ProbeHitFace HitFace,
    VectorInt3 HitBlockPos,
    VectorInt3 HitFaceNormal,
    VectorInt3 SampleBlockPos,
    Vector4 SampleLightRgbS);

// TODO(WorldProbes): Consider extending hit metadata for future material/shading upgrades
// (e.g., liquid/transparent flags, block entity/material group, etc.).
