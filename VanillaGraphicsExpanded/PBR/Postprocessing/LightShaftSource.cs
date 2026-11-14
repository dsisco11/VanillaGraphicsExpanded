using System.Numerics;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Describes a projected emitter and the physical reach of its shaft occlusion.</summary>
/// <param name="ScreenPosition">Normalized projected emitter position.</param>
/// <param name="Visibility">Admitted emitter visibility.</param>
/// <param name="AspectRatio">Source image width divided by height.</param>
/// <param name="Tint">Normalized emitter color.</param>
/// <param name="MaximumOcclusionDistance">Maximum scene distance considered by shaft occlusion.</param>
internal readonly record struct LightShaftSource(Vector2 ScreenPosition, float Visibility,
    float AspectRatio, Vector3 Tint, float MaximumOcclusionDistance = 128);
