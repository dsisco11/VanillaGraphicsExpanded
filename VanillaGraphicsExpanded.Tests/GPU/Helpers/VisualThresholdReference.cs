namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Test-only steady-state threshold reference from https://www.cs.ucf.edu/~sumant/publications/sig96.pdf;
/// this is not a production tone mapper or a complete retinal model.</summary>
internal static class VisualThresholdReference
{
    #region Public API
    /// <summary>Evaluates the cone threshold in cd/m² from Ferwerda et al. (1996), Equation 4, using decimal logarithms.</summary>
    internal static double ConeThreshold(double adaptation)
    {
        Validate(adaptation);
        double logarithm = Math.Log10(adaptation);
        double threshold = logarithm <= -2.6 ? -.72 : logarithm >= 1.9 ? logarithm - 1.255
            : Math.Pow(.249 * logarithm + .65, 2.7) - .72;
        return Math.Pow(10, threshold);
    }

    /// <summary>Evaluates Equation 5 for an independently supplied scotopic adaptation; no RGB-to-scotopic conversion is implied.</summary>
    internal static double RodThreshold(double adaptation)
    {
        Validate(adaptation);
        // Limit the study to dim and mesopic conditions: this approximation does not model high-level rod saturation.
        if (adaptation > 10) throw new ArgumentOutOfRangeException(nameof(adaptation));
        double logarithm = Math.Log10(adaptation);
        double threshold = logarithm <= -3.94 ? -2.86 : logarithm >= -1.44 ? logarithm - .395
            : Math.Pow(.405 * logarithm + 1.6, 2.18) - 2.86;
        return Math.Pow(10, threshold);
    }

    /// <summary>Equation 3 maps a world threshold to the display cone threshold without stacking a second runtime transform.</summary>
    internal static double MatchingGain(double worldAdaptation, double displayAdaptation, bool rodProxy = false)
        => ConeThreshold(displayAdaptation) / (rodProxy ? RodThreshold(worldAdaptation) : ConeThreshold(worldAdaptation));

    /// <summary>Converts an encoded sRGB output to assumed physical display luminance including the display black offset.</summary>
    internal static double DisplayLuminance(double r, double g, double b, double white = 100, double black = .1)
        => black + (white - black) * (.2126 * Decode(r) + .7152 * Decode(g) + .0722 * Decode(b));
    #endregion

    #region Private
    /// <summary>Rejects inputs outside the bounded evaluation range; numerical guards never create an observed adaptation luminance.</summary>
    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || value < 1e-6 || value > 1e6) throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Inverts sRGB once so threshold comparisons use emitted luminance rather than encoded display codes.</summary>
    private static double Decode(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    #endregion
}
