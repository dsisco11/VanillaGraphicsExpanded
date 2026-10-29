using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that framebuffer images consume manager-owned canonical texture facts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuImageMetadataProjectionRegressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A managed texture view projects its selected image without refreshing the canonical native reader.</summary>
    [Fact]
    public void ManagedViewAttachmentReusesCanonicalSnapshot()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = texture, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 2, LayerCount = 3, Label = null }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            var original = entry.Reader;
            try
            {
                // A managed image must use the already published entry even if another read would fail.
                entry.Reader = new RejectNativeRead();
                using var image = GpuFramebufferAttachment.FromTexture(view, layer: 1);
                Assert.Same(entry, manager.GetResourceRegistration(image));
                Assert.Equal(8, image.Width);
                Assert.Equal(4, image.Height);
                Assert.Equal(1, image.ImageDepth);
                Assert.True(entry.MetadataAvailable);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally { entry.Reader = original; }
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Raw-image association uses observed dimensions through the canonical manager entry.</summary>
    [Fact]
    public void ExternalImageAttachmentRefreshesCanonicalSnapshot()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 12, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)sentinel).Target), 0, sentinel.TextureId);
            using var reads = new ImageDescriptorCapture();
            using var image = GpuFramebufferAttachment.FromTextureId(texture.TextureId);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(image));
            Assert.Equal(12, image.Width);
            Assert.Equal(6, image.Height);
            Assert.Equal(12, texture.Width);
            Assert.Equal(0, reads.Reads);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuFramebufferAttachment.FromTextureId(texture.TextureId, mipLevel: 31));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>External cube-face selection uses the cube object target while projecting the requested face.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalCubeFaceProjectsCanonicalImage(bool dsa)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 8, Height = 8, MipLevels = 2, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (2) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTextureId(cube.TextureId, mipLevel: 1, cubeFace: CubeFace.NegativeZ);
            Assert.Equal(4, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(CubeFace.NegativeZ, image.CubeFace);
            Assert.Equal(TextureFormat.Rgba8, image.InternalFormat);
            Assert.Same(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(cube),
                RHIResourceManagerSystem.CurrentManager.GetResourceRegistration(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A raw array attachment retains explicit layer zero when imported and reconciled with canonical storage.</summary>
    [Fact]
    public void RawArrayLayerZeroDiscoveryPreservesSelectedPlane()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int texture = GL.GenTexture(), framebuffer = GL.GenFramebuffer();
        try
        {
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, texture))
                GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 8, 4, 3, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using (OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, framebuffer))
                GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, texture, 0, 0);
            using var wrapped = DynamicGpuFramebuffer.Wrap(framebuffer, width: 8, height: 4);
            wrapped.PublishRenderPassMetadata();
            var image = Assert.IsType<GpuFramebufferAttachment>(wrapped.GetAttachment(FramebufferSlot.Color0));
            Assert.Equal(0, image.Layer);
            Assert.Equal(8, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(1, image.ImageDepth);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(wrapped));
            var descriptor = entry.Metadata.Attachments[FramebufferSlot.Color0];
            Assert.Equal(0, descriptor.Layer);
            Assert.False(descriptor.Layered);
            Assert.Equal((nint)texture, descriptor.Id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, framebuffer));
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Fails immediately if a managed image projection incorrectly attempts another native acquisition.</summary>
    private sealed class RejectNativeRead : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Projection must reuse canonical facts.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata) => throw new InvalidOperationException("Projection must reuse canonical facts.");
    }
    #endregion
}
