using System;
using System.Runtime.InteropServices;
using ErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies native image transfers, selector restoration and compiler-resource context boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageNativeTransferTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    #region Native tuple reads

    /// <summary>A native float tuple preserves explicit layer selection and IEEE payload bits.</summary>
    [Fact]
    public void LocalFloatTuplePreservesLayerAndPayload()
    {
        // Arrange: create two array layers with distinct values and unusual IEEE payloads in the selected layer.
        EnsureContextValid();
        using var image = Create(TextureDimension.TwoArray, TextureFormat.Rgba32f, 2, 1, 2);
        float[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, float.NaN, float.Epsilon, -0f, float.PositiveInfinity];

        // Act: upload the authored layers, then read the selected texel through the native tuple path.
        RHICommandContext.Current.UploadTexture(image, new(new(0), 0, 0, 0, 2, 1, 2),
            RHICommandContext.Current.GetTextureEncoding(image, PixelComponentType.Float), MemoryMarshal.AsBytes(values.AsSpan()));
        var observed = GpuLocalRead.Tuple(image, 1, 0, 1);

        // Assert: compare bit patterns so NaN payload class, subnormal, signed zero, and infinity survive.
        Assert.Equal(Array.ConvertAll(values[12..], BitConverter.SingleToUInt32Bits),
            Array.ConvertAll(observed, BitConverter.SingleToUInt32Bits));
    }

    /// <summary>A native UNORM8 tuple preserves authored bytes from the first and selected final array layers.</summary>
    [Fact]
    public void LocalRgba8TuplePreservesOriginalBytes()
    {
        // Arrange: create a two-layer byte texture whose layers have different authored tuples.
        EnsureContextValid();
        using var image = Create(TextureDimension.TwoArray, TextureFormat.Rgba8, 2, 1, 2);
        byte[] values = [0, 1, 127, 255, 17, 33, 65, 129, 255, 128, 127, 0, 1, 127, 128, 254];

        // Act: upload the complete image and retrieve one tuple from each selected layer.
        RHICommandContext.Current.UploadTexture(image, new(new(0), 0, 0, 0, 2, 1, 2),
            RHICommandContext.Current.GetTextureEncoding(image, PixelComponentType.UnsignedByte), values);
        var firstLayer = GpuLocalRead.Rgba8(image, 0, 0);
        var finalLayer = GpuLocalRead.Rgba8(image, 1, 0, 1);

        // Assert: both reads contain their exact four authored bytes.
        Assert.Equal(values[..4], firstLayer);
        Assert.Equal(values[12..], finalLayer);
    }

    #endregion

    #region Checker dispatch and selector preservation

    /// <summary>The production numeric checker preserves whole-buffer and ranged caller uniform selectors.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProductionCheckerPreservesUniformSelectors(bool range)
    {
        // Arrange: bind distinct generic and indexed uniform buffers so checker cleanup is observable.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Upload(image, [1f], PixelComponentType.Float);
        int selected = GL.GenBuffer(), generic = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.UniformBuffer, selected);
            GL.BufferData(BufferTarget.UniformBuffer, 256, IntPtr.Zero, BufferUsageHint.StaticDraw);
            if (range) GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 28, selected, IntPtr.Zero, 16);
            else GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 28, selected);
            GL.BindBuffer(BufferTarget.UniformBuffer, generic);
            var check = ImageAssertions;
            // Act: execute the production comparison while the indexed caller binding is selected.
            check.Comparisons.CompareConstant(image, 1).AssertEqual();
            // Assert: native selectors and the managed state cache retain the caller's binding.
            Assert.Equal(generic, GL.GetInteger(GetPName.UniformBufferBinding));
            GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, 28, out int restored);
            Assert.Equal(selected, restored);
            var cache = OpenGlStateCache.Current;
            Assert.True(cache.TryGetCachedIndexedBuffer(OpenTK.Graphics.OpenGL.BufferRangeTarget.UniformBuffer,
                28, out int cached, out nint offset, out nint size));
            Assert.Equal(selected, cached); Assert.Equal((nint)0, offset);
            Assert.Equal(range ? (nint)16 : 0, size);
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 28, 0);
            GL.BindBuffer(BufferTarget.UniformBuffer, 0);
            GL.DeleteBuffer(selected); GL.DeleteBuffer(generic);
        }
    }

    /// <summary>Malformed kernel construction retires the failed allocation without publishing an executable.</summary>
    [Fact]
    public void MalformedKernelRetiresPartialConstruction()
    {
        // Arrange: retain whether this test created the resource service so cleanup follows its original ownership.
        EnsureContextValid();
        bool createdService = RHIResourceManagerSystem.CurrentManager is null;
        var service = RHIResourceManagerSystem.EnsureCurrentContext();
        try
        {
            // Act: fail construction after shader allocation, then submit a malformed source and valid source.
            int partial = 0;
            Assert.Throws<IOException>(() => new NativeComputeProgram("unused", shader =>
            {
                partial = shader;
                throw new IOException("injected construction fault");
            }));
            Assert.NotEqual(0, partial); Assert.False(GL.IsShader(partial));
            Assert.Throws<InvalidOperationException>(() => new NativeComputeProgram("#version 450\nnot a shader"));
            using var valid = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
            // Assert: failed objects are retired while an independently compiled valid program stays usable.
            Assert.True(GL.IsProgram(valid.Id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (createdService) { RHIResourceManagerSystem.Shutdown(); service.Dispose(); } }
    }

    /// <summary>Comparison dispatch preserves hostile native program, texture, buffer, image and pack selectors.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostileSelectorsRemainUnchanged(bool failedDispatch)
    {
        // Arrange: install hostile texture/buffer selectors and record native state before execution.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(image, new float[] { 1, 2 }, PixelComponentType.Float);
        using var arraySentinel = Create(TextureDimension.TwoArray, TextureFormat.R32f, 1, 1);
        using var sentinel = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        int buffer = GL.GenBuffer();
        try
        {
            GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment, out int alignment);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer); GL.BufferData(BufferTarget.ShaderStorageBuffer, alignment * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, buffer);
            GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, buffer, (IntPtr)alignment, alignment);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, 0); GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, buffer); GL.PixelStore(PixelStoreParameter.PackRowLength, 7); GL.PixelStore(PixelStoreParameter.PackSkipPixels, 1);
            GL.UseProgram(sentinel.Id); GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, arraySentinel.TextureId); GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2DArray, arraySentinel.TextureId);
            GL.ActiveTexture(TextureUnit.Texture4); GL.BindTexture(TextureTarget.Texture2DArray, arraySentinel.TextureId); GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindImageTexture(0, image.TextureId, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.R32f);
            int sampler = GL.GenSampler(); GL.BindSampler(0, sampler); GL.BindSampler(3, sampler); GL.BindSampler(4, sampler);
            try
            {
                using var native = failedDispatch ? new NativeDriverCapture() : null;
                if (native is not null) { native.Add("glDispatchCompute", new Dispatch((_, _, _) => { })); native.Install(); }
                var assertions = ImageAssertions;
                // Act: execute or suppress comparison dispatch under the hostile selector state.
                if (failedDispatch) Assert.Throws<InvalidOperationException>(() => assertions.Comparisons.Compare(image, new float[] { 1, 2 }));
                else assertions.Comparisons.Compare(image, new float[] { 1, 2 }).AssertEqual();
                // Assert: generic/indexed bindings, image and sampler slots and pack layout remain installed.
                Assert.Equal(sentinel.Id, GL.GetInteger(GetPName.CurrentProgram)); Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
                Assert.Equal(buffer, GL.GetInteger(GetPName.ShaderStorageBufferBinding)); Assert.Equal(buffer, GL.GetInteger(GetPName.PixelPackBufferBinding));
                Assert.Equal(7, GL.GetInteger(GetPName.PackRowLength)); Assert.Equal(1, GL.GetInteger(GetPName.PackSkipPixels));
                GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 0, out int first); Assert.Equal(buffer, first);
                GL.GetInteger64(GetIndexedPName.ShaderStorageBufferStart, 1, out long offset); Assert.Equal(alignment, offset);
                GL.GetInteger64(GetIndexedPName.ShaderStorageBufferSize, 1, out long size); Assert.Equal(alignment, size);
                GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 2, out int empty); Assert.Equal(0, empty);
                GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int imageBinding); Assert.Equal(image.TextureId, imageBinding);
                GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 0, out int access); Assert.Equal((int)TextureAccess.ReadOnly, access);
                GL.GetInteger((GetIndexedPName)All.SamplerBinding, 0, out int samplerBinding); Assert.Equal(sampler, samplerBinding);
                GL.ActiveTexture(TextureUnit.Texture0); Assert.Equal(arraySentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray)); GL.ActiveTexture(TextureUnit.Texture5);
                GL.ActiveTexture(TextureUnit.Texture3); Assert.Equal(arraySentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray)); GL.ActiveTexture(TextureUnit.Texture5);
                GL.GetInteger((GetIndexedPName)All.SamplerBinding, 3, out int fourthSampler); Assert.Equal(sampler, fourthSampler);
                GL.ActiveTexture(TextureUnit.Texture4); Assert.Equal(arraySentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray)); GL.ActiveTexture(TextureUnit.Texture5);
                GL.GetInteger((GetIndexedPName)All.SamplerBinding, 4, out int fifthSampler); Assert.Equal(sampler, fifthSampler);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally
            {
                GL.BindSampler(0, 0); GL.BindSampler(3, 0); GL.BindSampler(4, 0); GL.DeleteSampler(sampler);
                GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.R32f);
            }
        }
        finally
        {
            GL.UseProgram(0); GL.ActiveTexture(TextureUnit.Texture4); GL.BindTexture(TextureTarget.Texture2DArray, 0);
            GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2DArray, 0);
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, 0);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0); GL.PixelStore(PixelStoreParameter.PackRowLength, 0); GL.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, 0); GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, 0); GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, 0);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0); GL.DeleteBuffer(buffer);
        }
    }

    #endregion

    #region Context ownership and native retirement

    /// <summary>A foreign context cannot use or retire a live checker owner; returning to origin restores use.</summary>
    [Fact]
    public unsafe void ForeignContextCannotAccessOrRetireAnOwner()
    {
        // Arrange: create a live checker owner in the fixture context and a second physical context.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1); Upload(image, new float[] { 1 }, PixelComponentType.Float);
        var assertions = ImageAssertions; assertions.Statistics.Inspect(image).AssertFiniteRange();
        GLFW.DefaultWindowHints(); GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4); GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        Window* window = GLFW.CreateWindow(16, 16, "Assertion context qualification", null, null);
        Assert.True(window != null); object owner = new();
        try
        {
            // Act: attempt owner access/disposal while the foreign context and registry identity are current.
            GLFW.MakeContextCurrent(window); RenderContextRegistry.RegisterCurrent(owner, static _ => true);
            int foreign = GL.CreateProgram();
            try
            {
                Assert.Throws<InvalidOperationException>(() => assertions.Statistics.Inspect(image));
                Assert.Throws<InvalidOperationException>(() => fixture.Observations);
                Assert.Throws<InvalidOperationException>(assertions.Programs.Dispose);
                // Assert: foreign program remains intact, then origin checker use succeeds after context return.
                Assert.True(GL.IsProgram(foreign));
            }
            finally { GL.DeleteProgram(foreign); }
        }
        finally { RenderContextRegistry.Retire(owner); GLFW.DestroyWindow(window); fixture.MakeCurrent(); }
        assertions.Statistics.Inspect(image).AssertFiniteRange();
    }

    /// <summary>Interrupted construction retires its partial shader on the origin service, preserving foreign names.</summary>
    [Fact]
    public unsafe void InterruptedConstructionRetiresOnReturnToOrigin()
    {
        // Arrange: create separate origin and foreign contexts and retain one foreign-owned shader.
        EnsureContextValid();
        bool createdService = RHIResourceManagerSystem.CurrentManager is null;
        var service = RHIResourceManagerSystem.EnsureCurrentContext();
        GLFW.DefaultWindowHints(); GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4); GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        Window* window = GLFW.CreateWindow(16, 16, "Partial construction qualification", null, null);
        Assert.True(window != null); object foreignOwner = new(); int foreignShader = 0, partial = 0;
        try
        {
            // Act: interrupt construction after its callback switches to the foreign native context.
            GLFW.MakeContextCurrent(window); RenderContextRegistry.RegisterCurrent(foreignOwner, static _ => true);
            foreignShader = GL.CreateShader(ShaderType.ComputeShader); fixture.MakeCurrent();
            var expected = new IOException("construction interrupted");
            Assert.Same(expected, Assert.Throws<IOException>(() => new NativeComputeProgram("unused", shader =>
            {
                partial = shader; GLFW.MakeContextCurrent(window); throw expected;
            })));
            Assert.True(GL.IsShader(foreignShader));
            fixture.MakeCurrent(); Assert.True(GL.IsShader(partial));
            service.OnRenderFrame(0, RHIResourceManager.Stage); Assert.False(GL.IsShader(partial));
            GLFW.MakeContextCurrent(window);
            // Assert: origin cleanup deletes only the partial shader, leaving the foreign shader valid.
            Assert.True(GL.IsShader(foreignShader));
        }
        finally
        {
            GLFW.MakeContextCurrent(window); if (foreignShader != 0) GL.DeleteShader(foreignShader);
            RenderContextRegistry.Retire(foreignOwner); GLFW.DestroyWindow(window); fixture.MakeCurrent();
            if (createdService) { RHIResourceManagerSystem.Shutdown(); service.Dispose(); }
        }
    }

    /// <summary>Retiring a real native namespace cannot delete recycled program names in the surviving context.</summary>
    [Fact]
    public unsafe void RetiredContextDropsNativeNamesWithoutForeignDeletion()
    {
        // Arrange: create and register a program in a disposable context before returning to the fixture context.
        EnsureContextValid();
        GLFW.DefaultWindowHints(); GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4); GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        Window* window = GLFW.CreateWindow(16, 16, "Assertion retirement qualification", null, null);
        Assert.True(window != null); object owner = new(); NativeComputeProgram? retired = null;
        try
        {
            GLFW.MakeContextCurrent(window); RenderContextRegistry.RegisterCurrent(owner, static _ => true);
            retired = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        }
        finally { RenderContextRegistry.Retire(owner); GLFW.DestroyWindow(window); fixture.MakeCurrent(); }
        using var surviving = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        // Act: dispose the retired owner after a new context has reused the native namespace.
        retired!.Dispose();
        // Assert: native-name retirement does not destroy the surviving context program.
        Assert.True(GL.IsProgram(surviving.Id)); retired.Dispose();
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Creates an immutable native image subject with the original assertion suite's dimension rules.</summary>
    private static GpuTexture Create(TextureDimension dimension, TextureFormat format, int width, int height,
        int depth = 1, int levels = 1)
    {
        return RHICommandContext.Current.CreateTexture(new RHITextureDescription
        {
            Dimension = dimension,
            Format = format,
            Width = width,
            Height = height,
            Depth = dimension == TextureDimension.Cube ? 1 : depth,
            MipLevels = levels,
            Storage = ResourceStorageMode.Immutable,
            Filter = TextureFilterMode.Nearest
        }).RequireResource();
    }

    /// <summary>Uploads authored float data through the production transfer APIs without deriving expectations from readback.</summary>
    private static void Upload(GpuTexture image, float[] values, PixelComponentType type, int layer = 0, int mip = 0,
        int? width = null, int? height = null)
    {
        var encoding = RHICommandContext.Current.GetTextureEncoding(image, type);
        RHICommandContext.Current.UploadTexture(image,
            new(new(mip, layer), 0, 0, 0, width ?? image.Width, height ?? image.Height),
            encoding, MemoryMarshal.AsBytes(values.AsSpan()));
    }

    /// <summary>Matches the native compute dispatch ABI used to suppress completion in the selector scenario.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Dispatch(uint x, uint y, uint z);

    /// <summary>Prepares only the comparison and inspection programs used by native interoperability cases.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("compare", 1, 0);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("inspection", 1, 0);
        programs.Prepare("inspection", 2, 0);
    }

    #endregion
}
