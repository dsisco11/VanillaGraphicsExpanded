using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Bounds every captured boundary and the initial camera source from matching published illumination.</summary>
internal static class WaterSourceBounds
{
    #region Public API
    /// <summary>Combines admitted solar illumination and isotropic diffuse incident illumination with outward arithmetic headroom.</summary>
    internal static Vector3 FromIncident(Vector3 solar, Vector3 diffuseIncident)
    {
        if (!LightingCalibration.FiniteNonnegative(solar) || !LightingCalibration.FiniteNonnegative(diffuseIncident))
            throw new ArgumentOutOfRangeException(nameof(solar));
        // The 64 phase envelope covers admitted anisotropy through ±.95. Include source-expression
        // rounding headroom before publishing the bound; no receiver-local sample substitutes for it.
        const double allowance = 1 + 16.0 / (1 << 23);
        return new(LightingStorage.UpperBound((64.0 * solar.X + diffuseIncident.X / (4 * Math.PI)) * allowance),
            LightingStorage.UpperBound((64.0 * solar.Y + diffuseIncident.Y / (4 * Math.PI)) * allowance),
            LightingStorage.UpperBound((64.0 * solar.Z + diffuseIncident.Z / (4 * Math.PI)) * allowance));
    }
    /// <summary>Admits the active normalized engine decoder, preserving its raw field until shader conversion migrates.</summary>
    internal static float EngineFieldMaximum(ReadOnlySpan<float> levels)
    {
        if (levels.Length != 32) throw new ArgumentException("Expected the active 32-level block decoder.", nameof(levels));
        float maximum = 0;
        foreach (float level in levels)
        {
            if (!float.IsFinite(level) || level < 0 || level > 1)
                throw new ArgumentOutOfRangeException(nameof(levels), "Unsupported engine block-light decoder.");
            maximum = Math.Max(maximum, level);
        }
        // Vertex fields/accessor RGB are normalized byte colors; upward byte rounding covers interpolation.
        return MathF.Ceiling(maximum * 255) / 255;
    }
    #endregion
}
