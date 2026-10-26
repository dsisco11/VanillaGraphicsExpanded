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
    /// <summary>Equal native descriptors still invalidate successful compatibility history after publication.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturePublicationInvalidatesHistory(bool storage)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
            var previous = ShaderInputValidation.TextureKey(texture);
            var validation = new ShaderInputValidation();
            validation.Commit(default, previous);
            Assert.True(validation.Matches(default, ShaderInputValidation.TextureKey(texture)));
            GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget),
                storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata);
            var current = ShaderInputValidation.TextureKey(texture);
            Assert.Equal(previous.Width, current.Width);
            Assert.Equal(previous.Format, current.Format);
            Assert.NotEqual(previous.Revisions, current.Revisions);
            Assert.False(validation.Matches(default, current));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Buffer metadata revisions invalidate history even when the initialized binding range is unchanged.</summary>
    [Fact]
    public void BufferPublicationInvalidatesHistory()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var range = new GpuStorageBufferBinding(buffer, 0, 64);
            var previous = ShaderInputValidation.StorageKey(range);
            var validation = new ShaderInputValidation();
            validation.Commit(default, previous);
            Assert.True(validation.Matches(default, ShaderInputValidation.StorageKey(range)));
            GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Metadata);
            var current = ShaderInputValidation.StorageKey(range);
            Assert.Equal(previous.Capacity, current.Capacity);
            Assert.Equal(previous.Size, current.Size);
            Assert.NotEqual(previous.Revisions, current.Revisions);
            Assert.False(validation.Matches(default, current));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
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
            using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var range = new GpuStorageBufferBinding(buffer, 0, 64);
            var previous = textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range);
            if (textureInput) GpuResourceManager.InvalidateWrapperMetadata(texture);
            else GpuResourceManager.InvalidateWrapperMetadata(buffer);
            Assert.Throws<InvalidOperationException>(() => textureInput
                ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range));
            if (textureInput)
                GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget), GpuMetadataChange.Metadata);
            else GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Metadata);
            var current = textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range);
            Assert.NotEqual(previous.Revisions, current.Revisions);
            Assert.Equal(current, textureInput ? ShaderInputValidation.TextureKey(texture) : ShaderInputValidation.StorageKey(range));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
