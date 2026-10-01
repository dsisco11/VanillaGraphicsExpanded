using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns packed depth-stencil storage and exposes each aspect for readback.</summary>
public sealed class DepthStencilTexture : DynamicTexture2D
{
    /// <summary>Creates a packed depth-stencil texture.</summary>
    public DepthStencilTexture(int width, int height,
        PixelInternalFormat format = PixelInternalFormat.Depth24Stencil8,
        string? debugName = null)
        : base(width, height, ValidateFormat(format), TextureFilterMode.Nearest, debugName) { }

    /// <inheritdoc />
    public override void Clear()
    {
        if (!IsValid) throw new InvalidOperationException("Depth-stencil texture is invalid.");
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    /// <summary>Reads the stencil aspect as unsigned bytes.</summary>
    public byte[] ReadStencilPixels()
    {
        if (!IsValid) throw new InvalidOperationException("Depth-stencil texture is invalid.");

        byte[] pixels = new byte[checked(Width * Height)];
        using var framebuffer = GpuFramebuffer.CreateEmpty("VGE_DepthStencilTexture_StencilReadback_FBO");
        framebuffer.Attach(this);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.GetInteger(GetPName.PackAlignment, out int previousPackAlignment);
        try
        {
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, Width, Height, PixelFormat.StencilIndex, PixelType.UnsignedByte, pixels);
        }
        finally
        {
            GL.PixelStore(PixelStoreParameter.PackAlignment, previousPackAlignment);
        }
        GpuFramebuffer.Unbind();
        return pixels;
    }

    /// <inheritdoc />
    protected override void AttachForReadback(GpuFramebuffer framebuffer, int mipLevel)
    {
        framebuffer.Attach(this, mipLevel);
        GL.DrawBuffer(DrawBufferMode.None);
    }

    /// <inheritdoc />
    protected override PixelFormat ReadbackFormat => PixelFormat.DepthComponent;

    /// <inheritdoc />
    protected override ReadBufferMode ReadbackBuffer => ReadBufferMode.None;

    /// <summary>Rejects formats without packed depth-stencil storage.</summary>
    private static PixelInternalFormat ValidateFormat(PixelInternalFormat format)
    {
        if (format is not (PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8))
            throw new ArgumentException("A packed depth-stencil internal format is required.", nameof(format));

        return format;
    }
}
