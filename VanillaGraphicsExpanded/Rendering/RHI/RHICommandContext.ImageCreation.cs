using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Provides semantic image factories without exposing provisional recipients.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    #region Creation
    /// <summary>Creates an image owner after complete metadata and optional initial content handoff.</summary>
    public RHICreationResult<GpuTexture> CreateTexture(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<GpuTexture>(description, initialData);
    /// <summary>Creates a resizable two-dimensional image owner.</summary>
    public RHICreationResult<DynamicTexture2D> CreateDynamicTexture2D(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<DynamicTexture2D>(description, initialData);
    /// <summary>Creates a resizable volume or layered image owner.</summary>
    public RHICreationResult<DynamicTexture3D> CreateDynamicTexture3D(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<DynamicTexture3D>(description, initialData);
    /// <summary>Creates a cube owner retaining explicit size, format and mip allocation policy.</summary>
    public RHICreationResult<DynamicTextureCube> CreateTextureCube(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<DynamicTextureCube>(description, initialData);
    /// <summary>Creates a fixed two-dimensional image owner.</summary>
    public RHICreationResult<Texture2D> CreateTexture2D(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<Texture2D>(description, initialData);
    /// <summary>Creates a fixed layered image owner.</summary>
    public RHICreationResult<Texture3D> CreateTexture3D(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData = default) => backend.CreateTexture<Texture3D>(description, initialData);
    /// <summary>Creates renderbuffer storage without exposing a native target.</summary>
    public RHICreationResult<GpuRenderbuffer> CreateRenderbuffer(in RHIRenderbufferDescription description) => backend.CreateRenderbuffer(description);
    /// <summary>Creates a buffer texture from an original backing reference and explicit range mode.</summary>
    public RHICreationResult<GpuBufferTexture> CreateBufferTexture(in RHIBufferTextureDescription description) => backend.CreateBufferTexture(description);
    /// <summary>Creates a view with view-relative mip and layer selections.</summary>
    public RHICreationResult<GpuTextureView> CreateTextureView(in RHITextureViewDescription description) => backend.CreateTextureView(description);
    /// <summary>Creates an owned sampler with an explicit immutable initialization request.</summary>
    public RHICreationResult<GpuSampler> CreateSampler(in RHISamplerDescription description) => backend.CreateSampler(description);
    #endregion
    #region Content and transfers
    /// <summary>Uploads stable client pixels immediately with explicit byte pitches.</summary>
    public void UploadTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> bytes, int rowPitch = 0, int slicePitch = 0) => backend.UploadTexture(destination, region, encoding, bytes, rowPitch, slicePitch);
    /// <summary>Transfers a GPU buffer range into image storage, retaining producer completion policy.</summary>
    public void TransferBufferToTexture(GpuTextureRef destination, in RHITextureTransfer transfer) => backend.TransferBufferToTexture(destination, transfer);
    /// <summary>Transfers an image into existing GPU buffer storage without a CPU readback.</summary>
    public void TransferTextureToBuffer(GpuTextureRef source, in RHITextureTransfer transfer) => backend.TransferTextureToBuffer(source, transfer);
    /// <summary>Copies compatible view-relative regions in explicit caller order.</summary>
    public void CopyTexture(GpuTextureRef source, in RHITextureRegion sourceRegion, GpuTextureRef destination, in RHITextureRegion destinationRegion) => backend.CopyTexture(source, sourceRegion, destination, destinationRegion);
    /// <summary>Clears an image region without allocating a full-image CPU payload.</summary>
    public void ClearTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> value) => backend.ClearTexture(destination, region, encoding, value);
    /// <summary>Generates mip contents immediately because mutable storage facts may change.</summary>
    public void GenerateMipmaps(GpuTextureRef texture) => backend.GenerateMipmaps(texture);
    #endregion
    #endregion

    #region Internal API
    /// <summary>Executes owner-authorized storage policy without exposing it on consumer references.</summary>
    internal void AllocateTextureStorage(GpuTexture owner, in RHITextureDescription description) => backend.AllocateTextureStorage(owner, description);
    /// <summary>Executes the renderbuffer owner's explicit storage request.</summary>
    internal void AllocateRenderbufferStorage(GpuRenderbuffer owner, in RHIRenderbufferDescription description) => backend.AllocateRenderbufferStorage(owner, description);
    /// <summary>Applies the texture owner's sampling policy through the semantic execution boundary.</summary>
    internal void ConfigureTexture(GpuTexture owner, in RHISamplerDescription description) => backend.ConfigureTexture(owner, description);
    /// <summary>Applies assigned sampler fields under the existing owner's authority.</summary>
    internal void ConfigureSampler(GpuSampler owner, in RHISamplerDescription description) => backend.ConfigureSampler(owner, description);
    /// <summary>Changes the selected mip range under the texture owner's authority.</summary>
    internal void SetMipRange(GpuTexture owner, int baseLevel, int maxLevel) => backend.SetMipRange(owner, baseLevel, maxLevel);
    #endregion
    #region Internal API
    /// <summary>Initializes an unpublished specialized owner without exposing recipients to consumers.</summary>
    internal void InitializeTextureOwner(GpuTexture owner, in RHITextureDescription description) => backend.InitializeTextureOwner(owner, description);
    #endregion

}
