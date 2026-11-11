namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>CPU-known bounded command interval for one native-compatible group, independent of physical banks.</summary>
internal readonly record struct ShadowDrawInterval(int Group, int FirstCommand, int Count);
