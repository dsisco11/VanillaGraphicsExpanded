using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceHit(
    double HitDistance,
    Vec3i HitBlockPos,
    Vec3i HitFaceNormal,
    Vec3i SampleBlockPos,
    Vec4f SampleLightRgbS);
