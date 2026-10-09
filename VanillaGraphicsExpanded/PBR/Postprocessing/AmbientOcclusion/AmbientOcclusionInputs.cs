using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes bounded horizon/filter parameters independently of the shared camera.</summary>
internal sealed class AmbientOcclusionInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates three effect-specific std140 vectors.</summary>
    internal AmbientOcclusionInputs() : base(48) { }
    /// <summary>Captures sampling policy and the selected spatial operation.</summary>
    internal void Capture(Vector4 frame, Vector4 sampling, Vector4 distance)
    {
        WriteVector4(0, frame); WriteVector4(16, sampling); WriteVector4(32, distance);
    }
    #endregion
}
