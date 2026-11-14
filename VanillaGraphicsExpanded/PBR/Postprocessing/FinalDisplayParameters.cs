using System;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Captures native display grading independently of effect and texture ownership.</summary>
/// <param name="Grading">Color transform controls shared by display and lookup admission.</param>
/// <param name="Sepia">Combined sepia strength.</param>
/// <param name="WindPhase">Animation phase of authored display effects.</param>
/// <param name="GlitchStrength">Authored glitch strength.</param>
/// <param name="DamageVignette">Damage vignette intensity.</param>
/// <param name="SideDamageVignette">Directional damage vignette intensity.</param>
/// <param name="FrostVignette">Frost vignette intensity.</param>
internal readonly record struct FinalDisplayParameters(ColorGradingParameters Grading, float Sepia,
    float WindPhase, float GlitchStrength, float DamageVignette, float SideDamageVignette, float FrostVignette)
{
    // Native gamma is centered at 3; linear grading uses that relative control before display encoding.
    private const float NativeNeutralGamma = 3f;

    #region Public API
    /// <summary>Captures relative gamma and native display controls for grading and screen effects at the final handoff.</summary>
    internal static FinalDisplayParameters Capture(ICoreClientAPI api)
    {
        var uniforms=api.Render.ShaderUniforms;
        // Preserve native controls while keeping gamma, gain and contrast in exposed linear light.
        return new(new(ClientSettings.GammaLevel / NativeNeutralGamma,ClientSettings.ExtraGammaLevel,
            ClientSettings.BrightnessLevel+Math.Max(0,uniforms.DropShadowIntensity*2-1.66f)/3,uniforms.ExtraContrastLevel),
            uniforms.SepiaLevel+uniforms.ExtraSepia,uniforms.WindWaveCounter,uniforms.GlitchStrength,
            uniforms.DamageVignetting,uniforms.DamageVignettingSide,uniforms.FrostVignetting);
    }
    #endregion
}
