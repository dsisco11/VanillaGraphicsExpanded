using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Validates framebuffer publications and configured copies across attachment lifetime changes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FramebufferAttachmentNotificationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API

    #region Renderbuffer storage notifications
    /// <summary>No-op renderbuffer resizes retain depth contents and only changed storage publishes an attachment update.</summary>
    [Fact]
    public void DepthRenderbufferResizePublishesOnlyStorageChanges()
    {
        // Arrange
        EnsureContextValid();
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        using var fixedState = LegacyFixedFunctionReference.Capture(OpenGlStateCache.Current);
        using var depth = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.DepthComponent24, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var framebuffer = DynamicGpuFramebuffer.CreateDepthOnly(depth)!;
        framebuffer.Bind();
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { 0.375f });
        int notifications = 0;
        framebuffer.AttachmentsChanged += () => notifications++;

        // Equal dimensions must avoid reallocating storage, which would discard
        // the cleared depth values even though the framebuffer remains complete.
        // Act
        Assert.False(depth.Resize(2, 2));

        // Assert
        Assert.False(framebuffer.Resize(2, 2));
        Assert.Equal(0, notifications);
        using var contents = Assertions.GpuImageSnapshot.FromAttachment(framebuffer.GetAttachment(FramebufferSlot.Depth)!, ImageAspects.Depth);
        ImageAssertions.Statistics.Inspect(contents.Texture, 0.374f, 0.376f).AssertFiniteRange();

        // Act: changed dimensions reallocate the attached renderbuffer and publish an event.
        Assert.True(framebuffer.Resize(4, 3));

        // Assert
        Assert.Equal(1, notifications);
        Assert.Equal(4, depth.Width);
        Assert.Equal(3, depth.Height);
        Assert.Equal(TextureFormat.DepthComponent24, depth.Storage);
        Assert.Equal(0, depth.Samples);
        Assert.True(framebuffer.CheckStatus(out string? error), error);

        // Act: a repeat at the new size is still a no-op.
        Assert.False(framebuffer.Resize(4, 3));

        // Assert
        Assert.Equal(1, notifications);

        // Act: deleting the attached renderbuffer detaches it and publishes another attachment change.
        depth.Dispose();
        Assert.False(depth.Resize(5, 5));
        Assert.False(framebuffer.Resize(5, 5));

        // Assert
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            FramebufferParameterName.FramebufferAttachmentObjectName, out int attachment);
        Assert.Equal(0, attachment);
        Assert.Equal(2, notifications);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Changed multisample renderbuffer storage preserves its original format and sample count.</summary>
    [Fact]
    public void RenderbufferResizePreservesMultisampleStorage()
    {
        // Arrange
        EnsureContextValid();
        using var depth = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.DepthComponent24, Width = 2, Height = 2, Samples = 4, Label = null }).RequireResource();
        // Act
        Assert.True(depth.Resize(4, 3));

        // Act: a repeat resize does not report another storage change.
        Assert.False(depth.Resize(4, 3));

        // Assert
        using var binding = OpenGlStateCache.Current.BindRenderbufferScope(depth.RenderbufferId);
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
    #endregion

    #region Wrapped attachment publication
    /// <summary>Observed attachment changes notify borrowers before an explicit wrapped refresh.</summary>
    [Fact]
    public void WrappedRefreshReconfiguresOnlyAfterNotification()
    {
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacement = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var source = DynamicGpuFramebuffer.CreateSingle(first)!;
        using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        using var wrapped = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        first.UploadDataImmediate(Enumerable.Repeat(3f, 16).ToArray());
        replacement.UploadDataImmediate(Enumerable.Repeat(9f, 16).ToArray());
        int notifications = 0;
        wrapped.AttachmentsChanged += () => notifications++;
        var copy = FramebufferTestSetup.DescribeCopy(source, destination);

        // Act
        RHICommandContext.Current.BlitImages(copy);

        // Assert
        ImageAssertions.Comparisons.CompareConstant(destination[0], 3f).AssertEqual();

        // The observer reports the change; an already prepared copy still retains
        // its original image selection until the caller rebuilds its description.
        // Act: the notification reports changed source storage, while the prepared copy remains unchanged.
        source.Attach(replacement);
        RHICommandContext.Current.BlitImages(copy);

        // Assert
        Assert.Equal(1, notifications);
        ImageAssertions.Comparisons.CompareConstant(destination[0], 3f).AssertEqual();
        // Act: explicit refresh allows a new copy description to observe the replacement attachment.
        wrapped.RefreshWrappedFramebuffer(source.FboId, 2, 2);
        copy = FramebufferTestSetup.DescribeCopy(source, destination);
        // Assert
        Assert.Equal(2, notifications);

        // Act
        RHICommandContext.Current.BlitImages(copy);

        // Assert
        ImageAssertions.Comparisons.CompareConstant(destination[0], 9f).AssertEqual();
        Assert.True(GL.IsTexture(first.TextureId));
        Assert.True(GL.IsTexture(replacement.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Resize publication and configured copies
    /// <summary>Resize publishes one complete change, updates copy extents, and suppresses no-op texture resizes.</summary>
    [Fact]
    public void ResizePublishesCompletedAttachmentsAndUpdatesCopyDimensions()
    {
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = CreateMRTRenderTarget(2, 2, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination);
        int notifications = 0;
        source.AttachmentsChanged += () =>
        {
            notifications++;
            if (source.IsDisposed) return;
            Assert.Equal(4, source[0].Width);
            Assert.Equal(4, source[1].Width);
        };
        // Act
        Assert.True(source.Resize(4, 3));

        // Assert
        Assert.Equal(1, notifications);

        // Act: resize the destination, then rebuild and execute a copy using the published source dimensions.
        Assert.False(source.Resize(4, 3));
        Assert.Equal(1, notifications);
        Assert.True(destination.Resize(4, 3));
        source[0].UploadDataImmediate(Enumerable.Repeat(7f, 4 * 3 * 4).ToArray());
        copy = FramebufferTestSetup.DescribeCopy(source, destination);
        RHICommandContext.Current.BlitImages(copy);

        // Assert
        ImageAssertions.Comparisons.CompareConstant(destination[0], 7f).AssertEqual();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Copy lifetime
    /// <summary>A configured copy retains usable source storage after framebuffer retirement and fails after texture retirement.</summary>
    [Fact]
    public void ImageCopyRetainsStorageIndependentlyOfFramebufferLifetime()
    {
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination);
        int notifications = 0;
        source.AttachmentsChanged += () => notifications++;
        // Act
        source.Dispose();

        // Assert
        Assert.Equal(1, notifications);

        // Act: repeating disposal is harmless; the configured copy can still read its retained storage.
        source.Dispose();
        Assert.Equal(1, notifications);
        RHICommandContext.Current.BlitImages(copy);

        // Assert
        Assert.True(copy.Source.Texture.IsValid);

        // Act: retire the retained source storage and attempt the same copy again.
        copy.Source.Texture.Resource!.Dispose();

        // Assert
        Assert.ThrowsAny<InvalidOperationException>(() => RHICommandContext.Current.BlitImages(copy));
        Assert.True(destination.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A completed blit does not prevent either target from resizing while both remain valid.</summary>
    [Fact]
    public void CompletedBlitDoesNotPreventTargetResize()
    {
        // Arrange
        EnsureContextValid();

        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        // Act
        RHICommandContext.Current.BlitImages(FramebufferTestSetup.DescribeCopy(source, destination));
        Assert.True(source.Resize(3, 3));
        Assert.True(destination.Resize(3, 3));

        // Assert
        Assert.True(source.IsValid);
        Assert.True(destination.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion

    #region Private Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
