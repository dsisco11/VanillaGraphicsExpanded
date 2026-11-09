using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies managed alias association changes at explicit framebuffer lifecycle boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceDeferredLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Refreshing a borrower keeps its canonical association until the selected owner changes.</summary>
    [Fact]
    public void BorrowedFramebufferReplacementPreservesOriginalAssociation()
    {
        // Arrange: bind two real framebuffers so the managed wrapper can query their descriptors.
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

        // Act: refresh within the same owner, then point the wrapper at a replacement owner.
        borrower.RefreshWrappedFramebuffer(first.FboId, 6, 6, false);

        // Assert: same-owner metadata refresh preserves entry identity and emits one change notice.
        Assert.Same(original, manager.GetResourceRegistration(borrower));
        Assert.Equal(6, borrower.Width);
        Assert.Equal(1, notifications);

        // Act: select the replacement object after the prior framebuffer is no longer selected.
        borrower.RefreshWrappedFramebuffer(second.FboId, 8, 8, false);

        // Assert: switching selection attaches the borrower to the new canonical registration.
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
    #endregion
}
