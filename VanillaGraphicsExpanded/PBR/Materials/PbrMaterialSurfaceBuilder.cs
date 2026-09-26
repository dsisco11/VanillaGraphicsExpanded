using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Builds representative lighting surfaces from resolved BRDF values and texture-derived color.</summary>
internal static class PbrMaterialSurfaceBuilder
{
    #region Surface construction
    /// <summary>Applies resolved mapping scales once and preserves the existing diffuse/Fresnel convention.</summary>
    public static PbrMaterialSurface Build(in BRDFProperties properties, Vector3 averageTextureColor)
    {
        // Metallic controls the diffuse/specular split of the representative texture color.
        Vector3 color = Vector3.Clamp(averageTextureColor, Vector3.Zero, Vector3.One);
        float metallic = Math.Clamp(properties.Metallic * properties.Scale.Metallic, 0f, 1f);
        return new PbrMaterialSurface(
            Math.Clamp(properties.Roughness * properties.Scale.Roughness, 0f, 1f), metallic,
            Math.Clamp(properties.Emissive * properties.Scale.Emissive, 0f, 1f),
            Vector3.Clamp(color * (1f - metallic), Vector3.Zero, Vector3.One),
            Vector3.Clamp(Vector3.Lerp(new Vector3(0.04f), color, metallic), Vector3.Zero, Vector3.One));
    }

    #endregion
}
