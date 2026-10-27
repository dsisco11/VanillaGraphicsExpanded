using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures retained submission work separately from preparation and actual driver resource binds.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderBindingOperationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Identical generated assignments and fixed-slot publication retain semantic output without redundant binds or uploads.</summary>
    [Fact]
    public void SteadyStateCountsSeparateAssignmentsChecksAndDriverWork()
    {
        EnsureContextValid();
        using var pipeline = Load("uniform_state", UniformStateComputeShader.Contract);
        using var shader = new UniformStateComputeShader(pipeline);
        using var result = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        shader.Values = [2, 3];
        shader.Scalar = 7;
        shader.Output = new(result, TextureAccess.WriteOnly);
        shader.Dispatch(1);
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
        var cache = StateCache.Current;
        var prepared = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
#if DEBUG
        using var acquisition = new ProgramAcquisitionCapture();
        int reflection = acquisition.Reads;
        long names = pipeline.ProgramLayout.UniformNameResolutions;
        long interfaceNames = pipeline.ProgramLayout.BinaryInterface.UniformNameResolutions;
#endif
        ulong revision = Revision(shader);
        Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
#if DEBUG
        long uploads = ring.AllocationsWritten, bytes = ring.BytesWritten;
        long imageChecks = cache.ImageCacheChecks;
        long imageBinds = cache.ResourceSlotBindCount;
#endif
        const int repeats = 8;
        // Measure only publication. Readback and resource creation deliberately stay outside the interval.
        for (int use = 0; use < repeats; use++)
        {
            shader.Scalar = 7;
            shader.Values = [2, 3];
            shader.Output = new(result, TextureAccess.WriteOnly);
            shader.Dispatch(1);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
        }
        Assert.Equal(revision, Revision(shader));
#if DEBUG
        Assert.Equal(uploads, ring.AllocationsWritten);
        Assert.Equal(bytes, ring.BytesWritten);
        Assert.Equal(imageChecks + repeats, cache.ImageCacheChecks);
        Assert.Equal(imageBinds, cache.ResourceSlotBindCount);
        Assert.Equal(reflection, acquisition.Reads);
        Assert.Equal(names, pipeline.ProgramLayout.UniformNameResolutions);
        Assert.Equal(interfaceNames, pipeline.ProgramLayout.BinaryInterface.UniformNameResolutions);
#endif
        var validation = History<ShaderInputValidation>(shader, "Output");
#if DEBUG
        Assert.Equal(1, validation.EntryResolutions);
        Assert.Equal(1, validation.CompatibilityChecks);
#endif
        RHICommandContext.Current.Barrier(new(ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 7, 5, 0, 0 }, result.ReadPixels());
#if DEBUG
        output.WriteLine($"Generated owner: {repeats * 3} assignment attempts/skips; 0 revision changes; preparation reflection queries {reflection}; steady-state reflection/name resolutions 0; {repeats} submissions/image cache checks; entry resolutions 1; compatibility checks 1; numeric GL uploads 0; repeated block copies/allocations 0; repeated image or UBO range binds 0.");
#endif

        using var arrays = Load("prepared_binding", PreparedBindingComputeShader.Contract);
        var table = arrays.ProgramLayout.BinaryInterface!.PreparedBindings;
        var input = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var image = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var first = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 1, 2, 3, 4 }).AsSpan()).ToArray())]).RequireResource();
        using var second = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 10, 20, 30, 40 }).AsSpan()).ToArray())]).RequireResource();
        GpuTextureRef[] desired = [first, second];
        var view = new GpuTextureBinding(result, TextureAccess.WriteOnly);
        ShaderPreparedSubmission.SamplerArray(input, desired);
        ShaderPreparedSubmission.Image(image, view);
#if DEBUG
        long textures = cache.TextureBindCount, samplers = cache.SamplerBindCount;
        long textureChecks = cache.TextureCacheChecks, samplerChecks = cache.SamplerCacheChecks;
        imageChecks = cache.ImageCacheChecks;
        imageBinds = cache.ResourceSlotBindCount;
        reflection = acquisition.Reads;
        names = arrays.ProgramLayout.UniformNameResolutions;
        interfaceNames = arrays.ProgramLayout.BinaryInterface.UniformNameResolutions;
#endif
        for (int use = 0; use < repeats; use++)
        {
            ShaderPreparedSubmission.ValidateSamplerArray(input, desired);
            ShaderPreparedSubmission.ValidateImage(image, view);
            ShaderPreparedSubmission.SamplerArray(input, desired);
            ShaderPreparedSubmission.Image(image, view);
        }
#if DEBUG
        Assert.Equal(textures, cache.TextureBindCount);
        Assert.Equal(samplers, cache.SamplerBindCount);
        Assert.Equal(imageBinds, cache.ResourceSlotBindCount);
        Assert.Equal(textureChecks + repeats * 2, cache.TextureCacheChecks);
        Assert.Equal(samplerChecks + repeats * 2, cache.SamplerCacheChecks);
        Assert.Equal(imageChecks + repeats, cache.ImageCacheChecks);
        Assert.Equal(reflection, acquisition.Reads);
        Assert.Equal(names, arrays.ProgramLayout.UniformNameResolutions);
        Assert.Equal(interfaceNames, arrays.ProgramLayout.BinaryInterface.UniformNameResolutions);
#endif
        arrays.Dispatch(1);
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 11, 22, 33, 44 }, result.ReadPixels());
#if DEBUG
        output.WriteLine($"Managed array: preparation reflection queries {reflection}; {repeats} full-set validations/publications; {repeats * 2} texture cache checks; {repeats * 2} sampler cache checks; {repeats} image cache checks; steady-state reflection/name resolutions 0; texture/sampler/image binds 0.");
#endif

        using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        buffer.EnsureCapacity(64, growExponentially: false);
        OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, buffer.BufferId, 0, 32);
#if DEBUG
        long bufferChecks = cache.IndexedBufferCacheChecks;
        long bufferBinds = cache.ResourceSlotBindCount;
#endif
        for (int use = 0; use < repeats; use++)
            OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, buffer.BufferId, 0, 32);
#if DEBUG
        Assert.Equal(bufferChecks + repeats, cache.IndexedBufferCacheChecks);
        Assert.Equal(bufferBinds, cache.ResourceSlotBindCount);
#endif
        Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out int retained, out nint offset, out nint size));
        Assert.Equal(buffer.BufferId, retained);
        Assert.Equal((nint)0, offset);
        Assert.Equal((nint)32, size);
#if DEBUG
        output.WriteLine($"Indexed buffer range: {repeats} cache checks; repeated buffer binds 0; retained allocation, offset and size verified.");
#endif
    }

    /// <summary>Borrowed objects share normal binding suppression and reject observed retirement without adopting a reused name.</summary>
    [Fact]
    public void RawEngineIdRetirementAndRepeatedBindingRemainExplicit()
    {
        EnsureContextValid();
        using var pipeline = Load("prepared_binding", PreparedBindingComputeShader.Contract);
        var input = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        // The descriptor fixture has no wrapper target policy; model the explicit engine-ID contract.
        input = input with { Contract = input.Contract with { Binding = input.Contract.Binding with { TextureTarget = (int)TextureTarget.Texture2D } } };
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        int borrowedId = texture.TextureId;
        var cache = StateCache.Current;
#if DEBUG
        long binds = cache.TextureBindCount;
#endif
        for (int use = 0; use < 3; use++)
        {
            ShaderPreparedSubmission.ValidateSampler(input, borrowedId);
            ShaderPreparedSubmission.Sampler(input, borrowedId);
        }
#if DEBUG
        Assert.Equal(binds + 3, cache.TextureBindCount);
#endif
        texture.Dispose();
#if DEBUG
        binds = cache.TextureBindCount;
#endif
        Assert.Throws<InvalidOperationException>(() => ShaderPreparedSubmission.ValidateSampler(input, borrowedId));
#if DEBUG
        Assert.Equal(binds, cache.TextureBindCount);
        output.WriteLine("Raw engine ID: 3 lifetime validations, 3 texture binds despite identical ID; retired retained ID rejected before publication. These binds are intentional ownership-boundary work.");
#endif
    }
    #endregion

    #region Private
    /// <summary>Loads existing offline executables through the production preparation owner.</summary>
    private static GpuComputePipeline Load(string name, GpuShaderContract contract)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", name + ".csh.spv");
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, new ShaderSettings(contract), out var pipeline, out string log), log);
        return pipeline!;
    }
    /// <summary>Reads the generated change revision without adding production assignment bookkeeping.</summary>
    private static ulong Revision(UniformStateComputeShader shader) => (ulong)typeof(UniformStateComputeShader)
        .GetField("__inputRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
    /// <summary>Observes production per-input counters without a second tracking implementation.</summary>
    private static T History<T>(UniformStateComputeShader shader, string property) => (T)typeof(UniformStateComputeShader)
        .GetField("__validation_" + property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
    #endregion
}
