using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Packs horizon integration and reconstruction inputs independently of the shared camera.</summary>
internal sealed class AmbientOcclusionInputs : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 48;
    internal const int FrameOffset = 0;
    internal const int SamplingOffset = 16;
    internal const int DistanceOffset = 32;
    /// <summary>Declares this layout's immutable extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Stages sampling policy and the selected spatial operation.</summary>
    internal void Capture(int divisor, int directions, int steps, float radius = 1.25f,
        float relaxation = .25f, float fadeStart = 64, float fadeEnd = 96, bool reconstruct = false)
    {
        WriteVector4(FrameOffset, new(0, 0, divisor, reconstruct ? 1 : 0));
        // Relaxation releases cosine-space occlusion as weaker radial observations arrive.
        WriteVector4(SamplingOffset, new(radius, relaxation, directions, steps));
        WriteVector4(DistanceOffset, new(fadeStart, fadeEnd, 0, 0));
    }
    #endregion
}
