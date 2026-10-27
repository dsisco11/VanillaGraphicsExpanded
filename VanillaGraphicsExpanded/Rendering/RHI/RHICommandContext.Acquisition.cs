namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Defines explicit import, recovery and selected-image publication at the private resource boundary.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Routes explicit AcquireBorrowedTexture without exposing native execution to resource wrappers.</summary>
    internal void AcquireBorrowedTexture(BorrowedTexture owner) => backend.AcquireBorrowedTexture(owner);
    /// <summary>Routes explicit AcquireAttachmentImage without exposing native execution to resource wrappers.</summary>
    internal void AcquireAttachmentImage(GpuFramebufferAttachment image, GpuTextureMetadata? texture = null, GpuRenderbufferMetadata? renderbuffer = null) => backend.AcquireAttachmentImage(image, texture, renderbuffer);
    /// <summary>Routes explicit AcquireExternalAttachmentTexture without exposing native execution to resource wrappers.</summary>
    internal GpuTextureMetadata AcquireExternalAttachmentTexture(int id) => backend.AcquireExternalAttachmentTexture(id);
    /// <summary>Routes explicit AcquireProgramInterface without exposing native execution to resource wrappers.</summary>
    internal GpuProgramMetadata AcquireProgramInterface(int id) => backend.AcquireProgramInterface(id);
    /// <summary>Routes explicit AdoptProgramOwner without exposing native execution to resource wrappers.</summary>
    internal void AdoptProgramOwner(GpuProgramObject owner) => backend.AdoptProgramOwner(owner);
    /// <summary>Routes explicit AdoptShaderOwner without exposing native execution to resource wrappers.</summary>
    internal void AdoptShaderOwner(GpuShaderModule owner) => backend.AdoptShaderOwner(owner);
    /// <summary>Routes explicit DiscoverFramebufferAttachment without exposing native execution to resource wrappers.</summary>
    internal GpuFramebufferAttachment DiscoverFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot, bool recover) => backend.DiscoverFramebufferAttachment(owner, slot, recover);
    /// <summary>Routes explicit PublishImportedFramebuffer without exposing native execution to resource wrappers.</summary>
    internal void PublishImportedFramebuffer(GpuFramebuffer owner, bool publish, int? replacementId = null, int width = 0, int height = 0) => backend.PublishImportedFramebuffer(owner, publish, replacementId, width, height);
    /// <summary>Routes explicit ResolveFramebufferAttachments without exposing native execution to resource wrappers.</summary>
    internal void ResolveFramebufferAttachments(GpuFramebuffer owner, bool recover) => backend.ResolveFramebufferAttachments(owner, recover);
    /// <summary>Routes explicit ObserveAttachmentOwner without exposing native execution to resource wrappers.</summary>
    internal void ObserveAttachmentOwner(GpuResource owner, GpuFramebufferAttachment image) => backend.ObserveAttachmentOwner(owner, image);
    #endregion
}
