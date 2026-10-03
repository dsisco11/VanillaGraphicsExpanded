using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises attachment composition against real framebuffer storage and managed lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferAttachmentTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Replacing and retiring framebuffers never disposes their shared borrowed attachment.</summary>
    [Fact]
    public void SharedAttachmentSurvivesFramebufferRemovalAndDisposal()
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var replacementTexture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementTexture);
        using var first = GpuFramebuffer.Create([attachment])!;
        using var second = GpuFramebuffer.Create([attachment])!;
        first.SetAttachment(FramebufferAttachment.ColorAttachment0, replacement);
        Assert.Same(attachment, second.GetAttachment(FramebufferAttachment.ColorAttachment0));
        first.Dispose();
        Assert.True(replacement.IsValid);
        second.RemoveAttachment(FramebufferAttachment.ColorAttachment0);
        Assert.Null(second.GetAttachment(FramebufferAttachment.ColorAttachment0));
        Assert.True(attachment.IsValid);
        attachment.Dispose();
        Assert.Equal((nint)0, attachment.ResourceId);
        Assert.True(texture.IsValid);
        Assert.True(GL.IsTexture(texture.TextureId));
    }

    /// <summary>Sparse slots retain only real bindings and selected mip dimensions match the GL image.</summary>
    [Fact]
    public void SparseMipAttachmentRetainsExactImageSelection()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var texture = DynamicTexture2D.CreateMipmapped(8, 4, PixelInternalFormat.Rgba8, 3);
        using var attachment = GpuFramebufferAttachment.FromTexture(texture, mipLevel: 1);
        using var framebuffer = GpuFramebuffer.CreateEmpty();
        framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment3, attachment);
        Assert.Null(framebuffer.GetAttachment(FramebufferAttachment.ColorAttachment0));
        Assert.Same(attachment, framebuffer.GetAttachment(FramebufferAttachment.ColorAttachment3));
        Assert.Equal(4, attachment.Width);
        Assert.Equal(2, attachment.Height);
        framebuffer.Bind();
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3,
            FramebufferParameterName.FramebufferAttachmentTextureLevel, out int level);
        Assert.Equal(1, level);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Layer zero is a selected single image rather than a whole layered attachment.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArrayLayerSelectionMatchesDriverAttachment(int selectedLayer)
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var texture = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8);
        using var attachment = GpuFramebufferAttachment.FromTexture(texture, layer: selectedLayer);
        using var framebuffer = GpuFramebuffer.Create([attachment])!;
        framebuffer.Bind();
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferParameterName.FramebufferAttachmentTextureLayer, out int layer);
        Assert.Equal(selectedLayer, layer);
        Assert.True(framebuffer.CheckStatus(out string? error), error);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Packed depth-stencil bindings keep the unaffected aspect when depth is replaced.</summary>
    [Fact]
    public void PackedDepthStencilReplacementPreservesStencilBinding()
    {
        EnsureContextValid();
        using var packed = new DepthStencilTexture(4, 4);
        using var depth = new DepthTexture(4, 4, PixelInternalFormat.DepthComponent24);
        using var combined = GpuFramebufferAttachment.FromTexture(packed);
        using var depthOnly = GpuFramebufferAttachment.FromTexture(depth);
        using var framebuffer = GpuFramebuffer.CreateEmpty();
        framebuffer.SetAttachment(FramebufferAttachment.DepthStencilAttachment, combined);
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferAttachment.DepthAttachment));
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferAttachment.StencilAttachment));
        framebuffer.SetAttachment(FramebufferAttachment.DepthAttachment, depthOnly);
        Assert.Same(depthOnly, framebuffer.GetAttachment(FramebufferAttachment.DepthAttachment));
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferAttachment.StencilAttachment));
        framebuffer.RemoveAttachment(FramebufferAttachment.DepthStencilAttachment);
        Assert.Null(framebuffer.GetAttachment(FramebufferAttachment.DepthAttachment));
        Assert.Null(framebuffer.GetAttachment(FramebufferAttachment.StencilAttachment));
    }
    /// <summary>Borrowed wrappers observe explicit backing-resource retirement and cannot transfer its handle.</summary>
    [Fact]
    public void BorrowedAttachmentTracksBackingResourceRetirement()
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        Assert.Equal((nint)texture.TextureId, ((GpuResource)texture).ResourceId);
        Assert.Equal(texture.ResourceId, ((GpuResource)attachment).ResourceId);
        Assert.Throws<NotSupportedException>(() => attachment.Detach());
        Assert.Throws<NotSupportedException>(() => attachment.ReleaseHandle());
        texture.Dispose();
        Assert.Equal((nint)0, texture.ResourceId);
        Assert.Equal((nint)0, attachment.ResourceId);
        Assert.False(attachment.IsValid);
    }
    /// <summary>One shared image resize updates both framebuffer extents and publishes once per observer.</summary>
    [Fact]
    public void SharedAttachmentResizeNotifiesEveryFramebuffer()
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var first = GpuFramebuffer.Create([attachment])!;
        using var second = GpuFramebuffer.Create([attachment])!;
        int firstChanges = 0, secondChanges = 0;
        first.AttachmentsChanged += () => firstChanges++;
        second.AttachmentsChanged += () => secondChanges++;
        Assert.True(first.Resize(4, 3));
        Assert.Equal(4, first.Width);
        Assert.Equal(3, second.Height);
        Assert.Equal(1, firstChanges);
        Assert.Equal(1, secondChanges);
        Assert.False(second.Resize(4, 3));
        Assert.Equal(1, firstChanges);
        Assert.Equal(1, secondChanges);
    }
    /// <summary>Distinct image wrappers sharing storage publish resize changes to every affected framebuffer.</summary>
    [Fact]
    public void DistinctWrappersObserveSharedBackingResize()
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var firstImage = GpuFramebufferAttachment.FromTexture(texture);
        using var secondImage = GpuFramebufferAttachment.FromTexture(texture);
        using var first = GpuFramebuffer.Create([firstImage])!;
        using var second = GpuFramebuffer.Create([secondImage])!;
        int changes = 0;
        second.AttachmentsChanged += () => changes++;
        Assert.True(first.Resize(5, 3));
        Assert.Equal(5, second.Width);
        Assert.Equal(1, changes);
    }

    /// <summary>Legacy 32-bit integer depth storage remains valid in the new aspect validation.</summary>
    [Fact]
    public void DepthComponent32RemainsAttachable()
    {
        EnsureContextValid();
        using var buffer = GpuRenderbuffer.Create(RenderbufferStorage.DepthComponent32, 2, 2);
        using var image = GpuFramebufferAttachment.FromRenderbuffer(buffer);
        Assert.NotEqual((nint)0, buffer.ResourceId);
        Assert.Equal(buffer.ResourceId, image.ResourceId);
        using var framebuffer = GpuFramebuffer.Create([], image)!;
        Assert.True(framebuffer.CheckStatus(out string? error), error);
    }

    /// <summary>Reallocating renderbuffer storage updates the wrapper format used for slot validation.</summary>
    [Fact]
    public void RenderbufferReallocationRefreshesAttachmentFormat()
    {
        EnsureContextValid();
        using var buffer = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        using var image = GpuFramebufferAttachment.FromRenderbuffer(buffer);
        buffer.AllocateStorage(RenderbufferStorage.DepthComponent24, 2, 2);
        using var framebuffer = GpuFramebuffer.CreateEmpty();
        framebuffer.SetAttachment(FramebufferAttachment.DepthAttachment, image);
        Assert.Throws<ArgumentException>(() => framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, image));
    }
    /// <summary>A throwing observer cannot prevent the other framebuffers from seeing committed resize changes.</summary>
    [Fact]
    public void ThrowingObserverDoesNotSuppressOtherFramebufferInvalidation()
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var first = GpuFramebuffer.Create([image])!;
        using var second = GpuFramebuffer.Create([image])!;
        first.AttachmentsChanged += () => throw new InvalidOperationException("Injected observer failure");
        int changes = 0;
        second.AttachmentsChanged += () => changes++;
        Assert.True(first.Resize(3, 3));
        Assert.Equal(1, changes);
        Assert.Equal(3, second.Width);
    }

    /// <summary>Manager replacement requires explicit shutdown and disposed managers cannot be installed.</summary>
    [Fact]
    public void ManagerReplacementRequiresExplicitContextShutdown()
    {
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        using var replacement = new GpuResourceManager();
        Assert.Throws<InvalidOperationException>(() => GpuResourceManagerSystem.Initialize(replacement));
        GpuResourceManagerSystem.Shutdown();
        replacement.Dispose();
        Assert.Throws<ObjectDisposedException>(() => GpuResourceManagerSystem.Initialize(replacement));
    }

    /// <summary>Late disposal from a retired context cannot migrate its resource into a replacement manager queue.</summary>
    [Fact]
    public void OldContextAttachmentNeverEnqueuesIntoReplacementContext()
    {
        EnsureContextValid();
        var oldLifetime = new ManagerLifetime();
        var attachment = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var backing = Assert.IsType<DynamicTexture2D>(attachment.Resource);
        oldLifetime.Dispose();
        using var newLifetime = new ManagerLifetime();
        attachment.Dispose();
        GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();
        Assert.False(backing.IsDisposed);
        Assert.True(GL.IsTexture(backing.TextureId));
    }
    /// <summary>Each framebuffer retains shared storage until its own reference is released.</summary>
    [Fact]
    public void SharedFramebuffersKeepOwningAttachmentReachableUntilLastRemoval()
    {
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        using var first = GpuFramebuffer.CreateEmpty();
        using var second = GpuFramebuffer.CreateEmpty();
        var (weak, texture) = CreateSharedAttachment(first, second);
        first.RemoveAttachment(FramebufferAttachment.ColorAttachment0);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.True(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        second.RemoveAttachment(FramebufferAttachment.ColorAttachment0);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();
        Assert.True(texture.IsDisposed);
    }
    /// <summary>Owning attachments defer both explicit cleanup and finalizer cleanup to the same render queue.</summary>
    [Fact]
    public void OwningAttachmentDisposesOnlyWhenRenderQueueDrains()
    {
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        var queue = GpuResourceManagerSystem.CaptureDisposalQueue();
        var attachment = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        var texture = Assert.IsType<DynamicTexture2D>(attachment.Resource);
        int handle = texture.TextureId;
        attachment.Dispose();
        attachment.Dispose();
        Assert.False(texture.IsDisposed);
        Assert.True(GL.IsTexture(handle));
        queue.DrainPending();
        Assert.True(texture.IsDisposed);
        Assert.False(GL.IsTexture(handle));
    }

    /// <summary>Unreachable owning attachments enqueue storage without executing GL on the finalizer thread.</summary>
    [Fact]
    public void UnreachableOwningAttachmentQueuesResourceForRenderThread()
    {
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        var queue = GpuResourceManagerSystem.CaptureDisposalQueue();
        var (weak, texture) = CreateUnreachableAttachment();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        queue.DrainPending();
        Assert.True(texture.IsDisposed);
    }
    #endregion

    #region Private
    /// <summary>Provides an isolated disposal context so test order cannot reuse a closed queue.</summary>
    private sealed class ManagerLifetime : IDisposable
    {
        private readonly GpuResourceManager manager = new();
        /// <summary>Installs a new context lifetime and establishes its render thread.</summary>
        public ManagerLifetime()
        {
            GpuResourceManagerSystem.Initialize(manager);
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
        }
        /// <summary>Drains and closes the test context before releasing its manager.</summary>
        public void Dispose()
        {
            GpuResourceManagerSystem.Shutdown();
            manager.Dispose();
            TextureStreamingSystem.Dispose();
        }
    }
    /// <summary>Publishes the sole strong attachment references into the supplied framebuffer slots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Attachment, DynamicTexture2D Texture) CreateSharedAttachment(GpuFramebuffer first, GpuFramebuffer second)
    {
        var attachment = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        first.SetAttachment(FramebufferAttachment.ColorAttachment0, attachment);
        second.SetAttachment(FramebufferAttachment.ColorAttachment0, attachment);
        return (new WeakReference(attachment), (DynamicTexture2D)attachment.Resource!);
    }
    /// <summary>Separates the finalizable wrapper lifetime from the retained backing-resource probe.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Attachment, DynamicTexture2D Texture) CreateUnreachableAttachment()
    {
        var attachment = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        return (new WeakReference(attachment), (DynamicTexture2D)attachment.Resource!);
    }
    #endregion
}
