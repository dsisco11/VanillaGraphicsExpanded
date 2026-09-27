using System;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Physical displacement authoring limits, independent of the BRDF and height bake scale.</summary>
internal static class MaterialDisplacement
{
    public const float MaximumAmplitudeMetres = 0.05f;

    #region Authoring validation
    /// <summary>Invalid amplitudes opt out rather than clamping into unintended displacement.</summary>
    internal static float ResolveAmplitude(float amplitude, Action<string>? diagnostic = null)
    {
        if (float.IsFinite(amplitude) && amplitude >= 0 && amplitude <= MaximumAmplitudeMetres) return amplitude;
        diagnostic?.Invoke("Invalid displacement.amplitudeMetres; expected a finite value in [0, 0.05]. Displacement disabled.");
        return 0;
    }
    #endregion
}
