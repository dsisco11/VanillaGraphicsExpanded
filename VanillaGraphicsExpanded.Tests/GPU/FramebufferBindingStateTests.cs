using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks combined framebuffer queries against the driver-defined draw binding alias.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FramebufferBindingStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Binding aliases
    /// <summary>Cached and freshly queried combined bindings alias draw without overwriting a distinct read binding.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CombinedQueryPreservesDistinctReadBinding(bool invalidate)
    {
        EnsureContextValid();
        using var draw = GpuFramebuffer.CreateEmpty("Tests.DrawAlias");
        using var read = GpuFramebuffer.CreateEmpty("Tests.ReadAlias");
        var cache = GlStateCache.Current;
        try
        {
            cache.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw.FboId);
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
            if (invalidate) cache.InvalidateAll();
            Assert.Equal(draw.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.FramebufferBinding));
            Assert.Equal(read.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuFramebuffer.Unbind(); }
    }
    #endregion

    #region Readback and subsequent rendering
    /// <summary>Deleting a temporary layer-readback framebuffer cannot leave its name cached for a later blit restore.</summary>
    [Fact]
    public void LayerReadbackRetiresTemporaryBindingBeforeBlit()
    {
        EnsureContextValid();
        using var texture = Texture3D.Create(2, 2, 1, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        using var layer = GpuFramebuffer.CreateEmpty("Tests.ReadbackLayer");
        layer.AttachColorLayer(texture, 0);
        layer.BindAndClear(1, 2, 3, 1);
        using var sourceTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var targetTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var source = GpuFramebuffer.CreateSingle(sourceTexture)!;
        using var target = GpuFramebuffer.CreateSingle(targetTexture)!;
        var cache = GlStateCache.Current;
        try
        {
            source.BindAndClear(1, 0, 0, 1);
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.FboId);
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source.FboId);
            using (var pixels = texture.ReadPixelsRegion(0, 0, 2, 2, 0))
                Assert.Equal(1, pixels.Span[0]);
            Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(source.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            // This real follow-up operation consumes the cached combined binding while
            // restoring its state, reproducing the delayed error in the runtime fixture.
            target.BlitFrom(source);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.Equal(target.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        }
        finally { GpuFramebuffer.Unbind(); }
    }
    #endregion
}
