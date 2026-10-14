using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Packs the complete generator control key in one std140 vector.</summary>
internal sealed class ColorGradingInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates the existing uniform allocator's retained input slice.</summary>
    internal ColorGradingInputs() : base(16) { }
    /// <summary>Captures the exact controls shared by cache admission and generation.</summary>
    internal void Capture(Vector4 controls) => WriteVector4(0,controls);
    #endregion
}
