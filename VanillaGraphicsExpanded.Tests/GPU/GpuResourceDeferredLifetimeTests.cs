using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks alias guards across framebuffer replacement and deferred owning cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceDeferredLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Identity replacement waits for the original borrow while same-identity facts can refresh.</summary>
    [Fact]
    public void BorrowedFramebufferReplacementPreservesOriginalGuardAssociation()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var first = DynamicGpuFramebuffer.CreateEmpty();
        using var second = DynamicGpuFramebuffer.CreateEmpty();
        first.Bind();
        second.Bind();
        using var borrower = DynamicGpuFramebuffer.Wrap(first.FboId, width: 4, height: 4);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var original = manager.GetResourceRegistration(borrower);
        int notifications = 0;
        borrower.AttachmentsChanged += () => notifications++;
        borrower.RetainLifetimeReference();
        try
        {
            Assert.Throws<InvalidOperationException>(() => borrower.RefreshWrappedFramebuffer(second.FboId, 8, 8, false));
            Assert.Equal(first.FboId, borrower.FboId);
            Assert.Equal(4, borrower.Width);
            Assert.Same(original, manager.GetResourceRegistration(borrower));
            Assert.Equal(0, notifications);
            borrower.RefreshWrappedFramebuffer(first.FboId, 6, 6, false);
            Assert.Same(original, manager.GetResourceRegistration(borrower));
            Assert.Equal(6, borrower.Width);
            Assert.Equal(1, notifications);
        }
        finally { borrower.ReleaseLifetimeReference(); }
        borrower.RefreshWrappedFramebuffer(second.FboId, 8, 8, false);
        Assert.Same(manager.GetResourceRegistration(second), manager.GetResourceRegistration(borrower));
        first.Dispose();
        borrower.Dispose();
        second.Dispose();
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        fixture.CleanupGpuResources();
        StateCache.Current.InvalidateAll();
    }

    /// <summary>A queued owner survives both alias guard kinds and retires when the borrow releases.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredAttachmentOwnerRetriesAfterSharedGuardRelease(bool pass)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var attachment = new GpuFramebufferAttachment(4, 4, PixelInternalFormat.Rgba8);
        var owner = Assert.IsType<DynamicTexture2D>(attachment.Resource);
        int nativeId = owner.TextureId;
        using var alias = new BorrowedTexture(nativeId);
        var queue = GpuResourceManagerSystem.CaptureDisposalQueue();
        using var unrelated = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        if (pass) alias.RetainPassReference(); else alias.RetainLifetimeReference();
        try
        {
            attachment.Dispose();
            queue.Enqueue(unrelated);
            queue.Enqueue(owner);
            queue.DrainPending();
            Assert.True(unrelated.IsDisposed);
            Assert.False(owner.IsDisposed);
            Assert.True(GL.IsTexture(nativeId));
        }
        finally { if (pass) alias.ReleasePassReference(); else alias.ReleaseLifetimeReference(); }
        try
        {
            queue.DrainPending();
            Assert.True(owner.IsDisposed);
            Assert.False(GL.IsTexture(nativeId));
            queue.DrainPending();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            attachment.Dispose();
            owner.Dispose();
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
