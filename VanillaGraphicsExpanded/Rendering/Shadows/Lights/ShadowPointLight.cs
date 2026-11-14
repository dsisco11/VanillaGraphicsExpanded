using System.Numerics;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Retains illumination independently of the optional original world position needed by shadow projection.</summary>
internal readonly record struct ShadowPointLight(ShadowSourceKey Source, Vector3 ViewPosition, Vector4 Payload, Vector3d? WorldPosition)
{
    /// <summary>Shares the illumination support without a shadow-only cutoff.</summary>
    internal float Radius => ShadowLightSnapshot.InfluenceRadius(Payload);
}

