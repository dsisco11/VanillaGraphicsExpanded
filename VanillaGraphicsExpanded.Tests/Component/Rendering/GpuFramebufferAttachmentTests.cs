using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Exercises attachment composition against real framebuffer storage and managed lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFramebufferAttachmentTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    /// <summary>Packed depth-stencil bindings keep the unaffected aspect when depth is replaced.</summary>
    #region Backing ownership and resize
    [Fact]
    public void PackedDepthStencilReplacementPreservesStencilBinding()
    {
        // Arrange
        EnsureContextValid();
        using var packed = new DepthStencilTexture(4, 4);
        using var depth = new DepthTexture(4, 4, TextureFormat.DepthComponent24);
        using var combined = GpuFramebufferAttachment.FromTexture(packed);
        using var depthOnly = GpuFramebufferAttachment.FromTexture(depth);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        // Act
        framebuffer.SetAttachment(FramebufferSlot.DepthStencil, combined);
        // Assert
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferSlot.Depth));
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferSlot.Stencil));
        framebuffer.SetAttachment(FramebufferSlot.Depth, depthOnly);
        Assert.Same(depthOnly, framebuffer.GetAttachment(FramebufferSlot.Depth));
        Assert.Same(combined, framebuffer.GetAttachment(FramebufferSlot.Stencil));
        framebuffer.RemoveAttachment(FramebufferSlot.DepthStencil);
        Assert.Null(framebuffer.GetAttachment(FramebufferSlot.Depth));
        Assert.Null(framebuffer.GetAttachment(FramebufferSlot.Stencil));
    }
    /// <summary>Borrowed wrappers observe explicit backing-resource retirement and cannot transfer its handle.</summary>
    [Fact]
    public void BorrowedAttachmentTracksBackingResourceRetirement()
    {
        // Arrange
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        // Act
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        // Assert
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
        // Arrange
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var first = DynamicGpuFramebuffer.Create([attachment])!;
        using var second = DynamicGpuFramebuffer.Create([attachment])!;
        int firstChanges = 0, secondChanges = 0;
        first.AttachmentsChanged += () => firstChanges++;
        second.AttachmentsChanged += () => secondChanges++;
        // Act
        bool resized = first.Resize(4, 3);
        // Assert
        Assert.True(resized);
        Assert.Equal(4, first.Width);
        Assert.Equal(3, second.Height);
        Assert.Equal(1, firstChanges);
        Assert.Equal(1, secondChanges);
        // Act
        bool unchanged = second.Resize(4, 3);
        // Assert
        Assert.False(unchanged);
        Assert.Equal(1, firstChanges);
        Assert.Equal(1, secondChanges);
    }
    /// <summary>Distinct image wrappers sharing storage publish resize changes to every affected framebuffer.</summary>
    [Fact]
    public void DistinctWrappersObserveSharedBackingResize()
    {
        // Arrange
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var firstImage = GpuFramebufferAttachment.FromTexture(texture);
        using var secondImage = GpuFramebufferAttachment.FromTexture(texture);
        using var first = DynamicGpuFramebuffer.Create([firstImage])!;
        using var second = DynamicGpuFramebuffer.Create([secondImage])!;
        int changes = 0;
        second.AttachmentsChanged += () => changes++;
        // Act
        bool resized = first.Resize(5, 3);
        // Assert
        Assert.True(resized);
        Assert.Equal(5, second.Width);
        Assert.Equal(1, changes);
    }
    #endregion

    /// <summary>Reallocating renderbuffer storage updates the wrapper format used for slot validation.</summary>
    #region Format changes and observers
    [Fact]
    public void RenderbufferReallocationRefreshesAttachmentFormat()
    {
        // Arrange
        EnsureContextValid();
        using var buffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromRenderbuffer(buffer);
        // Act
        buffer.AllocateStorage(TextureFormat.DepthComponent24, 2, 2);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        framebuffer.SetAttachment(FramebufferSlot.Depth, image);
        // Assert
        Assert.Throws<ArgumentException>(() => framebuffer.SetAttachment(FramebufferSlot.Color0, image));
    }
    /// <summary>A throwing observer cannot prevent the other framebuffers from seeing committed resize changes.</summary>
    [Fact]
    public void ThrowingObserverDoesNotSuppressOtherFramebufferInvalidation()
    {
        // Arrange
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var first = DynamicGpuFramebuffer.Create([image])!;
        using var second = DynamicGpuFramebuffer.Create([image])!;
        first.AttachmentsChanged += () => throw new InvalidOperationException("Injected observer failure");
        int changes = 0;
        second.AttachmentsChanged += () => changes++;
        // Act
        bool resized = first.Resize(3, 3);
        // Assert
        Assert.True(resized);
        Assert.Equal(1, changes);
        Assert.Equal(3, second.Width);
    }
    #endregion

    /// <summary>Manager replacement requires explicit shutdown and disposed managers cannot be installed.</summary>
    #region Manager lifetime
    [Fact]
    public void ManagerReplacementRequiresExplicitContextShutdown()
    {
        // Arrange
        EnsureContextValid();
        using var lifetime = new ManagerLifetime();
        // Act
        using var replacement = new RHIResourceManager();
        // Assert
        Assert.Throws<InvalidOperationException>(() => RHIResourceManagerSystem.Initialize(replacement));
        RHIResourceManagerSystem.Shutdown();
        replacement.Dispose();
        Assert.Throws<ObjectDisposedException>(() => RHIResourceManagerSystem.Initialize(replacement));
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
    #endregion
}
