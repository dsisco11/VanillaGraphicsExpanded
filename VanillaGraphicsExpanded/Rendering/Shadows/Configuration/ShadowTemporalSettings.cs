using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Bounds the deliberate approximation of reusing previously complete shadow depths.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowTemporalSettings
{
    /// <summary>Maximum elapsed seconds since the retained depth was rendered.</summary>
    [JsonProperty] public double MaxRenderedAgeSeconds { get; set; } = 0.25;
    /// <summary>Maximum seconds since first invalidation; zero disables all stale reuse.</summary>
    [JsonProperty] public double MaxStaleAgeSeconds { get; set; } = 0.1;
    /// <summary>Maximum Euclidean point-source displacement from the rendered position, in world metres.</summary>
    [JsonProperty] public double MaxPointTranslationMetres { get; set; } = 0.15;
    /// <summary>Maximum angular separation from the rendered celestial direction, in degrees.</summary>
    [JsonProperty] public double MaxDirectionalAngleDegrees { get; set; } = 0.25;

    #region Public API
    /// <summary>Materializes finite bounded defaults, retaining an explicit strict zero-reuse setting.</summary>
    internal void Sanitize()
    {
        MaxRenderedAgeSeconds = Valid(MaxRenderedAgeSeconds, 10, 0.25);
        MaxStaleAgeSeconds = Math.Min(Valid(MaxStaleAgeSeconds, 10, 0.1), MaxRenderedAgeSeconds);
        MaxPointTranslationMetres = Valid(MaxPointTranslationMetres, 4, 0.15);
        MaxDirectionalAngleDegrees = Valid(MaxDirectionalAngleDegrees, 10, 0.25);
    }

    /// <summary>Freezes effective policy independently of subsequent live configuration mutations.</summary>
    internal ShadowTemporalSettings Freeze()
    {
        var copy = (ShadowTemporalSettings)MemberwiseClone();
        copy.Sanitize();
        return copy;
    }
    #endregion

    #region Private
    /// <summary>Rejects nonfinite and out-of-policy values while preserving zero.</summary>
    private static double Valid(double value, double maximum, double fallback) =>
        double.IsFinite(value) && value >= 0 && value <= maximum ? value : fallback;
    #endregion
}
