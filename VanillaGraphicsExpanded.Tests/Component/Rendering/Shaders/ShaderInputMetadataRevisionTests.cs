using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;


namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Verifies shader input metadata publication invalidates dependent preparation only when resource facts change.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderInputMetadataRevisionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Equal facts preserve preparation while explicit storage replacement withdraws it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturePublicationInvalidatesPreparation(bool storage)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        // Arrange a registered texture with one preparation observer and snapshot its current facts/revision.
        try
        {
            using var texture = ShaderMetadataRevisionFixture.CreateTexture();
            int changes = 0;
            var consumer = new object();
            texture.ObservePreparation(consumer, () => changes++);
            var manager = texture.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(texture)!;
            var previous = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            // Act by publishing identical texture facts under the selected metadata or storage classification.
            manager.PublishMetadata(entry, entry.Metadata, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata);
            // Assert facts stay identical while only storage classification advances revision and notifies preparation.
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
        // Arrange a registered vertex buffer and observe preparation invalidation for that resource.
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = 64, Usage = BufferUsage.StaticDraw }).RequireResource();
            int changes = 0;
            var consumer = new object();
            buffer.ObservePreparation(consumer, () => changes++);
            var manager = buffer.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            // Act by publishing equal buffer facts, then changing usage while leaving capacity unchanged.
            manager.PublishMetadata(entry, entry.Metadata, GpuMetadataChange.Metadata);
            Assert.Equal(0, changes);
            manager.PublishMetadata(entry, entry.Metadata with { Usage = BufferUsage.DynamicDraw }, GpuMetadataChange.Metadata);
            // Assert only the meaningful usage change notifies the observer and capacity remains 64 bytes.
            Assert.Equal(1, changes);
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            GC.KeepAlive(consumer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
}
