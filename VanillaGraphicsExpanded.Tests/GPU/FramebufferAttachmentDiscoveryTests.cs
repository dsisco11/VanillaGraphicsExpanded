using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies imported framebuffer images preserve their layer and cube-face selections during copies.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FramebufferAttachmentDiscoveryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Configured copies reject retired borrowed color or depth storage before issuing driver work.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredBlitterRejectsRetiredBackingStorage(bool retireDepth)
    {
        EnsureContextValid();
        using var color = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var depth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var source = GpuFramebuffer.CreateSingle(color, depth)!;
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var blitter = new GpuFramebufferBlitter(source, destination);
        if (retireDepth) depth.Dispose();
        else color.Dispose();
        Assert.Throws<InvalidOperationException>(() => blitter.Blit());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>External array attachments preserve layer zero and nonzero layers when configuring scratch FBOs.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExternalArrayLayerCopiesSelectedImage(int layer)
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var texture = DynamicTexture3D.Create(2, 2, 2, PixelInternalFormat.Rgba32f);
        using var image = GpuFramebufferAttachment.FromTexture(texture, layer: layer);
        using var source = GpuFramebuffer.Create([image])!;
        source.Bind();
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 0.25f, 0.5f, 0.75f, 1f });
        using var external = GpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var blitter = new GpuFramebufferBlitter(external, destination);
        blitter.Blit();
        Assert.Equal(new[] { 0.25f, 0.5f, 0.75f, 1f }, destination[0].ReadPixels()[..4]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>External cube-map attachments import the selected face without treating it as a 2D texture.</summary>
    [Fact]
    public void ExternalCubeFaceCopiesSelectedImage()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        int texture = GL.GenTexture();
        try
        {
            // No cube-map allocation wrapper exists; allocate every face to provide complete cube storage.
            using (GlStateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap, 0, texture))
            {
                for (int face = 0; face < 6; face++)
                    GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, PixelInternalFormat.Rgba32f,
                        2, 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            }
            using var source = GpuFramebuffer.CreateEmpty();
            source.Bind();
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapNegativeZ, texture, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 0.125f, 0.25f, 0.5f, 1f });
            using var external = GpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
            using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
            using var blitter = new GpuFramebufferBlitter(external, destination);
            blitter.Blit();
            Assert.Equal(new[] { 0.125f, 0.25f, 0.5f, 1f }, destination[0].ReadPixels()[..4]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GlStateCache.Current.DeleteTexture(texture);
        }
    }
    #endregion
}
