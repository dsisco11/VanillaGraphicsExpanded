using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;
using VanillaGraphicsExpanded.Collections;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Fixed-size 3D texture / array texture wrapper.
/// Inherits streamed upload defaults from <see cref="GpuTexture"/>.
/// </summary>
public sealed class Texture3D : GpuTexture
{
    internal Texture3D() { }



    #region Readback
    /// <summary>Synchronously reads a color region into pooled storage. The caller must dispose the returned owner.</summary>
    public PooledArray<float> ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight, int layer)
    {
        if (!((GpuResource)this).IsValid) throw new ObjectDisposedException(nameof(Texture3D));
        if ((uint)layer >= (uint)depth) throw new ArgumentOutOfRangeException(nameof(layer));
        if (x < 0 || y < 0 || regionWidth <= 0 || regionHeight <= 0 ||
            (long)x + regionWidth > width || (long)y + regionHeight > height)
            throw new ArgumentOutOfRangeException(nameof(regionWidth), "Readback region must lie within the texture.");
        PixelFormat format = TextureFormatHelper.GetPixelFormat(internalFormat);
        if (format is PixelFormat.RedInteger or PixelFormat.RgInteger or PixelFormat.RgbInteger or PixelFormat.RgbaInteger
            or PixelFormat.DepthComponent or PixelFormat.DepthStencil)
            throw new NotSupportedException("Floating-point color readback requires a noninteger color texture.");

        int count = checked(regionWidth * regionHeight * GetChannelCount());
        int byteCount = checked((int)((long)count << 2));
        var pixels = PooledArray<float>.Rent(count);
        try
        {
            using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
                new(new(0, layer), x, y, 0, regionWidth, regionHeight, 1), OpenGLPixelValues.Encoding(format, PixelType.Float)));
            if (request.Wait() != RHICompletionStatus.Ready)
                throw new InvalidOperationException("Layered texture readback failed.", request.Error);
            using var mapped = request.MapReadyReadback();
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapped.Bytes).CopyTo(pixels.Span);
            return pixels;
        }
        catch
        {
            pixels.Dispose();
            throw;
        }
    }
    #endregion
}
