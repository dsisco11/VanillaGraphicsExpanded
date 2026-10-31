using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Checks complete GPU image domains and returns a fixed-size result instead of image pixels.</summary>
internal sealed class GpuImageAssertions : IDisposable
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly Dictionary<int, GpuFixtureProgram> programs = [];
    private bool disposed;

    #region Public API
    /// <summary>Retires all executable variants in their original context, without a global native-name cache.</summary>
    public void Dispose()
    {
        if (disposed) return;
        if (RenderContextRegistry.IsAlive(origin)) RequireContext();
        foreach (var program in programs.Values) program.Dispose();
        programs.Clear(); disposed = true;
    }
    #endregion

    #region Internal API
    /// <summary>Compares every selected channel against independent CPU IEEE/integer bit patterns.</summary>
    internal GpuImageResult CompareBits(GpuTexture image, uint[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (expected.Length != checked(count * actual.Channels)) throw new ArgumentException("Expected data must cover the entire selected domain.", nameof(expected));
        uint[] expanded = new uint[checked(count * 4)];
        for (int pixel = 0; pixel < count; pixel++)
            Array.Copy(expected, pixel * actual.Channels, expanded, pixel * 4, actual.Channels);
        using var input = CreateArray(TextureFormat.Rgba32ui, actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth);
        input.UploadDataImmediate(expanded, 0, 0, 0, input.Width, input.Height, input.Depth);
        return Run(actual, 1, input, null, tolerance, double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Compares independent floating expectations without losing signed zero, NaN or subnormal representations.</summary>
    internal GpuImageResult Compare(GpuTexture image, float[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        var bits = Array.ConvertAll(expected, BitConverter.SingleToUInt32Bits);
        return CompareBits(image, bits, tolerance, selection, channelMask);
    }

    /// <summary>Checks all selected texels for finite values, range, positive values and extrema.</summary>
    internal GpuImageResult Inspect(GpuTexture image, double minimum = double.NegativeInfinity,
        double maximum = double.PositiveInfinity, RHITextureRegion? selection = null, uint? channelMask = null)
    {
        RequireContext();
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum) throw new ArgumentException("Invalid assertion range.");
        using var actual = new GpuImageSnapshot(image, selection);
        return Run(actual, 0, null, null, 0, minimum, maximum, channelMask);
    }

    /// <summary>Compares the complete current selection with an explicitly owned previous GPU snapshot.</summary>
    internal GpuImageResult CompareHistory(GpuTexture image, GpuImageSnapshot previous,
        RHITextureRegion? selection = null, double tolerance = 0, uint? channelMask = null)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        previous.Texture.RequireOriginalContext();
        if (actual.Kind != previous.Kind || actual.Channels != previous.Channels
            || actual.Texture.Width != previous.Texture.Width || actual.Texture.Height != previous.Texture.Height || actual.Texture.Depth != previous.Texture.Depth)
            throw new ArgumentException("History must retain the same selected domain and numeric representation.");
        return Run(actual, 2, null, previous.Texture, tolerance, double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Checks every normalized rank in each vertically stacked square slice, including all slice differences.</summary>
    internal GpuImageResult CheckRanks(GpuTexture image, int side, int slices)
    {
        RequireContext();
        if (side <= 0 || slices is < 1 or > 32 || image.Width != side || image.Height != checked(side * slices) || image.Depth != 1)
            throw new ArgumentException("Rank storage must be vertically stacked square slices.");
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || actual.Channels != 1) throw new ArgumentException("Normalized ranks require a scalar floating image.");
        return Run(actual, 3, null, null, 0, 0, 1, 1, side, slices);
    }
    #endregion

    #region Private
    #region Execution
    /// <summary>Dispatches generic predicates, validates completion evidence and synchronously reads only sixteen counters.</summary>
    private GpuImageResult Run(GpuImageSnapshot actual, int mode, Texture3D? expected, Texture3D? history,
        double tolerance, double minimum, double maximum, uint? channelMask, int rankSide = 1, int rankSlices = 1)
    {
        RequireContext();
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        uint mask = channelMask ?? (1u << actual.Channels) - 1;
        if (mask == 0 || (mask & ~((1u << actual.Channels) - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(channelMask));
        // Report an existing error instead of draining it and accepting zero-initialized results as success.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var state = new GpuAssertionState();
        if (!programs.TryGetValue(actual.Kind, out var kernel))
        {
            kernel = new(GpuAssertionKernel.Source(actual.Kind));
            programs.Add(actual.Kind, kernel);
        }
        using var output = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = 16, Height = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Result" }).RequireResource();
        uint[] initial = new uint[16]; initial[0] = 0xdeadbeef; initial[6] = uint.MaxValue; initial[7] = uint.MaxValue; initial[10] = uint.MaxValue;
        output.UploadDataImmediate(initial);
        using var histogram = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = mode == 3 ? checked(rankSide * rankSide) : 1,
            Height = mode == 3 ? rankSlices : 1, MipLevels = 1, Storage = ResourceStorageMode.Immutable,
            Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Histogram" }).RequireResource();
        histogram.UploadDataImmediate(new uint[checked(histogram.Width * histogram.Height)]);
        using var unusedExpected = expected is null ? CreateArray(TextureFormat.Rgba32ui,1,1,1) : null;
        int id = kernel.Id, width = actual.Texture.Width, height = actual.Texture.Height, depth = actual.Texture.Depth;
        GL.ProgramUniform3(id, 0, width, height, depth); GL.ProgramUniform1(id, 1, mode);
        // OpenTK's unsigned scalar overload requires an unsigned program ID; an int ID selects a floating overload.
        GL.ProgramUniform1((uint)id, 2, mask); GL.ProgramUniform1(id, 3, tolerance);
        GL.ProgramUniform2(id, 4, minimum, maximum); GL.ProgramUniform1(id, 5, rankSide); GL.ProgramUniform1(id, 6, rankSlices);
        RequireNativeSuccess("checker uniforms");
        BindSample(0, actual.Texture); BindSample(1, history ?? actual.Texture); BindSample(2, expected ?? unusedExpected!);
        GL.BindImageTexture(0, output.TextureId, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
        GL.BindImageTexture(1, histogram.TextureId, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
        GL.UseProgram(id);
        RequireNativeSuccess("checker bindings");
        int texels = checked(width * height * depth);
        Dispatch(texels);
        RequireNativeSuccess("checker dispatch");
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        if (mode == 3)
        {
            GL.ProgramUniform1(id, 1, 4);
            Dispatch(checked(rankSide * rankSide * rankSlices));
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        }
        RequireContext();
        uint[] counters = new uint[16];
        // The synchronous native read supplies completion; no universal Finish/Flush or SSBO binding is needed.
        using (OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, 0))
        using (OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(4)))
            GL.GetTextureImage(output.TextureId, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, 64, counters);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        if (counters[0] != 0x47505541 || counters[1] != (uint)texels)
            throw new InvalidOperationException($"GPU assertion did not complete: magic={counters[0]:x}, texels={counters[1]}/{texels}.");
        if(mode==3 && (counters[12]!=0x48495354 || counters[11]!=(uint)checked(rankSide*rankSide*rankSlices)))
            throw new InvalidOperationException($"GPU rank validation did not complete: magic={counters[12]:x}, ranks={counters[11]}/{rankSide*rankSide*rankSlices}.");
        return new(counters, width, height, depth, actual.Kind);
    }

    /// <summary>Binds an array snapshot with independent nearest sampling, without touching its original image.</summary>
    private static void BindSample(int unit, GpuTexture image)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit); GL.BindTexture(TextureTarget.Texture2DArray, image.TextureId); GL.BindSampler(unit, 0);
    }

    /// <summary>Covers large original domains within the portable per-axis compute group minimum.</summary>
    private static void Dispatch(int texels)
    {
        int groups=checked((texels+63)/64), x=Math.Min(groups,65535), y=(groups+x-1)/x;
        GL.DispatchCompute(x,y,1);
    }

    /// <summary>Reports the original native failure at its fixture operation boundary without draining errors.</summary>
    private static void RequireNativeSuccess(string operation)
    {
        var error=GL.GetError();
        if(error!=ErrorCode.NoError)throw new InvalidOperationException($"Native failure during {operation}: {error}.");
    }

    #endregion

    #region Resource ownership
    /// <summary>Creates only context-owned fixture storage for expected data.</summary>
    private static Texture3D CreateArray(TextureFormat format, int width, int height, int depth)
    {
        return RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        { Dimension = TextureDimension.TwoArray, Format = format, Width = width, Height = height, Depth = depth,
            MipLevels = 1, Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Expected" }).RequireResource();
    }

    /// <summary>Rejects disposed, missing or foreign context identity before allocating or accessing native objects.</summary>
    private void RequireContext()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (origin.Handle == 0 || !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("The image checker requires its original physical context.");
    }
    #endregion
    #endregion
}
