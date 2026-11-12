using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Associates one complete page enclosure with the immutable virtual region used to render it.</summary>
internal readonly record struct ShadowPageReplacement(int View, ShadowPageRequest Request);
