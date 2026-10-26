using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks direct submission validation against withdrawn snapshots and shrinking native stores.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderPreparedMetadataValidationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Direct images and individual or array samplers reject uncertified metadata before changing bindings.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureInputsRejectUnavailableMetadata(bool managed)
    {
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var sampler = Entry(ShaderBindingKind.Sampler, ActiveUniformType.Sampler2D, [true, true]);
            var image = Entry(ShaderBindingKind.Image, ActiveUniformType.Image2D, [true]);
            OpenGLImageBindings.ActiveTexture(StateCache.Current, 0);
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture2D, 0, sentinel.TextureId);
            ShaderPreparedSubmission.ValidateSampler(sampler, (GpuTexture)texture);
            ShaderPreparedSubmission.ValidateSamplerArray(sampler, [texture, sentinel]);
            ShaderPreparedSubmission.ValidateImage(image, texture);
            if (managed) GpuResourceManager.InvalidateWrapperMetadata(texture);
            else GpuResourceManager.DiscardUntrackedMetadata(texture);
            Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSampler(sampler, (GpuTexture)texture));
            Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSamplerArray(sampler, [sentinel, texture]));
            Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(image, texture));
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(TextureTarget.Texture2D), GpuMetadataChange.Metadata);
            ShaderPreparedSubmission.ValidateSamplerArray(sampler, [texture, sentinel]);
            ShaderPreparedSubmission.ValidateImage(image, texture);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Buffer consumers reject unavailable metadata and ranges exceeding a changed allocation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferInputsRejectUnavailableAndShrunkStorage(bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var storage = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
            storage.Allocate(64);
            var binding = Entry(ShaderBindingKind.StorageBlock, default, [true]) with { MinimumSize = 32 };
            Validate(binding, storage, ranged);
            GpuResourceManager.InvalidateWrapperMetadata(storage);
            Assert.Throws<InvalidOperationException>(() => Validate(binding, storage, ranged));
            GpuResourceManager.ObserveWrapperMetadata(storage, new GpuBufferMetadataReader(), GpuMetadataChange.Metadata);
            Validate(binding, storage, ranged);
            storage.Allocate(16);
            if (ranged) Assert.Throws<InvalidOperationException>(() => Validate(binding, storage, ranged));
            else Validate(binding, storage, ranged);
            storage.Allocate(64);
            Validate(binding, storage, ranged);
            if (ranged) ShaderPreparedSubmission.ValidateStorageBlock(binding, new GpuStorageBufferBinding(storage, 0, 16));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Supplies a prepared contract with explicit activity while exercising actual resource metadata.</summary>
    private static GpuPreparedBindings.Entry Entry(ShaderBindingKind kind, ActiveUniformType type, bool[] elements) =>
        new(new GpuBindingEntry(0, kind, "fixture", new GpuBindingContract.Binding(0, true, elements.Length)), true, type, elements);

    /// <summary>Exercises either the whole-store consumer or its retained explicit initialized range.</summary>
    private static void Validate(GpuPreparedBindings.Entry binding, GpuShaderStorageBuffer buffer, bool ranged)
    {
        if (ranged) ShaderPreparedSubmission.ValidateStorageBlock(binding, new GpuStorageBufferBinding(buffer, 0, 32));
        else ShaderPreparedSubmission.ValidateStorageBlock(binding, buffer);
    }
    #endregion
}
