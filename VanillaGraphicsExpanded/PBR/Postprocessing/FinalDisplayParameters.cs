using System;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Captures native display grading independently of effect and texture ownership.</summary>
internal readonly record struct FinalDisplayParameters(Vector4 Grading,Vector4 Effects,Vector4 Vignette)
{
    #region Public API
    /// <summary>Captures native control values for independently authored grading and screen effects at the final handoff.</summary>
    internal static FinalDisplayParameters Capture(ICoreClientAPI api)
    {
        var uniforms=api.Render.ShaderUniforms;
        return new(new(ClientSettings.GammaLevel,ClientSettings.ExtraGammaLevel,
            ClientSettings.BrightnessLevel+Math.Max(0,uniforms.DropShadowIntensity*2-1.66f)/3,uniforms.ExtraContrastLevel),
            new(uniforms.SepiaLevel+uniforms.ExtraSepia,uniforms.WindWaveCounter,uniforms.GlitchStrength,0),
            new(uniforms.DamageVignetting,uniforms.DamageVignettingSide,uniforms.FrostVignetting,0));
    }
    #endregion
}
