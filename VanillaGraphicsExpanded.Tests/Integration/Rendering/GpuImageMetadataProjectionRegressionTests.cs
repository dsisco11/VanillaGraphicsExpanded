using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks that framebuffer images consume manager-owned canonical texture facts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageMetadataProjectionRegressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Raw-image association uses observed dimensions through the canonical manager entry.</summary>
    [Fact]
    public void ExternalImageAttachmentRefreshesCanonicalSnapshot()
    {
        // Arrange: retain an unrelated sentinel binding while native texture storage is changed behind the manager.
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
            // Act: create a framebuffer image from the externally changed texture name.
            using var image = GpuFramebufferAttachment.FromTextureId(texture.TextureId);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            // Assert: canonical metadata refresh observes the native size without disturbing the sentinel binding.
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

    /// <summary>A raw array attachment retains explicit layer zero when imported and reconciled with canonical storage.</summary>
    [Fact]
    public void RawArrayLayerZeroDiscoveryPreservesSelectedPlane()
    {
        // Arrange: create raw texture and framebuffer names whose first discovered attachment selects layer zero.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int texture = GL.GenTexture(), framebuffer = GL.GenFramebuffer();
        try
        {
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, texture))
                GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 8, 4, 3, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using (OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, framebuffer))
                GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, texture, 0, 0);
            // Act: wrap the native framebuffer and publish its discovered attachment metadata.
            using var wrapped = DynamicGpuFramebuffer.Wrap(framebuffer, width: 8, height: 4);
            wrapped.PublishRenderPassMetadata();
            var image = Assert.IsType<GpuFramebufferAttachment>(wrapped.GetAttachment(FramebufferSlot.Color0));
            // Assert: both the wrapper image and canonical framebuffer descriptor retain the selected plane.
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

}
