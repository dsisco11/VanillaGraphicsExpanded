namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes native owner policy through the private allocation handoff without exposing native inputs publicly.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Executes owner storage policy, completing object replacement before notifying dependents.</summary>
    internal void AllocateTextureOwner(GpuTexture owner, in GpuTextureInitialization request, TextureFilterMode filter, string? label, bool replaceExisting = false) =>
        backend.AllocateTextureOwner(owner, request, filter, label, replaceExisting);
    #endregion
}
