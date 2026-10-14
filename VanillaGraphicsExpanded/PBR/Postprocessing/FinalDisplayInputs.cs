using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes grading and optional local-display policy in six std140 vector slots.</summary>
internal sealed class FinalDisplayInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates retained display parameter storage.</summary>
    internal FinalDisplayInputs() : base(96) { }
    /// <summary>Captures explicit candidate controls independently of global camera gain.</summary>
    internal void CaptureLocal(LocalExposure.LocalExposureParameters settings)
    {
        WriteVector4(64,new(settings.ShadowStrength,settings.HighlightStrength,settings.ShadowLimit,settings.HighlightLimit));
        WriteVector4(80,new(settings.RangeWidth,settings.Enabled?1:0,settings.Visualize?1:0,0));
    }
    /// <summary>Captures one coherent set of display, effect and exposure inputs.</summary>
    internal void Capture(bool antialias,FinalDisplayParameters display,Vector4 exposure)
    {
        WriteVector4(0,display.Grading); WriteVector4(16,display.Effects);
        WriteVector4(32,display.Vignette); WriteVector4(48,new(exposure.X,exposure.Y,antialias?1:0,exposure.W));
    }
    #endregion
}
