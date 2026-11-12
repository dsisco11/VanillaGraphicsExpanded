using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>Conservative current receiving bounds including deformation and every contributing transparent layer.</summary>
internal readonly record struct ShadowReceiverRegion(ShadowSurfaceCategory Category, ShadowBounds Bounds);
