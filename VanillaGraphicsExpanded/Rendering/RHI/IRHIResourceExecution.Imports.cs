namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Defines explicit import, recovery and selected-image publication at the private resource boundary.</summary>
internal partial interface IRHIResourceExecution
{
    #region Acquisition and publication
    /// <summary>Admits AcquireBorrowedTexture through the original resource context and manager authority.</summary>
    void AcquireBorrowedTexture(BorrowedTexture owner);
    /// <summary>Admits AcquireAttachmentImage through the original resource context and manager authority.</summary>
    void AcquireAttachmentImage(GpuFramebufferAttachment image);
    /// <summary>Admits AcquireExternalAttachmentTexture through the original resource context and manager authority.</summary>
    GpuTextureMetadata AcquireExternalAttachmentTexture(int id);
    /// <summary>Admits AcquireProgramInterface through the original resource context and manager authority.</summary>
    GpuProgramMetadata AcquireProgramInterface(int id);
    /// <summary>Admits AdoptProgramOwner through the original resource context and manager authority.</summary>
    void AdoptProgramOwner(GpuProgramObject owner);
    /// <summary>Admits AdoptShaderOwner through the original resource context and manager authority.</summary>
    void AdoptShaderOwner(GpuShaderModule owner);
    /// <summary>Admits DiscoverFramebufferAttachment through the original resource context and manager authority.</summary>
    GpuFramebufferAttachment DiscoverFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot, bool recover);
    /// <summary>Admits PublishImportedFramebuffer through the original resource context and manager authority.</summary>
    void PublishImportedFramebuffer(GpuFramebuffer owner, bool publish, int? replacementId = null, int width = 0, int height = 0);
    /// <summary>Admits ResolveFramebufferAttachments through the original resource context and manager authority.</summary>
    void ResolveFramebufferAttachments(GpuFramebuffer owner, bool recover);
    /// <summary>Admits ObserveAttachmentOwner through the original resource context and manager authority.</summary>
    void ObserveAttachmentOwner(GpuResource owner, GpuFramebufferAttachment image);
    /// <summary>Applies external authored assignments using canonical facts and returns their reconciled snapshot.</summary>
    GpuProgramMetadata ApplyProgramBindings(int program, System.Collections.Generic.IReadOnlyList<ProgramBindingAssignment> assignments, System.Action<string>? warn);
    #endregion
}
