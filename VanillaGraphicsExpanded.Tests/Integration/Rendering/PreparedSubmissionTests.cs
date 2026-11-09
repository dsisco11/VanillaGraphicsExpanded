using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises direct prepared slots using packaged SPIR-V and owned resource abstractions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PreparedSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Native view image publication keeps selections relative to the view rather than its source layers.</summary>
    [Fact]
    public void NativeViewImageSubmissionUsesViewRelativeSelection()
    {
        // Arrange: Create a texture view with a restricted layer range and prepare image bindings against that view.
        // Act: Bind the valid view-relative layer, then validate a layer beyond the view's range.
        // Assert: Native image queries report the view, mip zero, and layer one; the out-of-range selection is rejected.
        EnsureContextValid();
        using var pipeline = Load();
        var binding = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var nativeView = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 3, LayerCount = 2, Label = null }).RequireResource();
        var image = new RHIImageBinding(nativeView, ImageAccess.WriteOnly, Level: 0, Layered: false, Layer: 1);
        ShaderResourcePreparation.ValidateImage(binding, image);
        using var sampled = RHICommandContext.Current.CreateTexture2D(new() { Dimension=TextureDimension.Two, Width=2, Height=2, MipLevels=1, Format=TextureFormat.Rgba32f, Storage=ResourceStorageMode.Mutable }).RequireResource();
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        RHICommandContext.Current.BindResources(Capture(pipeline, sampled, sampled, image));
        // Query the native assignment to catch accidental source-relative argument translation.
        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 7, out int name);
        GL.GetInteger((GetIndexedPName)All.ImageBindingLevel, 7, out int level);
        GL.GetInteger((GetIndexedPName)All.ImageBindingLayer, 7, out int layer);
        Assert.Equal(nativeView.TextureId, name);
        Assert.Equal(0, level);
        Assert.Equal(1, layer);
        Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(binding, image with { Layer = 2 }));
    }

    /// <summary>Successful compatibility is reused while input changes, executable replacement and retirement remain observable.</summary>
    [Fact]
    public void RetainedValidationSkipsUnchangedInputsAndRefreshesChangedFacts()
    {
        // Arrange: Prepare resources and a compiled binding structure whose compatibility can be retained across uses.
        // Act: Reuse unchanged inputs, resize a texture, replace the executable, and retire the resource.
        // Assert: Preparation counts change only for relevant generations, replacement uses a distinct structure, and retired input is rejected.
        EnsureContextValid();
        using var pipeline = Load();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two,
            Format = TextureFormat.Rgba32f, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        var context = RHICommandContext.Current;
        var bindings = Capture(pipeline, texture, texture, new(texture, ImageAccess.WriteOnly));
        context.SetPipeline(pipeline.Pipeline);
        for (int use = 0; use < 5; use++) context.BindResources(bindings);
#if DEBUG
        Assert.Equal(1, bindings.Structure.Preparations);
#endif
        texture.Resize(4, 4);
        context.BindResources(bindings);
#if DEBUG
        Assert.Equal(2, bindings.Structure.Preparations);
#endif
        var invalid = Capture(pipeline, texture, texture, new(texture, ImageAccess.WriteOnly, Level: 3));
        Assert.Throws<InvalidOperationException>(() => context.BindResources(invalid));
        context.BindResources(bindings);
#if DEBUG
        Assert.Equal(2, bindings.Structure.Preparations);
#endif
        using var replacement = Load();
        context.SetPipeline(replacement.Pipeline);
        var replaced = Capture(replacement, texture, texture, new(texture, ImageAccess.WriteOnly));
        context.BindResources(replaced);
        Assert.NotSame(bindings.Structure, replaced.Structure);
        texture.Dispose();
        Assert.Throws<InvalidOperationException>(() => context.BindResources(replaced));
    }
    /// <summary>Array sampling and image writes use independent fixed slots without authored sampler locations.</summary>
    [Fact]
    public void FixedSlotArraysProduceExpectedOutputAndRestoreAcrossOwners()
    {
        // Arrange: Create two owners with array texture/image inputs mapped to fixed slots and a render target.
        // Act: Dispatch with the desired pair, let another owner overwrite the shared slots, then dispatch again.
        // Assert: Both output readbacks match the expected sum and the first owner's sampled texture is restored.
        EnsureContextValid();
        using var pipeline = Load();
        var prepared = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var outputBinding = prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var first = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 1, 2, 3, 4 }).AsSpan()).ToArray())]).RequireResource();
        using var second = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 10, 20, 30, 40 }).AsSpan()).ToArray())]).RequireResource();
        using var other = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 100, 200, 300, 400 }).AsSpan()).ToArray())]).RequireResource();
        using var output = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var view = new RHIImageBinding(output, ImageAccess.WriteOnly);
        GpuTextureRef[] desired = [first, second];
        // Validate the entire set before publication, as generated Submit does.

        var desiredCapture = Capture(pipeline, first, second, view);
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        RHICommandContext.Current.BindResources(desiredCapture);
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        RHICommandContext.Current.Dispatch(new(1));
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 11, 22, 33, 44 }, output.ReadPixels());

        // Another owner replaces shared units; retained desired values must restore them on reuse.
        RHICommandContext.Current.BindResources(Capture(pipeline, other, other, new(other, ImageAccess.WriteOnly)));
        output.UploadDataImmediate(new float[4]);
        RHICommandContext.Current.BindResources(desiredCapture);
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        RHICommandContext.Current.Dispatch(new(1));
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 11, 22, 33, 44 }, output.ReadPixels());
        Assert.True(OpenGlStateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 7, out int restored));
        Assert.Equal(first.TextureId, restored);
    }

    /// <summary>Missing required elements fail before publication; optional absence clears and inactive inputs do nothing.</summary>
    [Fact]
    public void ValidationFailureAndRetirementCannotPublishStaleResources()
    {
        // Arrange: Prepare required and optional resource slots, including an output that can be retired before submission.
        // Act: Try an incomplete binding set, validate optional absence, then dispose a previously bound resource.
        // Assert: Invalid input does not replace the retained output, inactive absence leaves its slot empty, and disposal withdraws the image binding.
        EnsureContextValid();
        using var pipeline = Load();
        var table = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var outputBinding = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        var inactive = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "unused"));
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var output = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        RHICommandContext.Current.BindResources(Capture(pipeline, texture, texture, new(output, ImageAccess.WriteOnly)));
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(new RHIResourceBindings(pipeline.Shader,
            samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), texture)],
            images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), new RHIImageBinding(output))])));
        Assert.True(OpenGlStateCache.Current.TryGetCachedImageTexture(7, out int retained));
        Assert.Equal(output.TextureId, retained);
        ShaderResourcePreparation.ValidateSampler(inactive, (GpuTexture?)null);
        Assert.False(OpenGlStateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 10, out _));

        // The same disposed wrapper is now invalid even though its desired reference did not change.
        texture.Dispose();
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(Capture(pipeline, texture, texture, new(output))));
        // Active optional absence and unavailable selections are covered through real optional contracts in BindingPreparationPolicyTests.
        output.Dispose();
        Assert.False(OpenGlStateCache.Current.TryGetCachedImageTexture(7, out _));
    }

    /// <summary>Context-wide range and view keys suppress repeated binds and invalidate retired allocations.</summary>
    [Fact]
    public void IndexedBuffersAndImagesRestoreAndInvalidateCompleteAssignments()
    {
        // Arrange: Capture existing indexed buffer/image assignments before binding replacement resources.
        // Act: repeat and replace indexed bindings, retire their resources, and invalidate the entire cache.
        // Assert: Identical assignments add no bind count, changed image access adds one, and retired/invalidated slots are withdrawn.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        using var first = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        using var second = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        first.EnsureCapacity(64, growExponentially: false);
        second.EnsureCapacity(64, growExponentially: false);
        OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
#if DEBUG
        long count = cache.ResourceSlotBindCount;
#endif
        OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
#if DEBUG
        Assert.Equal(count, cache.ResourceSlotBindCount);
#endif
        OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, second.BufferId, 0, 64);
        OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.ShaderStorageBuffer, 0, first.BufferId, 0, 32);
        Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out int restored, out nint offset, out nint size));
        Assert.Equal(first.BufferId, restored);
        Assert.Equal((nint)0, offset);
        Assert.Equal((nint)32, size);
        first.Dispose();
        Assert.False(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out _, out _, out _));
        OpenGLBufferBindings.BindBufferBase(cache, BufferRangeTarget.ShaderStorageBuffer, 0, 0);
        Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out int cleared, out _, out _));
        Assert.Equal(0, cleared);
        using var image = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 7, image.TextureId, 0, false, 0, TextureAccess.WriteOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)image).RequireImage(0).Format));
#if DEBUG
        count = cache.ResourceSlotBindCount;
#endif
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 7, image.TextureId, 0, false, 0, TextureAccess.WriteOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)image).RequireImage(0).Format));
#if DEBUG
        Assert.Equal(count, cache.ResourceSlotBindCount);
#endif
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 7, image.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)image).RequireImage(0).Format));
#if DEBUG
        Assert.Equal(count + 1, cache.ResourceSlotBindCount);
#endif
        cache.InvalidateAll();
        Assert.False(cache.TryGetCachedImageTexture(7, out _));
        Assert.False(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0, out _, out _, out _));
    }

    /// <summary>Storage interpretation and image layer selection are validated before publishing any resource.</summary>
    [Fact]
    public void FormatAndImageViewValidationUsesStorageAndSelectedLayer()
    {
        // Arrange: Prepare image bindings with storage formats and a view exposing a bounded layer selection.
        // Act: Try mismatched image interpretation, invalid mip/layers, and valid/invalid view-relative layer selections.
        // Assert: Invalid assignments throw before publication while valid selected-layer validation succeeds.
        EnsureContextValid();
        using var pipeline = Load();
        var table = pipeline.ProgramLayout.BinaryInterface!.PreparedBindings;
        var inputs = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        var image = table.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"));
        using var integer = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var floating = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        RHICommandContext.Current.SetPipeline(pipeline.Pipeline);
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(Capture(pipeline, floating, integer, new(floating))));
        // Equal texel byte sizes do not make unsigned storage valid for a floating image interpretation.
        Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(image, new RHIImageBinding(integer)));
        Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(image,
            new RHIImageBinding(floating, Level: 1)));
        using var array = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        ShaderResourcePreparation.ValidateImage(image, new RHIImageBinding(array, Layer: 1));
        Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(image,
            new RHIImageBinding(array, Layer: 2)));
        Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateImage(image,
            new RHIImageBinding(array, Layered: true)));
        // Authored selector shape is checked even when the reference is absent.
        Assert.Throws<ArgumentException>(() => new RHIResourceBindings(pipeline.Shader,
            images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), new RHIImageBinding(default, Level: -1))]));
    }
    #endregion

    #region Private
    /// <summary>Captures the complete fixed array and output contract without publishing native slots individually.</summary>
    private static RHIResourceBindings Capture(ComputeShaderAsset pipeline, GpuTextureRef first, GpuTextureRef second, RHIImageBinding output)
        => new(pipeline.Shader, samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), first),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"), second, Element: 1)],
            images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outputImage"), output)]);

    /// <summary>Loads the existing offline array fixture through the production preparation owner.</summary>
    private static ComputeShaderAsset Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "prepared_binding.csh.spv");
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, new ShaderSettings(PreparedBindingComputeShader.Contract), out var pipeline, out string log, fileSystem: ShaderAssetFileSystem.Built), log);
        return pipeline!;
    }
    #endregion
}
