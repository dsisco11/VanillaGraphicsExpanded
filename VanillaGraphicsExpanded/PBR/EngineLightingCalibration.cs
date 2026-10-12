using System;
using System.Numerics;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Refreshes shader interpretation only when the nominated engine decoder inputs change.</summary>
internal sealed class EngineLightingCalibration
{
    private (float Floor, float Adjacent, float Source, float SkyFloor, float SkyFull, byte Hue, byte Saturation)? previous;
    private LightingCalibration current = LightingCalibration.Default;

    #region Public API
    /// <summary>Returns matching block, point and sky references without modifying engine light tables.</summary>
    internal LightingCalibration Capture(float[] blockLevels, float[] sunLevels, byte[] hues, byte[] saturations)
    {
        if (blockLevels.Length < 15 || sunLevels.Length < 32 || hues.Length < 5 || saturations.Length < 5)
            throw new ArgumentException("Active engine light tables omit the nominated torch decoder.");
        var key = (blockLevels[0], blockLevels[13], blockLevels[14], sunLevels[0], sunLevels[31], hues[4], saturations[4]);
        if (previous == key) return current;
        // Fit the adjacent-air field and collected dynamic payload independently. A table change
        // updates both shader references atomically; neither creates source-resolved propagation.
        Vector3 adjacent = Decode(key.Item6, key.Item7, key.Item2, true);
        Vector3 payload = Decode(key.Item6, key.Item7, key.Item3, false) * 14;
        var terrain = LightingCalibration.FromDecodedTorch(adjacent, payload, Quantize(key.Item1, true),
            Quantize(key.Item4, true), Quantize(key.Item5, true), key.Item4, key.Item5);
        var accessor = LightingCalibration.FromDecodedTorch(Decode(key.Item6, key.Item7, key.Item2, false), payload,
            Quantize(key.Item1, false), terrain.TerrainSkyFloor, terrain.TerrainSkyFull, key.Item4, key.Item5);
        current = terrain with { AccessorBlockReferenceLux = accessor.BlockReferenceLux,
            AccessorBlockNeutralFloor = accessor.BlockNeutralFloor };
        previous = key;
        return current;
    }
    #endregion

    #region Private
    /// <summary>Matches byte-normalized engine HSV decoding without applying a display transfer.</summary>
    private static Vector3 Decode(byte hue, byte saturation, float level, bool terrain)
    {
        _ = Quantize(level, terrain);
        int rgb = ColorUtil.HsvToRgb(hue, saturation, (int)(level * (terrain ? 255.999f : 255f)));
        return new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
    /// <summary>Checks the active table domain before matching its truncated byte representation.</summary>
    private static float Quantize(float value, bool terrain)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        return (int)(value * (terrain ? 255.999f : 255f)) / 255f;
    }
    #endregion
}
