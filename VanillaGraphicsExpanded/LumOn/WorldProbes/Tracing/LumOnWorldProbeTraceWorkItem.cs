using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal readonly record struct LumOnWorldProbeTraceWorkItem(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    Vector3d ProbePosWorld,
    double MaxTraceDistanceWorld,
    int WorldProbeOctahedralTileSize,
    int WorldProbeAtlasTexelsPerUpdate);
