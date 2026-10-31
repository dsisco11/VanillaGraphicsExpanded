using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks original alias identity across owner replacement and deferred cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceDeferredLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Owner replacement refreshes its alias only at the explicit lifecycle boundary.</summary>
    [Fact]
    public void BorrowedFramebufferReplacementPreservesOriginalAssociation()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var first = DynamicGpuFramebuffer.CreateEmpty();
        using var second = DynamicGpuFramebuffer.CreateEmpty();
        first.Bind();
        second.Bind();
        using var borrower = DynamicGpuFramebuffer.Wrap(first.FboId, width: 4, height: 4);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var original = manager.GetResourceRegistration(borrower);
        int notifications = 0;
        borrower.AttachmentsChanged += () => notifications++;
        borrower.RefreshWrappedFramebuffer(first.FboId, 6, 6, false);
        Assert.Same(original, manager.GetResourceRegistration(borrower));
        Assert.Equal(6, borrower.Width);
        Assert.Equal(1, notifications);
        // No future use of the old selection remains when its owner follows replacement.
        borrower.RefreshWrappedFramebuffer(second.FboId, 8, 8, false);
        Assert.Same(manager.GetResourceRegistration(second), manager.GetResourceRegistration(borrower));
        first.Dispose();
        borrower.Dispose();
        second.Dispose();
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        fixture.CleanupGpuResources();
        OpenGlStateCache.Current.InvalidateAll();
    }

    /// <summary>A queued owner retires at the lifecycle drain and withdraws non-owning aliases.</summary>
    [Fact]
    public void DeferredAttachmentOwnerWithdrawsAliasesOnDrain()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var attachment = new GpuFramebufferAttachment(4, 4, TextureFormat.Rgba8);
        var owner = Assert.IsType<DynamicTexture2D>(attachment.Resource);
        int nativeId = owner.TextureId;
        using var alias = new BorrowedTexture(nativeId);
        var queue = RHIResourceManagerSystem.CaptureDisposalQueue();
        using var unrelated = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        attachment.Dispose();
        queue.Enqueue(unrelated);
        queue.Enqueue(owner);
        try
        {
            queue.DrainPending();
            Assert.True(owner.IsDisposed);
            Assert.False(GL.IsTexture(nativeId));
            Assert.False(alias.IsValid);
            Assert.True(unrelated.IsDisposed);
            queue.DrainPending();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            attachment.Dispose();
            owner.Dispose();
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
