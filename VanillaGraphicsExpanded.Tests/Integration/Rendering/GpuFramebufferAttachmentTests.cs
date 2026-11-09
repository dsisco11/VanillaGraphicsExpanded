using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises attachment composition against real framebuffer storage and managed lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFramebufferAttachmentTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Replacing and retiring framebuffers never disposes their shared borrowed attachment.</summary>
    #region Native selection and compatibility
    [Fact]
    public void SharedAttachmentSurvivesFramebufferRemovalAndDisposal()
    {
        // Arrange
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacementTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementTexture);
        using var first = DynamicGpuFramebuffer.Create([attachment])!;
        using var second = DynamicGpuFramebuffer.Create([attachment])!;
        // Act
        first.SetAttachment(FramebufferSlot.Color0, replacement);
        // Assert
        Assert.Same(attachment, second.GetAttachment(FramebufferSlot.Color0));
        first.Dispose();
        Assert.True(replacement.IsValid);
        second.RemoveAttachment(FramebufferSlot.Color0);
        Assert.Null(second.GetAttachment(FramebufferSlot.Color0));
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
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 3, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (3) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture, mipLevel: 1);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        // Act
        framebuffer.SetAttachment(FramebufferSlot.Color3, attachment);
        // Assert
        Assert.Null(framebuffer.GetAttachment(FramebufferSlot.Color0));
        Assert.Same(attachment, framebuffer.GetAttachment(FramebufferSlot.Color3));
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
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture, layer: selectedLayer);
        using var framebuffer = DynamicGpuFramebuffer.Create([attachment])!;
        framebuffer.Bind();
        // Act
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferParameterName.FramebufferAttachmentTextureLayer, out int layer);
        // Assert
        Assert.Equal(selectedLayer, layer);
        Assert.True(framebuffer.CheckStatus(out string? error), error);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Legacy 32-bit integer depth storage remains valid in the new aspect validation.</summary>
    [Fact]
    public void DepthComponent32RemainsAttachable()
    {
        // Arrange
        EnsureContextValid();
        using var buffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.DepthComponent32, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        // Act
        using var image = GpuFramebufferAttachment.FromRenderbuffer(buffer);
        // Assert
        Assert.NotEqual((nint)0, buffer.ResourceId);
        Assert.Equal(buffer.ResourceId, image.ResourceId);
        using var framebuffer = DynamicGpuFramebuffer.Create([], image)!;
        Assert.True(framebuffer.CheckStatus(out string? error), error);
    }
    #endregion

    /// <summary>Late disposal from a retired context cannot migrate its resource into a replacement manager queue.</summary>
    #region Context and resource lifetime
    [Fact]
    public void OldContextAttachmentNeverEnqueuesIntoReplacementContext()
    {
        // Arrange
        EnsureContextValid();
        var oldLifetime = new ManagerLifetime();
        GpuFramebufferAttachment? attachment = null;
        ManagerLifetime? newLifetime = null;
        bool oldLifetimeDisposed = false;
        try
        {
            // Act
            attachment = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
            // Assert
            using var backing = Assert.IsType<DynamicTexture2D>(attachment.Resource);
            // Act
            oldLifetime.Dispose();
            oldLifetimeDisposed = true;
            newLifetime = new ManagerLifetime();
            attachment.Dispose();
            RHIResourceManagerSystem.CaptureDisposalQueue().DrainPending();

            // Assert
            Assert.False(backing.IsDisposed);
            Assert.True(GL.IsTexture(backing.TextureId));
        }
        finally
        {
            try
            {
                attachment?.Dispose();
            }
            finally
            {
                if (newLifetime is not null) newLifetime.Dispose();
                else if (!oldLifetimeDisposed) oldLifetime.Dispose();
            }
        }
    }
    /// <summary>Each framebuffer retains shared storage until its own reference is released.</summary>
    [Fact]
    public void SharedFramebuffersKeepOwningAttachmentReachableUntilLastRemoval()
    {
        // Arrange
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        using var first = DynamicGpuFramebuffer.CreateEmpty();
        using var second = DynamicGpuFramebuffer.CreateEmpty();
        var (weak, texture) = CreateSharedAttachment(first, second);
        first.RemoveAttachment(FramebufferSlot.Color0);
        GC.Collect();
        // Act
        GC.WaitForPendingFinalizers();
        // Assert
        Assert.True(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        second.RemoveAttachment(FramebufferSlot.Color0);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        RHIResourceManagerSystem.CaptureDisposalQueue().DrainPending();
        Assert.True(texture.IsDisposed);
    }
    /// <summary>Owning attachments defer both explicit cleanup and finalizer cleanup to the same render queue.</summary>
    [Fact]
    public void OwningAttachmentDisposesOnlyWhenRenderQueueDrains()
    {
        // Arrange
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        var queue = RHIResourceManagerSystem.CaptureDisposalQueue();
        // Act
        var attachment = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        // Assert
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
        // Arrange
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        var queue = RHIResourceManagerSystem.CaptureDisposalQueue();
        var (weak, texture) = CreateUnreachableAttachment();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        // Act
        GC.Collect();
        // Assert
        Assert.False(weak.IsAlive);
        Assert.False(texture.IsDisposed);
        queue.DrainPending();
        Assert.True(texture.IsDisposed);
    }
    #endregion
    #endregion
    #region Private

    /// <summary>Provides an isolated disposal context so test order cannot reuse a closed queue.</summary>
    private sealed class ManagerLifetime : IDisposable
    {
        private readonly RHIResourceManager manager;
        #region Public API
        /// <summary>Uses the observed context's manager and establishes its render thread.</summary>
        public ManagerLifetime()
        {
            manager = RHIResourceManagerSystem.EnsureCurrentContext();
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
        }
        /// <summary>Drains and closes the test context before releasing its manager.</summary>
        public void Dispose()
        {
            RHIResourceManagerSystem.Shutdown();
            manager.Dispose();
            TextureStreamingSystem.Dispose();
        }
        #endregion
    }
    /// <summary>Publishes the sole strong attachment references into the supplied framebuffer slots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Attachment, DynamicTexture2D Texture) CreateSharedAttachment(GpuFramebuffer first, GpuFramebuffer second)
    {
        var attachment = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        first.SetAttachment(FramebufferSlot.Color0, attachment);
        second.SetAttachment(FramebufferSlot.Color0, attachment);
        return (new WeakReference(attachment), (DynamicTexture2D)attachment.Resource!);
    }
    /// <summary>Separates the finalizable wrapper lifetime from the retained backing-resource probe.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Attachment, DynamicTexture2D Texture) CreateUnreachableAttachment()
    {
        var attachment = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        return (new WeakReference(attachment), (DynamicTexture2D)attachment.Resource!);
    }
    #endregion
}

