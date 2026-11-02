using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Executes synchronous prepared kernels and verifies bounded result transport, without assertion policy.</summary>
internal sealed class GpuObservationDispatch
{
    #region Internal API
    /// <summary>Counts successfully allocated result/scratch images, excluding subjects, snapshots and references.</summary>
    internal long AllocatedImages { get; private set; }

    /// <summary>Executes a complete image domain with no unused histogram or placeholder image allocations.</summary>
    internal GpuObservationData Observe(GpuObservationImage actual, GpuAssertionProgram program, byte[] inputs,
        (string Name, GpuTexture? Texture)[] sources)
        => Observe(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth, actual.Kind,
            GpuObservationLimits.RequireDomain(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth), program, inputs, sources);

    /// <summary>Executes nominated records while retaining the original source extent for failure coordinates.</summary>
    internal GpuObservationData Observe(int width, int height, int depth, int kind, int records,
        GpuAssertionProgram program, byte[] inputs, (string Name, GpuTexture? Texture)[] sources)
    {
        GpuObservationLimits.RequireDomain(width, height, depth);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var state = new GpuAssertionState(program.SamplerUnits, program.ImageUnits);
        using var output = CreateResult();
        Initialize(output);
        int groups = GpuObservationLimits.Groups(records), x = Math.Min(groups, 65535);
        program.Execute(inputs, sources, [("resultImage", output)], x, (groups + x - 1) / x);
        program.RequireContext();
        Barrier();
        program.RequireContext();
        return Read(width, height, depth, kind, records, output);
    }

    /// <summary>Creates one explicitly sized integer result or operation-owned reduction scratch image.</summary>
    internal DynamicTexture2D CreateResult(int width = 32, int height = 1)
    {
        var image = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = width, Height = height,
            MipLevels = 1, Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            Label = "Tests.Observation.Result"
        }).RequireResource();
        AllocatedImages++;
        return image;
    }

    /// <summary>Resets every result field, sentinel and earliest-location marker before dispatch.</summary>
    internal static void Initialize(DynamicTexture2D output)
    {
        uint[] initial = new uint[32];
        initial[0] = 0xdeadbeef; initial[6] = uint.MaxValue; initial[7] = uint.MaxValue; initial[10] = uint.MaxValue;
        output.UploadDataImmediate(initial);
    }

    /// <summary>Publishes explicitly produced image writes for the next reduction or synchronous native read.</summary>
    internal static void Barrier() => RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate));

    /// <summary>Reads bounded counters independently through native GL and requires complete-domain dispatch proof.</summary>
    internal static GpuObservationData Read(GpuObservationImage actual, DynamicTexture2D output)
        => Read(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth, actual.Kind,
            GpuObservationLimits.RequireDomain(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth), output);

    /// <summary>Requires every nominated invocation independently of the original image extent.</summary>
    private static GpuObservationData Read(int width, int height, int depth, int kind, int count, DynamicTexture2D output)
    {
        uint[] counters = new uint[32];
        using (OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, 0))
        using (OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(4)))
            GL.GetTextureImage(output.TextureId, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, 128, counters);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        if (counters[0] != 0x47505541 || counters[1] != (uint)count)
            throw new InvalidOperationException($"GPU observation did not complete: magic={counters[0]:x}, texels={counters[1]}/{count}.");
        return new(counters, width, height, depth, kind);
    }

    #endregion
}
