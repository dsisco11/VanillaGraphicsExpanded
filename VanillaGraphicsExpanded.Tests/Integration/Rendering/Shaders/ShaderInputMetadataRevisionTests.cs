using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;


namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Verifies unavailable GPU input metadata withdraws shader preparation until native resource metadata recovery.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderInputMetadataRevisionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Withdrawal and explicit recovery both reach dependent preparation; notifications never recover facts themselves.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableMetadataWithdrawsPreparationUntilRecovery(bool textureInput)
    {
        // Arrange: Prepare a shader against available input metadata, then make the selected buffer or texture metadata unavailable.
        // Act: Request preparation before and after metadata recovery.
        // Assert: Missing inputs withdraw preparation until recovered metadata is published.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = ShaderMetadataRevisionFixture.CreateTexture();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = 64, Usage = BufferUsage.StaticDraw }).RequireResource();
            GpuResource resource = textureInput ? texture : buffer;
            var entry = resource.ResourceManager!.GetResourceRegistration(resource)!;
            ulong revision = entry.MetadataRevision;
            int changes = 0;
            var consumer = new object();
            resource.ObservePreparation(consumer, () => changes++);
            // Action/check: Withdraw metadata, demand the missing fact, recover it explicitly and verify each dependent notification.
            if (textureInput) RHIResourceManager.InvalidateWrapperMetadata(texture);
            else RHIResourceManager.InvalidateWrapperMetadata(buffer);
            Assert.Equal(1, changes);
            Assert.Throws<InvalidOperationException>(() => textureInput ? (object)((GpuTextureRef)texture).RequireMetadata()
                : RHIResourceManager.RequireBufferReferenceMetadata(buffer));
            if (textureInput) RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
            else RHIResourceManager.RecoverWrapperMetadata(buffer, new GpuBufferMetadataReader());
            Assert.Equal(2, changes);
            Assert.NotEqual(revision, entry.MetadataRevision);
            Assert.True(resource.ResourceManager.IsResourceMetadataAvailable(resource));
            GC.KeepAlive(consumer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
