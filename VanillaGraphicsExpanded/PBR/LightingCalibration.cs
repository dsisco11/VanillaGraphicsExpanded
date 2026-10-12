using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Defines source quantities and the selected compatibility references in linear Rec.709 RGB.</summary>
internal readonly record struct LightingCalibration(float BlockReferenceLux, float BlockNeutralFloor)
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
            || !float.IsFinite(BlockNeutralFloor) || BlockNeutralFloor < 0 || BlockNeutralFloor >= 1)
            throw new ArgumentOutOfRangeException(nameof(LightingCalibration));
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
}
