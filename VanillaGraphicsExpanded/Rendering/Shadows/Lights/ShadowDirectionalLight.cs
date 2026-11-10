using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Preserves an independent celestial direction and calibrated direct irradiance.</summary>
internal readonly record struct ShadowDirectionalLight(ShadowSourceKey Source, Vector3 Direction, Vector3 Irradiance);
