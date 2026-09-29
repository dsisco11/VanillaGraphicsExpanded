using System;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Publishes bounded material transmission without changing RGB override masks or cached RGB tiles.</summary>
internal static class MaterialTransmission
{
    #region Material resolution
    /// <summary>Rejects nonfinite strengths and bounds the fraction of transmitted sunlight.</summary>
    internal static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    /// <summary>Resolves the author-selected material for a texture, including override-only atlas jobs.</summary>
    internal static float Resolve(AssetLocation texture)
        => PbrMaterialRegistry.Instance.TryGetMaterial(texture, out var material) ? Clamp(material.Transmission) : 0;
    #endregion

    #region GPU publication
    /// <summary>Combines cached RGB properties with the current material's transmission for RGBA upload.</summary>
    internal static float[] Pack(float[] rgb, float transmission = 0)
    {
        if (rgb.Length % 3 != 0) throw new ArgumentException("Material RGB tiles must contain complete pixels.", nameof(rgb));
        float strength = Clamp(transmission);
        var rgba = new float[checked(rgb.Length / 3 * 4)];
        // RGB caches and override alpha masks keep their existing contract; transmission is independently authored.
        for (int source = 0, target = 0; source < rgb.Length; source += 3, target += 4)
        {
            rgba[target] = rgb[source];
            rgba[target + 1] = rgb[source + 1];
            rgba[target + 2] = rgb[source + 2];
            rgba[target + 3] = strength;
        }
        return rgba;
    }
    #endregion
}
