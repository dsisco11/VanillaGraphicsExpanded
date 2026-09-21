using System.Numerics;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Decodes packed chunk lighting with the same tables and RGB conversion as the engine accessor.</summary>
internal sealed class NearFieldLightDecoder
{
    private readonly float[] blockLevels, sunLevels;
    private readonly byte[] hues, saturations;

    #region Light conversion
    /// <summary>Retains the engine's light tables so runtime brightness changes remain observable.</summary>
    public NearFieldLightDecoder(float[] blockLevels, float[] sunLevels, byte[] hues, byte[] saturations)
    {
        this.blockLevels = blockLevels; this.sunLevels = sunLevels;
        this.hues = hues; this.saturations = saturations;
    }

    /// <summary>Preserves hue, saturation, block intensity and sunlight without allocating engine vectors.</summary>
    public Vector4 Decode(uint packed)
    {
        int rgb = ColorUtil.HsvToRgb(hues[(packed >> 10) & 63], saturations[(packed >> 16) & 7],
            (int)(blockLevels[(packed >> 5) & 31] * 255));
        return Vector4.Clamp(new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f,
            (rgb & 255) / 255f, sunLevels[packed & 31]), Vector4.Zero, Vector4.One);
    }
    #endregion
}
