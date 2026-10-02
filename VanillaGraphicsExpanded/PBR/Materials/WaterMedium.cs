using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Resolved water coefficients independent of texture tint, with explicit additional water-type absorption.</summary>
internal readonly record struct WaterMedium(Vector3 AdditionalAbsorptionPerMetre, Vector3 ScatteringPerMetre,
    float Density = 1, float Anisotropy = 0)
{
    /// <summary>Pope and Fry (1997), Table 3, at 650, 550 and 450 nm respectively, in inverse metres.</summary>
    internal static readonly Vector3 ClearWaterAbsorptionPerMetre = new(.340f, .0565f, .00922f);
    /// <summary>Clear-water absorption with no assumed turbidity or particulate scattering.</summary>
    internal static WaterMedium Clear => new(Vector3.Zero, Vector3.Zero);
    internal Vector3 AbsorptionPerMetre => (ClearWaterAbsorptionPerMetre + AdditionalAbsorptionPerMetre) * Density;
    internal Vector3 EffectiveScatteringPerMetre => ScatteringPerMetre * Density;

    #region Public API
    /// <summary>Resolves member-wise inheritance; invalid values inherit with a diagnostic instead of introducing non-finite transport.</summary>
    internal static WaterMedium Resolve(WaterMediumJson? json, WaterMedium inherited, Action<string>? diagnostic = null)
    {
        if (json is null) return inherited;
        return new(ReadCoefficients(json.AdditionalAbsorptionPerMetre, inherited.AdditionalAbsorptionPerMetre,
                "additionalAbsorptionPerMetre", diagnostic),
            ReadCoefficients(json.ScatteringPerMetre, inherited.ScatteringPerMetre, "scatteringPerMetre", diagnostic),
            ReadScalar(json.Density, inherited.Density, 0, 100, "density", diagnostic),
            ReadScalar(json.Anisotropy, inherited.Anisotropy, -.95f, .95f, "anisotropy", diagnostic));
    }
    #endregion

    #region Private
    /// <summary>Accepts exactly three finite nonnegative RGB coefficients with a bounded inverse-metre range.</summary>
    private static Vector3 ReadCoefficients(float[]? values, Vector3 inherited, string name, Action<string>? diagnostic)
    {
        if (values is null) return inherited;
        if (values.Length == 3 && Array.TrueForAll(values, value => float.IsFinite(value) && value >= 0 && value <= 100))
            return new(values[0], values[1], values[2]);
        diagnostic?.Invoke($"Invalid waterMedium.{name}; expected three finite RGB values in [0, 100] m^-1. Inheriting.");
        return inherited;
    }

    /// <summary>Preserves explicit zero while rejecting non-finite or unsupported scalar parameters.</summary>
    private static float ReadScalar(float? value, float inherited, float minimum, float maximum, string name, Action<string>? diagnostic)
    {
        if (value is null) return inherited;
        if (float.IsFinite(value.Value) && value >= minimum && value <= maximum) return value.Value;
        diagnostic?.Invoke($"Invalid waterMedium.{name}; expected a finite value in [{minimum}, {maximum}]. Inheriting.");
        return inherited;
    }
    #endregion
}
