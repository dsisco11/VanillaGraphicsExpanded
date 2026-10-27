using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        using var draw = DynamicGpuFramebuffer.CreateEmpty("Tests.DrawAlias");
        using var read = DynamicGpuFramebuffer.CreateEmpty("Tests.ReadAlias");
        var cache = OpenGlStateCache.Current;
        try
        {
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, 0);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, draw.FboId);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, read.FboId);
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
        using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var layer = DynamicGpuFramebuffer.CreateEmpty("Tests.ReadbackLayer");
        layer.Attach(texture, 0);
        FramebufferTestSetup.ClearAttachments(layer, 1, 2, 3, 1);
        using var sourceTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var targetTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var source = DynamicGpuFramebuffer.CreateSingle(sourceTexture)!;
        using var target = DynamicGpuFramebuffer.CreateSingle(targetTexture)!;
        var cache = OpenGlStateCache.Current;
        try
        {
            FramebufferTestSetup.ClearAttachments(source, 1, 0, 0, 1);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, target.FboId);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, source.FboId);
            using (var pixels = texture.ReadPixelsRegion(0, 0, 2, 2, 0))
                Assert.Equal(1, pixels.Span[0]);
            Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(source.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            // This real follow-up operation consumes the cached combined binding while
            // restoring its state, reproducing the delayed error in the runtime fixture.
            var copy = FramebufferTestSetup.DescribeCopy(source, target);
            RHICommandContext.Current.BlitImages(copy);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.Equal(target.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        }
        finally { GpuFramebuffer.Unbind(); }
    }
    #endregion
}
