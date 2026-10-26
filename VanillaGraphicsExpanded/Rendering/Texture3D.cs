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
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty("VGE_Texture3D_Readback");
            // Preserve separate read/draw bindings, including when attachment or mapping fails.
            using var draw = StateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer, framebuffer.FboId);
            using var read = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
            framebuffer.Attach(this, layer);
            if (!framebuffer.CheckStatus(out string? error)) throw new InvalidOperationException(error);
            // CheckStatus releases its diagnostic binding in debug builds.
            framebuffer.Bind();
            using var staging = RHICommandContext.Current.CreatePixelPackBuffer(new RHIBufferDescription { Role = RHIBufferRole.PixelPack, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StreamRead, Label = "VGE_Texture3D_Readback_Pixels" }).RequireResource();
            staging.AllocateOrphan(byteCount);
            staging.ReadPixels(x, y, regionWidth, regionHeight, format, PixelType.Float);
            using var mapped = staging.MapRange<float>(0, count, RHIBufferMapAccess.Read);
            if (!mapped.IsMapped) throw new InvalidOperationException("Texture readback mapping failed.");
            mapped.Span.CopyTo(pixels.Span);
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
