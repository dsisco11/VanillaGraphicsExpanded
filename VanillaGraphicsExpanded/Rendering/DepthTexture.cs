using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a two-dimensional depth-only texture and its depth readback behavior.</summary>
public sealed class DepthTexture : DynamicTexture2D
{
    /// <summary>Creates a depth texture with a depth-only internal format.</summary>
    public DepthTexture(int width, int height,
        TextureFormat format = TextureFormat.DepthComponent24,
        string? debugName = null)
        : base(width, height, ValidateFormat(format), TextureFilterMode.Nearest, debugName) { }


    /// <inheritdoc />
    protected override RHI.RHIPixelEncoding ReadbackEncoding => new(RHI.PixelChannelLayout.Depth, RHI.PixelComponentType.Float);


    /// <summary>Rejects formats that do not contain depth-only storage.</summary>
    private static TextureFormat ValidateFormat(TextureFormat format)
    {
        if (!TextureFormatHelper.IsDepthFormat(format) ||
            format is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8)
            throw new ArgumentException("A depth-only internal format is required.", nameof(format));

        return format;
    }
}
