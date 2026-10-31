using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks change-delivered preparation invalidation against canonical availability and revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderInputMetadataRevisionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Equal facts preserve preparation while explicit storage replacement withdraws it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturePublicationInvalidatesPreparation(bool storage)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = CreateTexture();
            int changes = 0;
            var consumer = new object();
            texture.ObservePreparation(consumer, () => changes++);
            var manager = texture.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(texture)!;
            var previous = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            manager.PublishMetadata(entry, entry.Metadata, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata);
            Assert.Equal(previous, ((GpuTextureRef)texture).RequireMetadata());
            Assert.Equal(storage ? 1 : 0, changes);
            Assert.Equal(storage, revision != entry.MetadataRevision);
            GC.KeepAlive(consumer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Equal buffer facts do not visit preparation, while a meaningful metadata change does.</summary>
    [Fact]
    public void BufferPublicationInvalidatesPreparation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = 64, Usage = BufferUsage.StaticDraw }).RequireResource();
            int changes = 0;
            var consumer = new object();
            buffer.ObservePreparation(consumer, () => changes++);
            var manager = buffer.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            manager.PublishMetadata(entry, entry.Metadata, GpuMetadataChange.Metadata);
            Assert.Equal(0, changes);
            manager.PublishMetadata(entry, entry.Metadata with { Usage = BufferUsage.DynamicDraw }, GpuMetadataChange.Metadata);
            Assert.Equal(1, changes);
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            GC.KeepAlive(consumer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Withdrawal and explicit recovery both reach dependent preparation; notifications never recover facts themselves.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableMetadataWithdrawsPreparationUntilRecovery(bool textureInput)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = CreateTexture();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = 64, Usage = BufferUsage.StaticDraw }).RequireResource();
            GpuResource resource = textureInput ? texture : buffer;
            var entry = resource.ResourceManager!.GetResourceRegistration(resource)!;
            ulong revision = entry.MetadataRevision;
            int changes = 0;
            var consumer = new object();
            resource.ObservePreparation(consumer, () => changes++);
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

    #region Private
    /// <summary>Creates a small mutable allocation for structural notification checks.</summary>
    private static DynamicTexture2D CreateTexture() => RHICommandContext.Current.CreateDynamicTexture2D(new() {
        Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
        MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
    #endregion
}
