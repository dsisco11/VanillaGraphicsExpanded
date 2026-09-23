using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Immutable worker admission, including the intended render-thread lighting revision.</summary>
internal readonly record struct LumOnWorldProbeTraceWorkItem(
    int FrameIndex,
    LumOnWorldProbeUpdateRequest Request,
    Vector3d ProbePosWorld,
    double MaxTraceDistanceWorld,
    int WorldProbeOctahedralTileSize,
    int WorldProbeAtlasTexelsPerUpdate,
    bool EnableDirectionPIS,
    float DirectionPISExploreFraction,
    int DirectionPISExploreCount,
    float DirectionPISWeightEpsilon,
    double NearbySolidHitDistance = 0d, bool DeferSurfaceLighting = false, long SurfaceRevision = 0);
