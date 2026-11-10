using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Identifies virtual depth independently of physical assignment and receiving coverage.</summary>
internal readonly record struct ShadowPageKey(ShadowSourceKey Source, long Projection, int FaceOrLevel, int X, int Y);
