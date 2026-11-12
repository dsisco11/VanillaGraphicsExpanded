using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Retains one material owner's coherent displacement publication without acquiring texture ownership.</summary>
internal readonly record struct MaterialDisplacementSnapshot(long Revision, GpuTextureRef Indices,
    GpuTextureRef Records, GpuTextureRef Height)
{
    /// <summary>Distinguishes an authored displacement page from the native zero-displacement case.</summary>
    internal bool HasDisplacement => !Indices.IsEmpty;
}
