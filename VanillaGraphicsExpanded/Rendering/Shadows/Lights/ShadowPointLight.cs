using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Retains native RGB or authored candela, view-space position and matching source key.</summary>
internal readonly record struct ShadowPointLight(ShadowSourceKey Source, Vector3 ViewPosition, Vector4 Payload)
{
    /// <summary>Shares the illumination support without a shadow-only cutoff.</summary>
    internal float Radius => ShadowLightSnapshot.InfluenceRadius(Payload);
}

