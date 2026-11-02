using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Supplies bounded first-failure values without downloading passing images or retaining subject resources.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Private
    /// <summary>Completes a comparison and evaluates its independent expected-value diagnostic only on failure.</summary>
    private GpuObservationData CompareDomain(GpuImageSnapshot actual, string family, byte[] inputs,
        (string Name, GpuTexture? Texture)[] sources, Func<int, int, uint[], string> expectedAt)
    {
        var data = Observe(actual, family, inputs, sources);
        if (data.Counters[2] == 0 || data.Counters[6] == uint.MaxValue) return data;
        int pixel = checked((int)(data.Counters[6] / 4)), channel = (int)(data.Counters[6] % 4);
        var value = ReadDiagnosticTuple(actual.Texture, pixel, actual.Kind);
        string expected = expectedAt(pixel, channel, value);
        return data with { ExpectedActual = $"actual={FormatTuple(value.Take(actual.Channels), actual.Kind)}, expected={expected}" };
    }

    /// <summary>Reads only the nominated failing texel through an independent native path, preserving integer and IEEE bits.</summary>
    private static uint[] ReadDiagnosticTuple(GpuTexture image, int pixel, int kind)
    {
        image.RequireOriginalContext();
        int x = pixel % image.Width, y = pixel / image.Width % image.Height, layer = pixel / (image.Width * image.Height);
        uint[] bits = new uint[4];
        bool depth = TextureFormatHelper.GetPixelFormat(image.InternalFormat) is PixelChannelLayout.Depth or PixelChannelLayout.DepthStencil;
        var format = depth ? kind == 2 ? PixelFormat.StencilIndex : PixelFormat.DepthComponent
            : kind == 0 ? PixelFormat.Rgba : PixelFormat.RgbaInteger;
        using (OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, 0))
        using (OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(4)))
            GL.GetTextureSubImage(image.TextureId, 0, x, y, layer, 1, 1, 1,
                format,
                kind == 0 ? PixelType.Float : kind == 1 ? PixelType.Int : PixelType.UnsignedInt, 16, bits);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return bits;
    }

    /// <summary>Displays represented values together with exact bits for signed zero, NaN and integer diagnostics.</summary>
    private static string FormatValue(uint bits, int kind) => kind switch
    {
        0 => $"{BitConverter.UInt32BitsToSingle(bits):R}/0x{bits:x8}",
        1 => $"{unchecked((int)bits)}/0x{bits:x8}",
        _ => $"{bits}/0x{bits:x8}"
    };

    /// <summary>Formats a bounded tuple without constructing image-sized failure data.</summary>
    private static string FormatTuple(IEnumerable<uint> bits, int kind) =>
        "(" + string.Join(",", bits.Select(value => FormatValue(value, kind))) + ")";
    #endregion
}
