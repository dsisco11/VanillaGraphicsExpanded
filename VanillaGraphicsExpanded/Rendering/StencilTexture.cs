using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a two-dimensional stencil-only texture with integer readback.</summary>
public sealed class StencilTexture : DynamicTexture2D
{
    /// <summary>Allocates stencil-only storage.</summary>
    public StencilTexture(int width, int height, string? debugName = null)
        : base(width, height, TextureFormatHelper.StencilIndex8, TextureFilterMode.Nearest, debugName) { }

    /// <inheritdoc />
    public override void Clear()
    {
        if (!IsValid) throw new InvalidOperationException("Stencil texture is invalid.");
        GL.Clear(ClearBufferMask.StencilBufferBit);
    }

    /// <summary>Reads stencil indices as unsigned bytes.</summary>
    public byte[] ReadStencilPixels()
    {
        if (!IsValid) throw new InvalidOperationException("Stencil texture is invalid.");

        byte[] pixels = new byte[checked(Width * Height)];
        using var framebuffer = GpuFramebuffer.CreateEmpty("VGE_StencilTexture_Readback_FBO");
        framebuffer.Attach(this);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        // Borrow a tightly packed layout and restore every incoming pack field through the cache.
        using (StateCache.Current.SetPixelPackScope(new StateCache.PixelPackState(Alignment: 1)))
        {
            GL.ReadPixels(0, 0, Width, Height, PixelFormat.StencilIndex, PixelType.UnsignedByte, pixels);
        }
        GpuFramebuffer.Unbind();
        return pixels;
    }

    /// <inheritdoc />
    public override float[] ReadPixels() => throw new NotSupportedException("Use ReadStencilPixels for integer stencil data.");

    /// <inheritdoc />
    public override float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight) =>
        throw new NotSupportedException("Use ReadStencilPixels for integer stencil data.");

    /// <inheritdoc />
    public override float[] ReadPixels(int mipLevel) =>
        throw new NotSupportedException("Use ReadStencilPixels for integer stencil data.");
}
