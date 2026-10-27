using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines image-family execution independently of native overloads and selectors.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Initializes an unpublished specialized owner before its existing manager handoff.</summary>
    void InitializeTextureOwner(GpuTexture owner, in RHITextureDescription description);
    /// <summary>Creates a completed existing owner from frozen image intent and stable initial content.</summary>
    RHICreationResult<T> CreateTexture<T>(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData) where T : GpuTexture;
    /// <summary>Creates a completed renderbuffer owner without changing selectors.</summary>
    RHICreationResult<GpuRenderbuffer> CreateRenderbuffer(in RHIRenderbufferDescription description);
    /// <summary>Creates an owned texture object sharing the explicit original buffer selection.</summary>
    RHICreationResult<GpuBufferTexture> CreateBufferTexture(in RHIBufferTextureDescription description);
    /// <summary>Creates a view retaining the source's original storage association.</summary>
    RHICreationResult<GpuTextureView> CreateTextureView(in RHITextureViewDescription description);
    /// <summary>Creates an owned sampler with the supplied legal sampling fields.</summary>
    RHICreationResult<GpuSampler> CreateSampler(in RHISamplerDescription description);
    /// <summary>Uploads client pixels to a view-relative region without publishing content as metadata.</summary>
    void UploadTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> bytes, int rowPitch, int slicePitch);
    /// <summary>Transfers a checked buffer region into a view-relative image region.</summary>
    void TransferBufferToTexture(GpuTextureRef destination, in RHITextureTransfer transfer);
    /// <summary>Transfers a selected image region into checked buffer storage without mapping.</summary>
    void TransferTextureToBuffer(GpuTextureRef source, in RHITextureTransfer transfer);
    /// <summary>Copies compatible image regions without creating an independent storage snapshot.</summary>
    void CopyTexture(GpuTextureRef source, in RHITextureRegion sourceRegion, GpuTextureRef destination, in RHITextureRegion destinationRegion);
    /// <summary>Clears an image region using a single explicitly encoded texel value.</summary>
    void ClearTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> value);
    /// <summary>Generates mip contents and immediately publishes any mutable allocation changes.</summary>
    void GenerateMipmaps(GpuTextureRef texture);
    /// <summary>Replaces storage under the existing owner's mutation authority.</summary>
    void AllocateTextureStorage(GpuTexture owner, in RHITextureDescription description);
    /// <summary>Replaces storage under the renderbuffer owner's authority.</summary>
    void AllocateRenderbufferStorage(GpuRenderbuffer owner, in RHIRenderbufferDescription description);
    /// <summary>Applies owner-authorized texture sampling policy.</summary>
    void ConfigureTexture(GpuTexture owner, in RHISamplerDescription description);
    /// <summary>Applies owner-authorized sampler configuration.</summary>
    void ConfigureSampler(GpuSampler owner, in RHISamplerDescription description);
    /// <summary>Changes the owning texture's represented sampling range.</summary>
    void SetMipRange(GpuTexture owner, int baseLevel, int maxLevel);
    /// <summary>Clears a complete allocated mip to zero using its published representation.</summary>
    void ClearTextureMip(GpuTextureRef texture, int mipLevel);
    /// <summary>Changes extent while retaining the original renderbuffer format and sample policy.</summary>
    void ResizeRenderbuffer(GpuRenderbuffer owner, int width, int height);
    #endregion
}
