using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates framebuffer publications and configured copies across attachment lifetime changes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FramebufferAttachmentNotificationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>No-op renderbuffer resizes retain depth contents and only changed storage publishes an attachment update.</summary>
    [Fact]
    public void DepthRenderbufferResizePublishesOnlyStorageChanges()
    {
        EnsureContextValid();
        using var bindings = GlStateCache.Current.BindFramebufferScope();
        using var fixedState = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        using var depth = GpuRenderbuffer.Create(RenderbufferStorage.DepthComponent24, 2, 2);
        using var framebuffer = GpuFramebuffer.CreateDepthOnly(depth)!;
        framebuffer.Bind();
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { 0.375f });
        int notifications = 0;
        framebuffer.AttachmentsChanged += () => notifications++;

        // Equal dimensions must avoid reallocating storage, which would discard
        // the cleared depth values even though the framebuffer remains complete.
        Assert.False(depth.Resize(2, 2));
        Assert.False(framebuffer.Resize(2, 2));
        Assert.Equal(0, notifications);
        float[] pixels = new float[4];
        GL.ReadPixels(0, 0, 2, 2, PixelFormat.DepthComponent, PixelType.Float, pixels);
        Assert.All(pixels, value => Assert.InRange(value, 0.374f, 0.376f));

        Assert.True(framebuffer.Resize(4, 3));
        Assert.Equal(1, notifications);
        Assert.Equal(4, depth.Width);
        Assert.Equal(3, depth.Height);
        Assert.Equal(RenderbufferStorage.DepthComponent24, depth.Storage);
        Assert.Equal(0, depth.Samples);
        Assert.True(framebuffer.CheckStatus(out string? error), error);
        Assert.False(framebuffer.Resize(4, 3));
        Assert.Equal(1, notifications);
        depth.Dispose();
        Assert.False(depth.Resize(5, 5));
        Assert.False(framebuffer.Resize(5, 5));
        Assert.Equal(1, notifications);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Changed multisample renderbuffer storage preserves its original format and sample count.</summary>
    [Fact]
    public void RenderbufferResizePreservesMultisampleStorage()
    {
        EnsureContextValid();
        using var depth = GpuRenderbuffer.Create(RenderbufferStorage.DepthComponent24, 2, 2, samples: 4);
        Assert.True(depth.Resize(4, 3));
        Assert.False(depth.Resize(4, 3));
        using var binding = depth.BindScope();
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferWidth, out int width);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferHeight, out int height);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out int samples);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferInternalFormat, out int format);
        Assert.Equal(4, width);
        Assert.Equal(3, height);
        Assert.Equal(4, samples);
        Assert.Equal((int)RenderbufferStorage.DepthComponent24, format);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Ordinary copies retain configured images, and an explicit equal-size engine rebuild refreshes them.</summary>
    [Fact]
    public void WrappedRefreshReconfiguresOnlyAfterNotification()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var first = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var replacement = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var source = GpuFramebuffer.CreateSingle(first)!;
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var wrapped = GpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        first.UploadDataImmediate(Enumerable.Repeat(3f, 16).ToArray());
        replacement.UploadDataImmediate(Enumerable.Repeat(9f, 16).ToArray());
        int notifications = 0;
        wrapped.AttachmentsChanged += () => notifications++;
        using var blitter = new GpuFramebufferBlitter(wrapped, destination);
        blitter.Blit();
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(3f, value));

        // Engine-side mutation deliberately bypasses the wrapper. If Blit queries or
        // reattaches each frame, this copy would incorrectly observe the new image.
        source.Attach(replacement);
        blitter.Blit();
        Assert.Equal(0, notifications);
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(3f, value));
        wrapped.RefreshWrappedFramebuffer(source.FboId, 2, 2);
        Assert.Equal(1, notifications);
        blitter.Blit();
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(9f, value));
        Assert.True(GL.IsTexture(first.TextureId));
        Assert.True(GL.IsTexture(replacement.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Resize publishes one complete change, updates copy extents, and suppresses no-op texture resizes.</summary>
    [Fact]
    public void ResizePublishesCompletedAttachmentsAndUpdatesCopyDimensions()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var source = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var blitter = new GpuFramebufferBlitter(source, destination);
        int notifications = 0;
        source.AttachmentsChanged += () =>
        {
            notifications++;
            if (source.IsDisposed) return;
            Assert.Equal(4, source[0].Width);
            Assert.Equal(4, source[1].Width);
        };
        Assert.True(source.Resize(4, 3));
        Assert.Equal(1, notifications);
        Assert.False(source.Resize(4, 3));
        Assert.Equal(1, notifications);
        Assert.True(destination.Resize(4, 3));
        source[0].UploadDataImmediate(Enumerable.Repeat(7f, 4 * 3 * 4).ToArray());
        blitter.Blit();
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(7f, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Target retirement emits once and cannot leave a configured blitter able to copy stale storage.</summary>
    [Fact]
    public void RetirementWithdrawsConfiguredCopyTargets()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var blitter = new GpuFramebufferBlitter(source, destination);
        int notifications = 0;
        source.AttachmentsChanged += () => notifications++;
        source.Dispose();
        Assert.Equal(1, notifications);
        source.Dispose();
        Assert.Equal(1, notifications);
        Assert.Throws<ObjectDisposedException>(() => blitter.Blit());
        Assert.True(destination.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Disposal removes target subscriptions so still-live framebuffers do not retain the blitter.</summary>
    [Fact]
    public void DisposedBlitterIsNotRetainedByLiveTargets()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        var retired = CreateDisposedBlitter(source, destination);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(retired.IsAlive);
        Assert.True(source.Resize(3, 3));
        Assert.True(destination.Resize(3, 3));
        Assert.True(source.IsValid);
        Assert.True(destination.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Ends the local strong-reference lifetime before checking event subscriber retention.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedBlitter(GpuFramebuffer source, GpuFramebuffer destination)
    {
        using var blitter = new GpuFramebufferBlitter(source, destination);
        blitter.Blit();
        return new WeakReference(blitter);
    }
    #endregion
}
