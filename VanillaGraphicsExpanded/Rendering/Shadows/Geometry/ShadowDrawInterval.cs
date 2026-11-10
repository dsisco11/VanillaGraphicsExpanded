namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>CPU-known bounded command interval for one native-compatible group and destination array.</summary>
internal readonly record struct ShadowDrawInterval(int Group, int Array, int FirstCommand, int Count);
