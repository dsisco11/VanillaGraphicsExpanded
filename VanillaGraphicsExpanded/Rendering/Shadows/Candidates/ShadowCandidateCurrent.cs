using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Supplies current dependencies; projection changes deliberately do not imply association changes.</summary>
internal readonly record struct ShadowCandidateCurrent(ShadowSourceKey Source, long WorldOwner,
    long DomainGeneration, long InfluenceGeneration, ShadowBounds Receiver, Vector3d PositionOrDirection,
    long ChangeEpoch, long ChangeSequence, long ProjectionGeneration);
