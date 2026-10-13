using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Calculates a conservative visible-disk storage limit independently of physical solar illumination.</summary>
internal static class AtmosphereSolarDiskStorage
{
    #region Public API
    /// <summary>Returns the maximum RGB storage scale, or zero when source or glare inputs are unsupported.</summary>
    internal static float MaximumScale(Vector3 physicalNits, float bloomStrength, float shaftStrength)
    {
        if (!float.IsFinite(physicalNits.X) || !float.IsFinite(physicalNits.Y) || !float.IsFinite(physicalNits.Z)
            || physicalNits.X < 0 || physicalNits.Y < 0 || physicalNits.Z < 0
            || !float.IsFinite(bloomStrength) || bloomStrength < 0 || bloomStrength > 2
            || !float.IsFinite(shaftStrength) || shaftStrength < 0 || shaftStrength > 2)
            return 0;

        // Preserve the existing glare headroom rule. Round its destination down before
        // calculating the ratio, so a nonrepresentable cap cannot round above the bound.
        double cap = Math.Min(48000, Math.Max(0,
            64000 / Math.Max(1d, Math.Max(bloomStrength, shaftStrength)) - 16000));
        float storedCap = (float)cap;
        if (storedCap > cap) storedCap = MathF.BitDecrement(storedCap);
        float peak = Math.Max(physicalNits.X, Math.Max(physicalNits.Y, physicalNits.Z));
        if (peak == 0) return float.MaxValue;

        // Double division avoids the fragment driver's subnormal reciprocal path for
        // extreme finite radiance. One downward float step also reserves multiplication
        // rounding headroom; the fragment shader only selects a scale and multiplies.
        double limit = (double)storedCap / peak;
        return MathF.BitDecrement((float)Math.Min(float.MaxValue, limit));
    }
    #endregion
}
