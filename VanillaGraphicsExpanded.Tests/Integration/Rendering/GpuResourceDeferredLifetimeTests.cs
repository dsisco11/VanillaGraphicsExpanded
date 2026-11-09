using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks original alias identity across owner replacement and deferred cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceDeferredLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A queued owner retires at the lifecycle drain and withdraws non-owning aliases.</summary>
    [Fact]
    public void DeferredAttachmentOwnerWithdrawsAliasesOnDrain()
    {
        // Arrange: queue an attachment owner and an unrelated owner under the same context lifecycle.
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
            // Act: drain the queued owners at the explicit lifecycle boundary.
            queue.DrainPending();

            // Assert: aliases withdraw with their original owner and repeated draining is harmless.
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
