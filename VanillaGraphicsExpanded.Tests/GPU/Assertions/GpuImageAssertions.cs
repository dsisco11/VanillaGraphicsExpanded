using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Checks complete GPU image domains and returns a fixed-size result instead of image pixels.</summary>
internal sealed partial class GpuImageAssertions : IDisposable
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private bool disposed;
    private GpuImageTransform? projections;

    #region Public API
    /// <summary>Releases per-observation helper state while the fixture retains production executable owners.</summary>
    public void Dispose()
    {
        if (disposed) return;
        if (RenderContextRegistry.IsAlive(origin)) RequireContext();
        projections?.Dispose(); disposed = true;
    }
    #endregion

    #region Private
    #region Execution
    /// <summary>Dispatches complete-domain predicates, validates every reduction stage and reads a bounded result.</summary>
    private GpuImageResult Run(GpuImageSnapshot actual, int mode, Texture3D? expected, Texture3D? history,
        double tolerance, double minimum, double maximum, uint? channelMask, int rankSide = 1, int rankSlices = 1, uint constant = 0,
        System.Numerics.Vector4 multipliers = default, int referenceChannel = 0, int pixelStride = 1, int pixelOffset = 0,
        int statisticExpression = 0, System.Numerics.Vector4 channelMinimum = default, System.Numerics.Vector4 channelMaximum = default,
        Texture3D? additional = null, Texture3D? fourth = null, GpuAssertionPredicate predicate = GpuAssertionPredicate.None, System.Numerics.Vector4? evaluation = null)
    {
        RequireContext();
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        uint mask = channelMask ?? (1u << actual.Channels) - 1;
        if (mask == 0 || (mask & ~((1u << actual.Channels) - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(channelMask));
        if (pixelStride <= 0 || pixelOffset < 0 || pixelOffset >= pixelStride ||
            pixelOffset >= (long)actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth)
            throw new ArgumentOutOfRangeException(nameof(pixelStride));
        // Report an existing error instead of draining it and accepting zero-initialized results as success.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var state = new GpuAssertionState();
        var fixture = Fixtures.HeadlessGLFixture.Current;
        var kernel = mode == 21 ? fixture.PredicateProgram : fixture.AssertionProgram(actual.Kind, statisticExpression);
        using var output = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = 32, Height = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Result" }).RequireResource();
        uint[] initial = new uint[32]; initial[0] = 0xdeadbeef; initial[6] = uint.MaxValue; initial[7] = uint.MaxValue; initial[10] = uint.MaxValue;
        output.UploadDataImmediate(initial);
        using var histogram = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = mode == 3 ? checked(rankSide * rankSide) : mode == 11 ? 256 * 16 : 1,
            Height = mode == 3 ? rankSlices : 1, MipLevels = 1, Storage = ResourceStorageMode.Immutable,
            Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Histogram" }).RequireResource();
        histogram.UploadDataImmediate(new uint[checked(histogram.Width * histogram.Height)]);
        using var unusedExpected = expected is null ? CreateArray(TextureFormat.Rgba32ui,1,1,1) : null;
        int width = actual.Texture.Width, height = actual.Texture.Height, depth = actual.Texture.Depth;
        byte[] bytes = new byte[160];
        GpuAssertionInputs.Integer(bytes, 0, (uint)width); GpuAssertionInputs.Integer(bytes, 4, (uint)height);
        GpuAssertionInputs.Integer(bytes, 8, (uint)depth);
        GpuAssertionInputs.Integer(bytes, 16, mask); GpuAssertionInputs.Integer(bytes, 20, constant);
        GpuAssertionInputs.Integer(bytes, 24, (uint)rankSide); GpuAssertionInputs.Integer(bytes, 28, (uint)rankSlices);
        GpuAssertionInputs.Double(bytes, 32, tolerance); GpuAssertionInputs.Double(bytes, 40, minimum);
        GpuAssertionInputs.Double(bytes, 48, maximum); GpuAssertionInputs.Vector(bytes, 64, multipliers);
        GpuAssertionInputs.Integer(bytes, 80, (uint)referenceChannel); GpuAssertionInputs.Integer(bytes, 84, (uint)pixelStride);
        GpuAssertionInputs.Integer(bytes, 88, (uint)pixelOffset); GpuAssertionInputs.Integer(bytes, 92, (uint)predicate);
        for (int channel = 0; channel < 4; channel++)
        {
            GpuAssertionInputs.Integer(bytes, 96 + channel * 4, BitConverter.SingleToUInt32Bits(channelMinimum[channel]));
            GpuAssertionInputs.Integer(bytes, 112 + channel * 4, BitConverter.SingleToUInt32Bits(channelMaximum[channel]));
        }
        GpuAssertionInputs.Vector(bytes, 128, evaluation ?? new(1, 1, 1, 0));
        GpuTexture[] sources = [actual.Texture, history ?? actual.Texture, expected ?? unusedExpected!, additional ?? actual.Texture, fourth ?? actual.Texture];
        GpuTexture[] destinations = [output, histogram];
        int texels = checked(width * height * depth);
        // Every pass stages a new immutable block version; physical upload and dispatch use the production path.
        /// <summary>Publishes the selected operation before one complete-domain dispatch.</summary>
        void Submit(int operation, int groupsX, int groupsY = 1)
        {
            GpuAssertionInputs.Integer(bytes, 12, (uint)operation);
            kernel.Execute(bytes, sources, destinations, groupsX, groupsY);
        }
        int groups = checked((texels + 63) / 64), x = Math.Min(groups, 65535);
        Submit(mode == 11 ? 0 : mode, x, (groups + x - 1) / x);
        RequireNativeSuccess("checker dispatch");
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate));
        if (mode == 3)
        {
            int rankGroups = checked((rankSide * rankSide * rankSlices + 63) / 64);
            int rankX = Math.Min(rankGroups, 65535);
            Submit(4, rankX, (rankGroups + rankX - 1) / rankX);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate));
        }
        if (mode == 11)
        {
            Submit(11, 256);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
            Submit(12, 1);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate));
        }
        RequireContext();
        uint[] counters = new uint[32];
        // The synchronous native read supplies completion; no universal Finish/Flush or SSBO binding is needed.
        using (OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, 0))
        using (OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(4)))
            GL.GetTextureImage(output.TextureId, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, 128, counters);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        if (counters[0] != 0x47505541 || counters[1] != (uint)texels)
            throw new InvalidOperationException($"GPU assertion did not complete: magic={counters[0]:x}, texels={counters[1]}/{texels}.");
        if(mode==3 && (counters[12]!=0x48495354 || counters[11]!=(uint)checked(rankSide*rankSide*rankSlices)))
            throw new InvalidOperationException($"GPU rank validation did not complete: magic={counters[12]:x}, ranks={counters[11]}/{rankSide*rankSide*rankSlices}.");
        if (mode == 11 && (counters[11] != 256 || counters[12] != 0x53554d53 || counters[14] != 1))
            throw new InvalidOperationException($"GPU statistics did not complete: partials={counters[11]}/256, magic={counters[12]:x}, final={counters[14]}/1.");
        return new(counters, width, height, depth, actual.Kind,mode);
    }

    /// <summary>Reports the original native failure at its fixture operation boundary without draining errors.</summary>
    private static void RequireNativeSuccess(string operation)
    {
        var error=GL.GetError();
        if(error!=ErrorCode.NoError)throw new InvalidOperationException($"Native failure during {operation}: {error}.");
    }

    #endregion

    #region Resource ownership
    /// <summary>Rejects mismatched domains before a relational oracle reads corresponding GPU texels.</summary>
    private static void RequireMatchingFloating(GpuImageSnapshot first, GpuImageSnapshot second)
    {
        if (first.Kind != 0 || second.Kind != 0 || first.Texture.Width != second.Texture.Width ||
            first.Texture.Height != second.Texture.Height || first.Texture.Depth != second.Texture.Depth || first.Channels != second.Channels)
            throw new ArgumentException("Relational observations require matching floating domains and channels.");
    }

    /// <summary>Packs authored double bands into separate low/high integer layers without rounding to floating texture storage.</summary>
    private static Texture3D CreateDoubleInput(GpuImageSnapshot actual, IReadOnlyList<double[]> bands)
    {
        int width = actual.Texture.Width, height = actual.Texture.Height, depth = actual.Texture.Depth;
        int plane = checked(width * height), count = checked(plane * depth);
        uint[] bits = new uint[checked(count * 8 * bands.Count)];
        for (int band = 0; band < bands.Count; band++)
        for (int pixel = 0; pixel < count; pixel++)
        for (int channel = 0; channel < actual.Channels; channel++)
        {
            ulong value = BitConverter.DoubleToUInt64Bits(bands[band][pixel * actual.Channels + channel]);
            int index = ((pixel / plane * bands.Count * 2 + band * 2) * plane + pixel % plane) * 4 + channel;
            bits[index] = (uint)value; bits[index + plane * 4] = (uint)(value >> 32);
        }
        var input = CreateArray(TextureFormat.Rgba32ui, width, height, checked(depth * 2 * bands.Count));
        try { input.UploadDataImmediate(bits, 0, 0, 0, width, height, input.Depth); return input; }
        catch { input.Dispose(); throw; }
    }

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
