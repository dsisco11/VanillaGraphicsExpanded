using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceWorkItem(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    Vec3d ProbePosWorld,
    double MaxTraceDistanceWorld);
