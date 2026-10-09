using System;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Captures native display grading independently of effect and texture ownership.</summary>
internal readonly record struct FinalDisplayParameters(Vector4 Grading,Vector4 Effects,Vector4 Vignette)
{
    // Native gamma is centered at 3; the owned display transform already performs sRGB encoding.
    private const float NativeNeutralGamma = 3f;

    #region Public API
    /// <summary>Captures relative gamma and native display controls for grading and screen effects at the final handoff.</summary>
    internal static FinalDisplayParameters Capture(ICoreClientAPI api)
    {
        var uniforms=api.Render.ShaderUniforms;
        // Preserve the native slider as a relative grading adjustment, not a second display transfer.
        return new(new(ClientSettings.GammaLevel / NativeNeutralGamma,ClientSettings.ExtraGammaLevel,
            ClientSettings.BrightnessLevel+Math.Max(0,uniforms.DropShadowIntensity*2-1.66f)/3,uniforms.ExtraContrastLevel),
            new(uniforms.SepiaLevel+uniforms.ExtraSepia,uniforms.WindWaveCounter,uniforms.GlitchStrength,0),
            new(uniforms.DamageVignetting,uniforms.DamageVignettingSide,uniforms.FrostVignetting,0));
    }
    #endregion
}
