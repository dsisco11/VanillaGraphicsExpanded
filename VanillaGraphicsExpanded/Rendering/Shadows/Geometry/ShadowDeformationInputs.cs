using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Frozen deformation values shared by terrain and object material packing.</summary>
/// <param name="ReferenceOrigin">Native deformation reference position.</param>
/// <param name="Animation">Animation and wind/liquid phase counters.</param>
/// <param name="Wind">Wind speed, world warp, glitch and wind strength.</param>
/// <param name="LiquidStrength">Liquid deformation strength.</param>
/// <param name="PerceptionMode">Native perception-effect selector.</param>
/// <param name="PerceptionStrength">Perception-effect strength.</param>
internal readonly record struct ShadowDeformationInputs(Vector3 ReferenceOrigin,
    Vector4 Animation, Vector4 Wind, float LiquidStrength, int PerceptionMode, float PerceptionStrength);
