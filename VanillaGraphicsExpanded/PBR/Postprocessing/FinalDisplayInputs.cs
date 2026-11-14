using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes grading and optional local-display policy in six std140 vector slots.</summary>
internal sealed class FinalDisplayInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 96;
    internal const int GradingOffset = 0;
    internal const int EffectsOffset = 16;
    internal const int VignetteOffset = 32;
    internal const int ExposureOffset = 48;
    internal const int LocalLimitsOffset = 64;
    internal const int LocalPolicyOffset = 80;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates retained display parameter storage.</summary>
    internal FinalDisplayInputs() : base() { }
    /// <summary>Captures explicit candidate controls independently of global camera gain.</summary>
    internal void CaptureLocal(LocalExposure.LocalExposureParameters settings)
    {
        WriteVector4(LocalLimitsOffset,new(settings.ShadowStrength,settings.HighlightStrength,settings.ShadowLimit,settings.HighlightLimit));
        WriteVector4(LocalPolicyOffset,new(settings.RangeWidth,settings.Enabled?1:0,settings.Visualize?1:0,0));
    }
    /// <summary>Packs named display controls and camera policy into the complete presentation input slice.</summary>
    internal void Capture(bool antialias, FinalDisplayParameters display, float manualLogGain,
        bool automaticExposure, float logMiddleGray)
    {
        var grading = display.Grading;
        WriteVector4(GradingOffset, new(grading.Gamma, grading.ExtraGamma, grading.Brightness, grading.Contrast));
        WriteVector4(EffectsOffset, new(display.Sepia, display.WindPhase, display.GlitchStrength, 0));
        WriteVector4(VignetteOffset, new(display.DamageVignette, display.SideDamageVignette, display.FrostVignette, 0));
        WriteVector4(ExposureOffset, new(manualLogGain, automaticExposure ? 1 : 0, antialias ? 1 : 0, logMiddleGray));
    }
    #endregion
}
