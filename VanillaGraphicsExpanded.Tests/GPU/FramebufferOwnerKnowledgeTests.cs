using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies owner lifecycle events preserve executed routing and unrelated selector knowledge.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FramebufferOwnerKnowledgeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Detaching an owner transfers lifetime authority without changing the native output route.</summary>
    [Fact]
    public void DetachPreservesExecutedRouting()
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        var cache = OpenGlStateCache.Current;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var expected));
        int id = checked((int)target.Detach());
        try
        {
            Assert.True(cache.TryCopyFramebufferDrawRouting(id, out var actual));
            Assert.Equal(expected, actual);
        }
        finally { RHICommandContext.Current.RetireResource(new(GpuResourceKind.Framebuffer, id)); }
        Assert.False(cache.TryCopyFramebufferDrawRouting(id, out _));
    }

    /// <summary>Publishing the same borrowed framebuffer leaves its independently tracked output route intact.</summary>
    [Fact]
    public void BorrowedRefreshPreservesExecutedRouting()
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var borrowed = DynamicGpuFramebuffer.Wrap(target.FboId, width: 2, height: 2);
        var cache = OpenGlStateCache.Current;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var expected));
        borrowed.RefreshWrappedFramebuffer(target.FboId, 2, 2, publishPassMetadata: true);
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var actual));
        Assert.Equal(expected, actual);
        Assert.True(target.IsValid);
    }

    /// <summary>Resize callbacks preserve tracked selectors and routes while retaining declared callback ordering.</summary>
    [Fact]
    public void ScreenResizePreservesKnowledgeAndCallbackOrdering()
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var expected));
        var order = new List<int>();
        var removeLate = ScreenResourceManager.Register(int.MaxValue, () => order.Add(2));
        var removeEarly = ScreenResourceManager.Register(int.MinValue, () => order.Add(1));
        try { ScreenResourceManager.HandleScreenResize(); }
        finally { removeEarly(); removeLate(); }
        Assert.Equal(new[] { 1, 2 }, order);
        Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int selected));
        Assert.Equal(target.FboId, selected);
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
    }
    #endregion
}
