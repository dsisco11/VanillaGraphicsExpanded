namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Accepts private legal-input records from existing native texture owner policy.</summary>
internal partial interface IRHIResourceExecution
{
    /// <summary>Realizes or replaces owner storage through the complete creation/storage transaction.</summary>
    void AllocateTextureOwner(GpuTexture owner, in GpuTextureInitialization request, TextureFilterMode filter, string? label, bool replaceExisting);
}
