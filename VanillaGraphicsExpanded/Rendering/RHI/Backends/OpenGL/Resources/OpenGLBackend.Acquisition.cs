namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Defines explicit import, recovery and selected-image publication at the private resource boundary.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void AcquireBorrowedTexture(BorrowedTexture owner)
    {
        RequireContext();
        owner.RequireOriginalContext(); RHIResourceManager.AcquireWrapperMetadata(owner, GpuTextureMetadataReader.Instance);
    }
    /// <inheritdoc />
    public void AcquireAttachmentImage(GpuFramebufferAttachment image)
    {
        RequireContext();
        if (image.ViewCount != 0 && (!Capabilities.Multiview
            || image.AttachmentMode == GpuFramebufferAttachmentMode.Multiview && image.ViewCount > Capabilities.MaxMultiviewViews
            || (long)image.BaseView + image.ViewCount > Capabilities.MaxArrayTextureLayers))
            throw new System.ArgumentOutOfRangeException(nameof(image), "The image selection exceeds multiview capabilities.");
        RHIResourceManager.AcquireImageMetadata(image);
    }
    /// <inheritdoc />
    public GpuTextureMetadata AcquireExternalAttachmentTexture(int id)
    {
        RequireContext();
        return RHIResourceManager.AcquireExternalImageTexture(id);
    }
    /// <inheritdoc />
    public GpuProgramMetadata AcquireProgramInterface(int id)
    {
        RequireContext();
        return RHIResourceManager.AcquireProgramMetadata(id);
    }
    /// <inheritdoc />
    public void AdoptProgramOwner(GpuProgramObject owner)
    {
        RequireContext();
        owner.RequireOriginalContext(); RHIResourceManager.AcquireWrapperMetadata(owner, GpuProgramMetadataReader.Instance);
    }
    /// <inheritdoc />
    public void AdoptShaderOwner(GpuShaderModule owner)
    {
        RequireContext();
        owner.RequireOriginalContext(); RHIResourceManager.AcquireWrapperMetadata(owner, GpuShaderMetadataReader.Instance);
    }
    /// <inheritdoc />
    public GpuFramebufferAttachment DiscoverFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot)
    {
        RequireContext();
        owner.RequireOriginalContext(); return RHIResourceManager.DiscoverFramebufferAttachment(owner, slot);
    }
    /// <inheritdoc />
    public void PublishImportedFramebuffer(GpuFramebuffer owner, bool publish, int? replacementId = null, int width = 0, int height = 0)
    {
        RequireContext();
        owner.RequireOriginalContext();
        RHIResourceManager.PublishImportedFramebuffer(owner, publish, replacementId, width, height);
    }
    /// <inheritdoc />
    public void ResolveFramebufferAttachments(GpuFramebuffer owner, bool recover)
    {
        RequireContext();
        owner.RequireOriginalContext();
        var images = RHIResourceManager.ResolveFramebufferAttachments(owner,
            new System.Collections.Generic.Dictionary<FramebufferSlot, GpuFramebufferAttachment>(owner.AttachmentImages), recover);
        owner.InstallAttachmentReferences(images, owner.PublishedMetadata.Attachments!);
    }
    /// <inheritdoc />
    public void ObserveAttachmentOwner(GpuResource owner, GpuFramebufferAttachment image)
    {
        RequireContext();
        RHIResourceManager.ObserveAttachmentOwner(owner, image);
    }
    #endregion
}
