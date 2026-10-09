using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes camera transforms and bounded horizon/filter parameters in one retained block.</summary>
internal sealed class AmbientOcclusionInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates two matrices and three std140 vectors.</summary>
    internal AmbientOcclusionInputs() : base(176) { }
    /// <summary>Captures camera space, sampling policy and the selected spatial operation.</summary>
    internal void Capture(float[] inverseProjection, float[] view, Vector4 frame, Vector4 sampling, Vector4 distance)
    {
        WriteMatrix4(0, inverseProjection); WriteMatrix4(64, view);
        WriteVector4(128, frame); WriteVector4(144, sampling); WriteVector4(160, distance);
    }
    #endregion
}
