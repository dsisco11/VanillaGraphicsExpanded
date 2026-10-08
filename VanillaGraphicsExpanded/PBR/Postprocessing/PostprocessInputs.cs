using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Stages bounded filtering, exposure and solar parameters for one postprocess draw.</summary>
internal sealed class PostprocessInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates four std140 vector slots.</summary>
    internal PostprocessInputs() : base(64) { }
    /// <summary>Captures the complete immutable draw parameters before submission.</summary>
    internal void Capture(Vector4 pass, Vector4 effect, Vector4 sun = default, Vector4 solar = default)
    {
        WriteVector4(0, pass); WriteVector4(16, effect); WriteVector4(32, sun); WriteVector4(48, solar);
    }
    #endregion
}
