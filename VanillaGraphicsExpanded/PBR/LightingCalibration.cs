using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Defines source quantities and the selected compatibility references in linear Rec.709 RGB.</summary>
internal readonly record struct LightingCalibration(float BlockReferenceLux, float BlockNeutralFloor,
    float PointCandelaPerUnit = LightingCalibration.EnginePointCandelaPerUnit, float TerrainSkyFloor = 7f / 255f,
    float TerrainSkyFull = 1, float AccessorSkyFloor = .03f, float AccessorSkyFull = 1,
    float AccessorBlockReferenceLux = 0, float AccessorBlockNeutralFloor = 0)
{
    #region Public API
    /// <summary>Source-derived normal-torch field reference; the propagated field remains engine-owned.</summary>
    internal static LightingCalibration Default => new(8.982904808459303f, 10f / 255f);
    /// <summary>Selected authored-emission and prelit-effect reference luminance in cd/m².</summary>
    internal const float EmissionReferenceNits = 32;
    /// <summary>Maps the original engine point RGB payload to evaluated candela.</summary>
    internal const float EnginePointCandelaPerUnit = .562774738350764f;
    /// <summary>Rejects unsupported calibration before publishing any shader inputs.</summary>
    internal void Validate()
    {
        if (!float.IsFinite(BlockReferenceLux) || BlockReferenceLux <= 0
            || !float.IsFinite(BlockNeutralFloor) || BlockNeutralFloor < 0 || BlockNeutralFloor >= 1
            || !float.IsFinite(PointCandelaPerUnit) || PointCandelaPerUnit <= 0
            || !float.IsFinite(AccessorBlockReferenceLux) || AccessorBlockReferenceLux < 0
            || !float.IsFinite(AccessorBlockNeutralFloor) || AccessorBlockNeutralFloor < 0 || AccessorBlockNeutralFloor >= 1
            || !ValidEndpoints(TerrainSkyFloor, TerrainSkyFull) || !ValidEndpoints(AccessorSkyFloor, AccessorSkyFull))
            throw new ArgumentOutOfRangeException(nameof(LightingCalibration));
    }

    /// <summary>Fits the nominated torch against active decoded table values, preserving the original engine field.</summary>
    internal static LightingCalibration FromDecodedTorch(Vector3 adjacentBlock, Vector3 dynamicPayload,
        float neutralFloor, float terrainSkyFloor, float terrainSkyFull, float accessorSkyFloor, float accessorSkyFull)
    {
        if (!FiniteNonnegative(adjacentBlock) || !FiniteNonnegative(dynamicPayload))
            throw new ArgumentOutOfRangeException(nameof(adjacentBlock));
        float peak = Math.Max(adjacentBlock.X, Math.Max(adjacentBlock.Y, adjacentBlock.Z));
        double correctedY = Luminance(adjacentBlock) * Math.Max(peak - neutralFloor, 0) / Math.Max(peak, float.Epsilon);
        double pointY = Luminance(dynamicPayload);
        if (correctedY <= 0 || pointY <= 0) throw new ArgumentException("The active light tables cannot represent the reference torch.");
        var result = new LightingCalibration((float)(44 / (4 * Math.PI) / correctedY), neutralFloor,
            (float)(44 / (4 * Math.PI) / pointY), terrainSkyFloor, terrainSkyFull, accessorSkyFloor, accessorSkyFull);
        result.Validate();
        return result;
    }
    /// <summary>Normalizes linear source chromaticity so RGB luminance equals the authored scalar intensity.</summary>
    internal static Vector3 SourceRgb(Vector3 linearColor, float intensity)
    {
        if (!float.IsFinite(intensity) || intensity < 0 || !FiniteNonnegative(linearColor))
            throw new ArgumentOutOfRangeException(nameof(intensity));
        if (intensity == 0) return Vector3.Zero;
        // Normalize color in double precision before converting the declared intensity to Float32 RGB.
        double y = .2126 * linearColor.X + .7152 * linearColor.Y + .0722 * linearColor.Z;
        if (y <= 0) throw new ArgumentException("A nonzero source requires nonzero chromaticity.", nameof(linearColor));
        Vector3 result = new((float)(linearColor.X * intensity / y), (float)(linearColor.Y * intensity / y),
            (float)(linearColor.Z * intensity / y));
        if (!FiniteNonnegative(result)) throw new ArgumentOutOfRangeException(nameof(intensity));
        return result;
    }
    /// <summary>Converts isotropic total luminous flux to directional luminous intensity.</summary>
    internal static float IsotropicCandela(float lumens)
    {
        if (!float.IsFinite(lumens) || lumens < 0) throw new ArgumentOutOfRangeException(nameof(lumens));
        return lumens / (4 * MathF.PI);
    }
    /// <summary>Checks a physical RGB quantity without silently replacing unsupported components.</summary>
    internal static bool FiniteNonnegative(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && value.X >= 0 && value.Y >= 0 && value.Z >= 0;
    #endregion

    #region Private
    /// <summary>Evaluates the declared scalar quantity without Float32 summation loss.</summary>
    private static double Luminance(Vector3 value) => .2126 * value.X + .7152 * value.Y + .0722 * value.Z;
    /// <summary>Rejects degenerate or nonfinite availability decoders before division.</summary>
    private static bool ValidEndpoints(float floor, float full) => float.IsFinite(floor) && float.IsFinite(full)
        && floor >= 0 && full <= 1 && full > floor;
    #endregion
}
