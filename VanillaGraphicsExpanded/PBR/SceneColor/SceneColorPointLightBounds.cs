using System;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Certifies native cloud color averages and particle normalization for one immutable engine light list.</summary>
internal readonly record struct SceneColorPointLightBounds(bool Supported, float CloudMaximum, float CloudMapMaximum)
{
    #region Public API
    /// <summary>Checks the original engine payload rather than relabeling evaluated candela as native effect color.</summary>
    internal static SceneColorPointLightBounds FromEngine(int count, ReadOnlySpan<float> colors)
    {
        if ((uint)count > Rendering.VgeLightsUniformBuffer.Capacity || colors.Length < count * 3)
            throw new ArgumentException("Native effect bounds require the complete active light list.", nameof(colors));
        double cloudMaximum = 0, mapMaximum = 0, cloudStrengthSum = 0, mapStrengthSum = 0;
        for (int light = 0; light < count; light++)
        {
            float r = colors[light * 3], g = colors[light * 3 + 1], b = colors[light * 3 + 2];
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || r < 0 || g < 0 || b < 0)
                return default;
            double sum = (double)r + g + b;
            double squared = (double)r * r + (double)g * g + (double)b * b;
            // The particle shader computes normalize(plc) and plc.r / color.r.
            // Zero-red colored lights cannot certify that legacy division, whereas
            // a near-black light is skipped before normalization.
            if (sum >= .02 && (r == 0 || squared > float.MaxValue || r / Math.Sqrt(squared) < 1.1754943508222875e-38))
                return default;
            double cloudDivisor = r > 10 ? 100 : 1, mapDivisor = r > 10 ? 200 : 1;
            double peak = Math.Max(r, Math.Max(g, b));
            cloudMaximum = Math.Max(cloudMaximum, peak / cloudDivisor);
            mapMaximum = Math.Max(mapMaximum, peak / mapDivisor);
            cloudStrengthSum += sum / cloudDivisor / 3;
            mapStrengthSum += sum / mapDivisor / 3;
        }
        // Positive average normalization bounds the final RGB independently of
        // light count. Its numerator and brightness sums must still fit before
        // division; a finite normalized result alone is insufficient evidence.
        const double guard = 1 + 256.0 / (1 << 23);
        if (cloudStrengthSum * guard > float.MaxValue || mapStrengthSum * guard > float.MaxValue
            || cloudMaximum * cloudStrengthSum * guard > float.MaxValue
            || mapMaximum * mapStrengthSum * guard > float.MaxValue)
            return default;
        return new(true, LightingStorage.UpperBound(cloudMaximum * guard), LightingStorage.UpperBound(mapMaximum * guard));
    }
    #endregion
}
