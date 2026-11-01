using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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
[Trait("Category", "GpuIntegration")]
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
        shader.Output = new(result, ImageAccess.WriteOnly);
        shader.Dispatch(1);
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
        var cache = OpenGlStateCache.Current;
        var prepared = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
#if DEBUG
        using var acquisition = new ProgramAcquisitionCapture();
        int reflection = acquisition.Reads;
        long names = pipeline.ProgramLayout.UniformNameResolutions;
        long interfaceNames = pipeline.ProgramLayout.BinaryInterface.UniformNameResolutions;
#endif
        var captured = shader.CaptureBindings();
#if DEBUG
        long structuralPreparations = captured.Structure.Preparations;
#endif
        Assert.True(RHIUniformRingSystem.TryGetCurrent(out var ring));
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
            shader.Output = new(result, ImageAccess.WriteOnly);
            shader.Dispatch(1);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
        }
        Assert.Same(captured, shader.CaptureBindings());
#if DEBUG
        Assert.Equal(uploads, ring.AllocationsWritten);
        Assert.Equal(bytes, ring.BytesWritten);
        Assert.Equal(structuralPreparations, captured.Structure.Preparations);
        // Compact executed-state comparisons remain; structural preparation and native binds do not repeat.
        Assert.Equal(imageChecks + repeats * 2, cache.ImageCacheChecks);
        Assert.Equal(imageBinds, cache.ResourceSlotBindCount);
        Assert.Equal(reflection, acquisition.Reads);
        Assert.Equal(names, pipeline.ProgramLayout.UniformNameResolutions);
        Assert.Equal(interfaceNames, pipeline.ProgramLayout.BinaryInterface.UniformNameResolutions);
#endif
        RHICommandContext.Current.Barrier(new(ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 7, 5, 0, 0 }, result.ReadPixels());
#if DEBUG
        output.WriteLine($"Generated owner: {repeats * 3} assignment attempts/skips; 0 revision changes; preparation reflection queries {reflection}; steady-state reflection/name resolutions 0; {repeats} submissions with compact slot comparisons and unchanged structural preparation; numeric GL uploads 0; repeated block copies/allocations 0; repeated image or UBO range binds 0.");
#endif

        using var arrays = Load("prepared_binding", PreparedBindingComputeShader.Contract);
        var table = arrays.ProgramLayout.BinaryInterface!.PreparedBindings;
        var input = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var image = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var first = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 1, 2, 3, 4 }).AsSpan()).ToArray())]).RequireResource();
        using var second = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 10, 20, 30, 40 }).AsSpan()).ToArray())]).RequireResource();
        GpuTextureRef[] desired = [first, second];
        var view = new RHIImageBinding(result, ImageAccess.WriteOnly);
        var commands = RHICommandContext.Current;
        var bindings = new RHIResourceBindings(arrays.Shader,
            samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), first),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), second, Element: 1)],
            images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), view)]);
        commands.SetPipeline(arrays.Pipeline); commands.BindResources(bindings);
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
            commands.BindResources(bindings);
        }
#if DEBUG
        Assert.Equal(textures, cache.TextureBindCount);
        Assert.Equal(samplers, cache.SamplerBindCount);
        Assert.Equal(imageBinds, cache.ResourceSlotBindCount);
        Assert.Equal(textureChecks + repeats * 2, cache.TextureCacheChecks);
        // Core sampler batching compares direct SamplerKnowledge; it does not enter scalar-bind instrumentation.
        Assert.Equal(samplerChecks, cache.SamplerCacheChecks);
        Assert.Equal(imageChecks + repeats, cache.ImageCacheChecks);
        Assert.Equal(reflection, acquisition.Reads);
        Assert.Equal(names, arrays.ProgramLayout.UniformNameResolutions);
        Assert.Equal(interfaceNames, arrays.ProgramLayout.BinaryInterface.UniformNameResolutions);
#endif
        RHICommandContext.Current.SetPipeline(arrays.Pipeline);
        RHICommandContext.Current.Dispatch(new(1));
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 11, 22, 33, 44 }, result.ReadPixels());
#if DEBUG
        output.WriteLine($"Managed array: preparation reflection queries {reflection}; {repeats} compact slot publications; unchanged structural preparation; sampler comparisons use the uninstrumented batch path; image comparisons without native binds; steady-state reflection/name resolutions 0; texture/sampler/image binds 0.");
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
    public void BorrowedStorageRetirementAndRepeatedBindingRemainExplicit()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var arrays = Load("prepared_binding", PreparedBindingComputeShader.Contract);
            var context = RHICommandContext.Current;
            using var source = context.CreateTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
                Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var borrowed = new BorrowedTexture(source.TextureId);
            using var result = context.CreateTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
                Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            ulong sampler = GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs");
            var bindings = new RHIResourceBindings(arrays.Shader, samplers: [new(sampler, borrowed), new(sampler, borrowed, 1)],
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), new RHIImageBinding(result, ImageAccess.WriteOnly))]);
            context.SetPipeline(arrays.Pipeline); context.BindResources(bindings);
#if DEBUG
            long binds = OpenGlStateCache.Current.TextureBindCount;
#endif
            for (int use = 0; use < 3; use++) context.BindResources(bindings);
#if DEBUG
            Assert.Equal(binds, OpenGlStateCache.Current.TextureBindCount);
#endif
            source.Dispose();
            Assert.False(borrowed.IsValid);
            Assert.Throws<InvalidOperationException>(() => context.BindResources(bindings));
#if DEBUG
            Assert.Equal(binds, OpenGlStateCache.Current.TextureBindCount);
#endif
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A retained plan restores external bindings, preserves recorded versions and rejects disposed inputs before dispatch.</summary>
    [Fact]
    public void PreparedPlanSurvivesRecordedReuseAndExternalChangesButNotDisposal()
    {
        EnsureContextValid();
        using var pipeline = Load("uniform_state", UniformStateComputeShader.Contract);
        using var shader = new UniformStateComputeShader(pipeline);
        var context = RHICommandContext.Current;
        using var outputTexture = context.CreateTexture2D(new() { Dimension = TextureDimension.Two,
            Format = TextureFormat.Rgba32f, Width = 1, Height = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Mutable }).RequireResource();
        shader.Values = [2, 3]; shader.Scalar = 7;
        shader.Output = new(outputTexture, ImageAccess.WriteOnly);
        var original = shader.CaptureBindings();
        context.SetPipeline(shader.Pipeline);
        context.BindResources(original);
#if DEBUG
        var backend = (OpenGLBackend)typeof(RHICommandContext)
            .GetField("backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        long plans = backend.BindingPlansPrepared;
#endif
        using var list = context.CreateCommandList();
        list.SetPipeline(shader.Pipeline); list.BindResources(original); list.Dispatch(new(1)); list.Close();
        // A later CPU edit cannot change the captured version or its prepared entry selections.
        shader.Scalar = 19;
        Assert.Same(original.Structure, shader.CaptureBindings().Structure);
        GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        context.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 7, 5, 0, 0 }, outputTexture.ReadPixels());
#if DEBUG
        Assert.Equal(plans, backend.BindingPlansPrepared);
#endif
        // Intervening external mutation must also be restored when dispatch does not repeat BindResources.
        GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);
        context.Dispatch(new(1));
        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int image);
        Assert.Equal(outputTexture.TextureId, image);
        context.BindResources(shader.CaptureBindings());
        context.Dispatch(new(1));
        context.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 19, 5, 0, 0 }, outputTexture.ReadPixels());
#if DEBUG
        Assert.Equal(plans, backend.BindingPlansPrepared);
#endif
        outputTexture.Dispose();
        Assert.ThrowsAny<InvalidOperationException>(() => context.Dispatch(new(1)));
        // Failed validation must release all guards and leave the same backend usable.
        using var replacement = context.CreateTexture2D(new() { Dimension = TextureDimension.Two,
            Format = TextureFormat.Rgba32f, Width = 1, Height = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Mutable }).RequireResource();
        shader.Output = new(replacement, ImageAccess.WriteOnly);
        shader.Dispatch(1);
        context.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 19, 5, 0, 0 }, replacement.ReadPixels());
    }
    /// <summary>Relevant changes refresh one retained structure; unrelated work, contents and failed-use retries do not poll it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationRefreshIsLocalAndFailedResultsStayWithdrawn(bool managed)
    {
        EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal(); else fixture.RetireResourceServices();
        try
        {
            using var pipeline = Load("prepared_binding", PreparedBindingComputeShader.Contract);
            var context = RHICommandContext.Current;
            using var texture = managed ? context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba32f, Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource()
                : ContextLocalTexture.Create(4, 4, TextureFormat.Rgba32f);
            ulong samplers = GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs");
            var bindings = new RHIResourceBindings(pipeline.Shader, samplers: [new(samplers, texture), new(samplers, texture, 1)],
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), new RHIImageBinding(texture))]);
            context.SetPipeline(pipeline.Pipeline);
            context.BindResources(bindings);
#if DEBUG
            Assert.Equal(1, bindings.Structure.Preparations);
#endif
            using var unrelated = managed ? context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba32f, Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource()
                : ContextLocalTexture.Create(1, 1, TextureFormat.Rgba32f);
            texture.UploadDataImmediate(new float[64]);
            context.BindResources(bindings);
#if DEBUG
            Assert.Equal(1, bindings.Structure.Preparations);
#endif
            texture.Resize(2, 2);
            context.BindResources(bindings);
#if DEBUG
            Assert.Equal(2, bindings.Structure.Preparations);
#endif
            RHIResourceManager.InvalidateWrapperMetadata(texture);
            Assert.Throws<InvalidOperationException>(() => context.BindResources(bindings));
            Assert.Throws<InvalidOperationException>(() => context.BindResources(bindings));
#if DEBUG
            Assert.Equal(3, bindings.Structure.Preparations);
#endif
            // The owner explicitly supplies a replacement. An execution retry never attempts recovery.
            context.AllocateTextureOwner(texture, new GpuTextureInitialization { Target = TextureDimension.Two,
                InternalFormat = TextureFormat.Rgba32f, Width = 3, Height = 3, Depth = 1, Levels = 1,
                AllocationMode = GpuTextureAllocationMode.Mutable, BaseLevel = 0, MaxLevel = 0 }, TextureFilterMode.Nearest, null, replaceExisting: true);
            context.BindResources(bindings);
#if DEBUG
            Assert.Equal(4, bindings.Structure.Preparations);
#endif
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Loads existing offline executables through the production preparation owner.</summary>
    private static ComputeShaderAsset Load(string name, GpuShaderContract contract)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", name + ".csh.spv");
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, new ShaderSettings(contract), out var pipeline, out string log, fileSystem: ShaderAssetFileSystem.Built), log);
        return pipeline!;
    }
    #endregion
}
