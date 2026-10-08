using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes final composition parameters in five std140 vector slots.</summary>
internal sealed class FinalDisplayInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates retained display parameter storage.</summary>
    internal FinalDisplayInputs() : base(80) { }
    /// <summary>Captures one coherent set of display, effect and exposure inputs.</summary>
    internal void Capture(Vector4 frame,FinalDisplayParameters display,Vector4 exposure)
    {
        WriteVector4(0,frame); WriteVector4(16,display.Grading); WriteVector4(32,display.Effects);
        WriteVector4(48,display.Vignette); WriteVector4(64,exposure);
    }
    #endregion
}
