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
        if (managed) fixture.InitializeResourceDisposal(); else fixture.UseContextLocalResources();
        try
        {
            using var texture = !managed ? ContextLocalTexture.Create(4, 4, TextureFormat.Rgba32f) : RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var sentinel = !managed ? ContextLocalTexture.Create(4, 4, TextureFormat.Rgba32f) : RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var context = RHICommandContext.Current;
            using var shader = CreateTextureShader();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            context.SetPipeline(pipeline);
            /// <summary>Captures actual array and image inputs for the fixed fixture interface.</summary>
            RHIResourceBindings Capture(GpuTextureRef first, GpuTextureRef second, GpuTextureRef imageTexture) => new(shader,
                samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), first),
                    new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), second, 1)],
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), new RHIImageBinding(imageTexture))]);
            var sampler = Entry(ShaderBindingKind.Sampler, ShaderValueType.Sampler2D, [true, true]);
            var image = Entry(ShaderBindingKind.Image, ShaderValueType.Image2D, [true]);
            OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, 0);
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 0, sentinel.TextureId);
            ShaderResourcePreparation.ValidateSampler(sampler, (GpuTexture)texture);
            context.BindResources(Capture(texture, sentinel, texture));
            ShaderResourcePreparation.ValidateImage(image, new RHIImageBinding(texture));
            RHIResourceManager.InvalidateWrapperMetadata(texture);
            Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateSampler(sampler, (GpuTexture)texture));
            Assert.Throws<InvalidOperationException>(() => context.BindResources(Capture(sentinel, texture, sentinel)));
            Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(image, new RHIImageBinding(texture)));
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            if (managed) RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two));
            else Assert.Throws<InvalidOperationException>(() => RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two)));
            // A local owner with withdrawn facts is replaced from supplied inputs, never rediscovered.
            using var replacement = managed ? null : RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = 4, Height = 4, MipLevels = 1,
                Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
            var available = replacement ?? texture;
            context.BindResources(Capture(available, sentinel, available));
            ShaderResourcePreparation.ValidateImage(image, new RHIImageBinding(available));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
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
            using var storage = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            storage.Allocate(64);
            var context = RHICommandContext.Current;
            var contract = new GpuBindingContract();
            contract.RegisterShaderStorageBlockBinding("Work", 0);
            using var shader = context.CreateShader(new(ShaderKind.Compute, [new(ShaderStage.Compute,
                "#version 450\nlayout(local_size_x=1) in; layout(std430,binding=0) buffer Work { vec4 values[2]; }; void main(){ values[0]=vec4(1); values[1]=vec4(2); }")], contract)).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            context.SetPipeline(pipeline);
            ulong identity = GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "Work");
            Assert.Equal(32, shader.Interface.PreparedBindings.Resolve(identity).MinimumSize);
            var inputs = new RHIResourceBindings(shader, buffers: [new(identity, BufferRole.ShaderStorage, storage,
                Length: ranged ? 32 : 0, Whole: !ranged)]);
            context.BindResources(inputs);
            RHIResourceManager.InvalidateWrapperMetadata(storage);
            Assert.Throws<InvalidOperationException>(() => context.BindResources(inputs));
            RHIResourceManager.RecoverWrapperMetadata(storage, new GpuBufferMetadataReader());
            context.BindResources(inputs);
            storage.Allocate(16);
            Assert.Throws<InvalidOperationException>(() => context.BindResources(inputs));
            storage.Allocate(64);
            context.BindResources(inputs);
            if (ranged) Assert.Throws<InvalidOperationException>(() => context.BindResources(new RHIResourceBindings(shader,
                buffers: [new(identity, BufferRole.ShaderStorage, storage, Length: 16, Whole: false)])));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Supplies a prepared contract with explicit activity while exercising actual resource metadata.</summary>
    private static GpuPreparedBindings.Entry Entry(ShaderBindingKind kind, ShaderValueType type, bool[] elements) =>
        new(new GpuBindingEntry(0, kind, "fixture", new GpuBindingContract.Binding(0, true, elements.Length)), true, type, elements);

    /// <summary>Creates a real fixed array/image contract directly from supplied source without fixture disk IO.</summary>
    private static RHIShader CreateTextureShader()
    {
        var contract = new GpuBindingContract();
        contract.Samplers.Add("inputs", new(7, true, 2));
        contract.RegisterImageUnit("outputImage", 7);
        return RHICommandContext.Current.CreateShader(new(ShaderKind.Compute, [new(ShaderStage.Compute,
            "#version 450\nlayout(local_size_x=1) in; layout(binding=7) uniform sampler2D inputs[2]; layout(rgba32f,binding=7) writeonly uniform image2D outputImage; void main(){ imageStore(outputImage,ivec2(0),texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)); }")], contract)).RequireResource();
    }
    #endregion
}
