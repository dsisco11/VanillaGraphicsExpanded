using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that shader compatibility history follows canonical metadata availability and revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderInputMetadataRevisionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Equal facts retain compatibility history while explicit storage replacement invalidates it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturePublicationInvalidatesHistory(bool storage)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var previous = ShaderInputValidation.TextureKey(texture);
            var validation = new ShaderInputValidation();
            validation.Commit(default, previous);
            Assert.True(validation.Matches(default, ShaderInputValidation.TextureKey(texture)));
            var manager = texture.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(texture)!;
            manager.PublishMetadata(entry, entry.Metadata, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata);
            var current = ShaderInputValidation.TextureKey(texture);
            Assert.Equal(previous.Width, current.Width);
            Assert.Equal(previous.Format, current.Format);
            if (storage) Assert.NotEqual(previous.Revisions, current.Revisions);
            else Assert.Equal(previous.Revisions, current.Revisions);
            Assert.Equal(!storage, validation.Matches(default, current));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Buffer metadata revisions invalidate history even when the initialized binding range is unchanged.</summary>
    [Fact]
    public void BufferPublicationInvalidatesHistory()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var range = new GpuStorageBufferBinding(buffer, 0, 64);
            var previous = ShaderInputValidation.StorageKey(range);
            var validation = new ShaderInputValidation();
            validation.Commit(default, previous);
            Assert.True(validation.Matches(default, ShaderInputValidation.StorageKey(range)));
            var manager = buffer.ResourceManager!;
            var entry = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            manager.PublishMetadata(entry, entry.Metadata, GpuMetadataChange.Metadata);
            var current = ShaderInputValidation.StorageKey(range);
            Assert.Equal(previous.Capacity, current.Capacity);
            Assert.Equal(previous.Size, current.Size);
            Assert.Equal(previous.Revisions, current.Revisions);
            Assert.True(validation.Matches(default, current));
            manager.PublishMetadata(entry, entry.Metadata with { Usage = BufferUsage.DynamicDraw }, GpuMetadataChange.Metadata);
            Assert.False(validation.Matches(default, ShaderInputValidation.StorageKey(range)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unavailable snapshots cannot consult prior compatibility, and native reacquisition restores usable keys.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableMetadataRejectsHistoryUntilRecovery(bool textureInput)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var range = new GpuStorageBufferBinding(buffer, 0, 64);
            var previous = textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range);
            if (textureInput) RHIResourceManager.InvalidateWrapperMetadata(texture);
            else RHIResourceManager.InvalidateWrapperMetadata(buffer);
            Assert.Throws<InvalidOperationException>(() => textureInput
                ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range));
            if (textureInput)
                RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
            else RHIResourceManager.RecoverWrapperMetadata(buffer, new GpuBufferMetadataReader());
            var current = textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range);
            Assert.NotEqual(previous.Revisions, current.Revisions);
            Assert.Equal(current, textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
