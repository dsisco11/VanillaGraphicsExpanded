using System;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Bounds the shared authored terrain deformation and authored height displacement without inspecting mesh vertices.</summary>
internal static class NativeShadowTerrainEnvelope
{
    #region Public API
    /// <summary>Returns an axis-independent world displacement bound for all supported material flag modes.</summary>
    internal static double Capture(DefaultShaderUniforms native, bool waving, double displacement)
    {
        if (!double.IsFinite(displacement) || displacement < 0) throw new ArgumentOutOfRangeException(nameof(displacement));
        if (!waving) return displacement;
        // Shared VGE waves have absolute value at most one. The largest foliage
        // coefficient is tall bend: 0.2 * (1 + 0.25) on the horizontal axis.
        double foliage = 0.25 * Math.Abs(native.WindWaveIntensity) * (1 + Math.Abs(native.WindSpeed));
        double liquid = Math.Abs(native.WaterWaveIntensity) / 5 + Math.Abs(native.WindWaveIntensity) * 0.05;
        double global = Math.Max(0, native.GlobalWorldWarp) * 0.05 + Math.Max(0, native.GlitchWaviness - 0.1) * 0.1;
        // Water evaluates perception locally and all geometry evaluates it globally.
        double perception = native.PerceptionEffectId == 2 ? Math.Max(0, native.PerceptionEffectIntensity) * 0.2 : 0;
        double bound = (Math.Max(foliage, liquid) + global + perception) * 1.02 + 0.01 + displacement;
        if (!double.IsFinite(bound) || bound > float.MaxValue)
            throw new InvalidOperationException("Shared deformation has no finite selectable envelope.");
        return bound;
    }
    #endregion
}
