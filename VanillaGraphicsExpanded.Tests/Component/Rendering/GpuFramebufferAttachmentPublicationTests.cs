using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies owned attachment publication notifies aliases while preserving the origin attachment.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFramebufferAttachmentPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Owned attachment publication invalidates other helpers while retaining the origin's owning image reference.</summary>
    [Fact]
    public void OwnedAttachmentUpdateNotifiesAliasWithoutReplacingOriginImage()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        // Act
        try
        {
            using var first=new GpuFramebufferAttachment(4,4,TextureFormat.Rgba8);
            using var replacement=new GpuFramebufferAttachment(8,4,TextureFormat.Rgba8);
            using var owner=DynamicGpuFramebuffer.Create([first])!;
            using var alias=DynamicGpuFramebuffer.Wrap(owner.FboId);
            alias.PublishRenderPassMetadata();
            ulong revision=alias.AttachmentRevision;
            owner.SetAttachment(FramebufferSlot.Color0,replacement);
            // Assert
            Assert.True(alias.AttachmentRevision>revision);
            Assert.Same(replacement,owner.GetAttachment(FramebufferSlot.Color0));
            Assert.Equal(replacement.TextureId,alias.GetColorTextureId(0));
            Assert.Equal(8,alias.Width);
            alias.Bind(); owner.Bind();
            Assert.Same(replacement,owner.GetAttachment(FramebufferSlot.Color0));
            Assert.True(replacement.IsValid);
            Assert.True(first.IsValid);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}

