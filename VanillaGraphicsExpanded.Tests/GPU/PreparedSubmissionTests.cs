using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises direct prepared slots using packaged SPIR-V and owned resource abstractions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PreparedSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Successful compatibility is reused while input changes, executable replacement and retirement remain observable.</summary>
    [Fact]
    public void RetainedValidationSkipsUnchangedInputsAndRefreshesChangedFacts()
    {
        EnsureContextValid();
        using var pipeline = Load();
        var owner = new SubmissionTarget(pipeline);
        var history = new ShaderInputValidation();
        ulong identity = GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage");
        var binding = history.Resolve(owner, identity);
        using var texture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        var view = new GpuTextureBinding(texture, TextureAccess.WriteOnly);
        for (int use = 0; use < 5; use++)
        {
            binding = history.Resolve(owner, identity);
            ShaderPreparedSubmission.ValidateImage(binding, view, ref history);
            ShaderPreparedSubmission.Image(binding, view);
        }
        Assert.Equal(1, history.EntryResolutions);
        Assert.Equal(1, history.CompatibilityChecks);
        texture.Resize(4, 4);
        ShaderPreparedSubmission.ValidateImage(binding, view, ref history);
        Assert.Equal(2, history.CompatibilityChecks);
        var invalid = view with { Level = 3 };
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(binding, invalid, ref history));
        Assert.Equal(2, history.CompatibilityChecks);
        ShaderPreparedSubmission.ValidateImage(binding, view, ref history);
        Assert.Equal(2, history.CompatibilityChecks);
        using var replacement = Load();
        binding = history.Resolve(new SubmissionTarget(replacement), identity);
        ShaderPreparedSubmission.ValidateImage(binding, view, ref history);
        Assert.Equal(2, history.EntryResolutions);
        Assert.Equal(3, history.CompatibilityChecks);
        texture.Dispose();
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(binding, view, ref history));
        Assert.Equal(3, history.CompatibilityChecks);
    }
    /// <summary>Array sampling and image writes use independent fixed slots without authored sampler locations.</summary>
    [Fact]
    public void FixedSlotArraysProduceExpectedOutputAndRestoreAcrossOwners()
    {
        EnsureContextValid();
        using var pipeline = Load();
        var prepared = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var outputBinding = prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var first = Texture2D.CreateWithDataImmediate(1, 1, PixelInternalFormat.Rgba32f, [1, 2, 3, 4]);
        using var second = Texture2D.CreateWithDataImmediate(1, 1, PixelInternalFormat.Rgba32f, [10, 20, 30, 40]);
        using var other = Texture2D.CreateWithDataImmediate(1, 1, PixelInternalFormat.Rgba32f, [100, 200, 300, 400]);
        using var output = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        var view = new GpuTextureBinding(output, TextureAccess.WriteOnly);
        GpuTexture?[] desired = [first, second];
        // Validate the entire set before publication, as generated Submit does.
        ShaderPreparedSubmission.ValidateSamplerArray(inputs, desired);
        ShaderPreparedSubmission.ValidateImage(outputBinding, view);
        ShaderPreparedSubmission.SamplerArray(inputs, desired);
        ShaderBindingAccess.Image(pipeline.ProgramLayout, pipeline.ProgramId, "outputImage", view);
        ShaderBindingAccess.Sampler(pipeline.ProgramLayout, pipeline.ProgramId, "unused", null!);
        pipeline.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);
        Assert.Equal(new float[] { 11, 22, 33, 44 }, output.ReadPixels());

        // Another owner replaces shared units; retained desired values must restore them on reuse.
        ShaderPreparedSubmission.SamplerArray(inputs, [other, other]);
        ShaderPreparedSubmission.Image(outputBinding, new GpuTextureBinding(other, TextureAccess.WriteOnly));
        output.UploadDataImmediate(new float[4]);
        ShaderPreparedSubmission.SamplerArray(inputs, desired);
        ShaderBindingAccess.Image(pipeline.ProgramLayout, pipeline.ProgramId, "outputImage", output);
        pipeline.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);
        Assert.Equal(new float[] { 11, 22, 33, 44 }, output.ReadPixels());
        Assert.True(GlStateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 7, out int restored));
        Assert.Equal(first.TextureId, restored);
    }

    /// <summary>Missing required elements fail before publication; optional absence clears and inactive inputs do nothing.</summary>
    [Fact]
    public void ValidationFailureAndRetirementCannotPublishStaleResources()
    {
        EnsureContextValid();
        using var pipeline = Load();
        var table = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var outputBinding = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        var inactive = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "unused"));
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var output = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        ShaderPreparedSubmission.SamplerArray(inputs, [texture, texture]);
        ShaderPreparedSubmission.Image(outputBinding, output);
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSamplerArray(inputs, [texture]));
        Assert.True(GlStateCache.Current.TryGetCachedImageTexture(7, out int retained));
        Assert.Equal(output.TextureId, retained);
        ShaderPreparedSubmission.ValidateSampler(inactive, (GpuTexture?)null);
        ShaderPreparedSubmission.Sampler(inactive, (GpuTexture?)null);
        Assert.False(GlStateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 10, out _));

        // The same disposed wrapper is now invalid even though its desired reference did not change.
        texture.Dispose();
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSamplerArray(inputs, [texture, texture]));
        var optional = inputs with { Contract = inputs.Contract with { Binding = inputs.Contract.Binding with { Required = false } } };
        ShaderPreparedSubmission.ValidateSamplerArray(optional, [texture, texture]);
        ShaderPreparedSubmission.SamplerArray(optional, [texture, texture]);
        Assert.True(GlStateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 7, out int cleared));
        Assert.Equal(0, cleared);
        output.Dispose();
        Assert.False(GlStateCache.Current.TryGetCachedImageTexture(7, out _));
        var optionalImage = outputBinding with { Contract = outputBinding.Contract with { Binding = outputBinding.Contract.Binding with { Required = false } } };
        ShaderPreparedSubmission.Image(optionalImage, output);
        Assert.True(GlStateCache.Current.TryGetCachedImageTexture(7, out int clearedImage));
        Assert.Equal(0, clearedImage);
    }

    /// <summary>Context-wide range and view keys suppress repeated binds and invalidate retired allocations.</summary>
    [Fact]
    public void IndexedBuffersAndImagesRestoreAndInvalidateCompleteAssignments()
    {
        EnsureContextValid();
        var cache = GlStateCache.Current;
        cache.InvalidateAll();
        using var first = GpuShaderStorageBuffer.Create();
        using var second = GpuShaderStorageBuffer.Create();
        first.EnsureCapacity(64, growExponentially: false);
        second.EnsureCapacity(64, growExponentially: false);
        cache.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
        long count = cache.ResourceSlotBindCount;
        cache.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
        Assert.Equal(count, cache.ResourceSlotBindCount);
        cache.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, second.BufferId, 0, 64);
        cache.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
        Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out int restored, out nint offset, out nint size));
        Assert.Equal(first.BufferId, restored);
        Assert.Equal((nint)0, offset);
        Assert.Equal((nint)32, size);
        first.Dispose();
        Assert.False(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out _, out _, out _));
        cache.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, 0);
        Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out int cleared, out _, out _));
        Assert.Equal(0, cleared);
        using var image = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        image.BindImageUnit(7, TextureAccess.WriteOnly);
        count = cache.ResourceSlotBindCount;
        image.BindImageUnit(7, TextureAccess.WriteOnly);
        Assert.Equal(count, cache.ResourceSlotBindCount);
        image.BindImageUnit(7, TextureAccess.ReadOnly);
        Assert.Equal(count + 1, cache.ResourceSlotBindCount);
        cache.InvalidateAll();
        Assert.False(cache.TryGetCachedImageTexture(7, out _));
        Assert.False(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out _, out _, out _));
    }

    /// <summary>Storage interpretation and image layer selection are validated before publishing any resource.</summary>
    [Fact]
    public void FormatAndImageViewValidationUsesStorageAndSelectedLayer()
    {
        EnsureContextValid();
        using var pipeline = Load();
        var table = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var image = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var integer = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32ui);
        using var floating = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSamplerArray(inputs, [floating, integer]));
        // Equal texel byte sizes do not make unsigned storage valid for a floating image interpretation.
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(image, integer));
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(image,
            new GpuTextureBinding(floating, Level: 1)));
        using var array = Texture3D.Create(1, 1, 2, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture2DArray);
        ShaderPreparedSubmission.ValidateImage(image, new GpuTextureBinding(array, Layer: 1));
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(image,
            new GpuTextureBinding(array, Layer: 2)));
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateImage(image,
            new GpuTextureBinding(array, Layered: true)));
        var optional = image with { Contract = image.Contract with { Binding = image.Contract.Binding with { Required = false } } };
        ShaderPreparedSubmission.Image(optional, new GpuTextureBinding(null!, (TextureAccess)(-1), -1, true, -1, (SizedInternalFormat)(-1)));
        Assert.True(GlStateCache.Current.TryGetCachedImageTexture(7, out int cleared));
        Assert.Equal(0, cleared);
    }
    #endregion

    #region Private
    /// <summary>Exposes a fixture-owned prepared executable through the production submission boundary.</summary>
    private sealed record SubmissionTarget(GpuComputePipeline Pipeline) : IShaderSubmissionTarget
    {
        public int ProgramId => Pipeline.ProgramId;
        public GpuProgramLayout ProgramLayout => Pipeline.ProgramLayout;
    }
    /// <summary>Loads the existing offline array fixture through the production preparation owner.</summary>
    private static GpuComputePipeline Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "prepared_binding.csh.spv");
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, new ShaderSettings(PreparedBindingComputeShader.Contract), out var pipeline, out string log), log);
        return pipeline!;
    }
    #endregion
}
