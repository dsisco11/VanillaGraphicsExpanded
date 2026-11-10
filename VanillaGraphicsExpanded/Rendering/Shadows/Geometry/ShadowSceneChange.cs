using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Describes a scene-owner change, not a second terrain membership record.</summary>
internal readonly record struct ShadowSceneChange(long Sequence, ShadowSourceKey Source,
    ShadowBounds? Prior, ShadowBounds? Current, long GeometryRevision, long MaterialRevision, long DeformationRevision);

