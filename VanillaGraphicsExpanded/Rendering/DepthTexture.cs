using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a two-dimensional depth-only texture and its depth readback behavior.</summary>
public sealed class DepthTexture : DynamicTexture2D
{
    /// <summary>Creates a depth texture with a depth-only internal format.</summary>
    public DepthTexture(int width, int height,
        PixelInternalFormat format = PixelInternalFormat.DepthComponent24,
        string? debugName = null)
        : base(width, height, ValidateFormat(format), TextureFilterMode.Nearest, debugName) { }

    /// <inheritdoc />
    public override void Clear()
    {
        if (!IsValid) throw new InvalidOperationException("Depth texture is invalid.");
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }



    /// <inheritdoc />
    protected override PixelFormat ReadbackFormat => PixelFormat.DepthComponent;



    /// <summary>Rejects formats that do not contain depth-only storage.</summary>
    private static PixelInternalFormat ValidateFormat(PixelInternalFormat format)
    {
        if (!TextureFormatHelper.IsDepthFormat(format) ||
            format is PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8)
            throw new ArgumentException("A depth-only internal format is required.", nameof(format));

        return format;
    }
}
