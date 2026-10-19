using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Pairs a physical diffuse bound with an exposure-independent half-float storage scale.</summary>
internal readonly record struct LightingVolumeEncoding(float Cap, float Scale)
{
    #region Public API
    /// <summary>Rejects hand-constructed or default records before they can describe a published field.</summary>
    internal bool IsValid => float.IsFinite(Cap) && Cap >= 0 && float.IsFinite(Scale) && Scale > 0 &&
        float.IsFinite(1 / Scale) && 1 / Scale > 0 && IsPowerOfTwo(Scale) &&
        (double)Cap / Scale <= 32752;

    /// <summary>Admits finite float32 arithmetic and reserves half-float rounding headroom.</summary>
    internal static LightingVolumeEncoding Create(double cap)
    {
        double scale = Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(1, cap) / 32752)));
        if (!double.IsFinite(cap) || cap < 0 || !float.IsFinite((float)cap) ||
            !float.IsFinite((float)scale) || scale <= 0 || !float.IsFinite((float)(1 / scale)) ||
            (float)scale == 0 || (float)(1 / scale) == 0)
            throw new ArgumentOutOfRangeException(nameof(cap));
        var result = new LightingVolumeEncoding((float)cap, (float)scale);
        if (!result.IsValid) throw new ArgumentOutOfRangeException(nameof(cap));
        return result;
    }

    /// <summary>Keeps a lifetime's scale monotonic while permitting brighter physical bounds.</summary>
    internal LightingVolumeEncoding Grow(double cap)
    {
        var next = Create(Math.Max(cap, Cap));
        return next with { Scale = Math.Max(Scale, next.Scale) };
    }
    #endregion
    #region Private
    /// <summary>Checks exact significand bits, including subnormal powers, without rounded logarithms.</summary>
    private static bool IsPowerOfTwo(float value)
    {
        uint bits = (uint)BitConverter.SingleToInt32Bits(value), fraction = bits & 0x7fffffu;
        return (bits & 0x7f800000u) == 0 ? BitOperations.IsPow2(fraction) : fraction == 0;
    }
    #endregion
}
