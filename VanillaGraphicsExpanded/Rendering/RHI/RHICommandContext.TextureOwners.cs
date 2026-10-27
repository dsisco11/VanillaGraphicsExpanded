namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes native owner policy through the private allocation handoff without exposing native inputs publicly.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Executes an existing owner's legal-input initialization record.</summary>
    internal void AllocateTextureOwner(GpuTexture owner, in GpuTextureInitialization request, TextureFilterMode filter, string? label) =>
        backend.AllocateTextureOwner(owner, request, filter, label);
    #endregion
}
