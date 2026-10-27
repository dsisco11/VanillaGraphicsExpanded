using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a two-dimensional stencil-only texture with integer readback.</summary>
public sealed class StencilTexture : DynamicTexture2D
{
    /// <summary>Allocates stencil-only storage.</summary>
    public StencilTexture(int width, int height, string? debugName = null)
        : base(width, height, TextureFormatHelper.StencilIndex8, TextureFilterMode.Nearest, debugName) { }



    /// <summary>Reads stencil indices as unsigned bytes.</summary>
    public byte[] ReadStencilPixels()
    {
        if (!IsValid) throw new InvalidOperationException("Stencil texture is invalid.");
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), 0, 0, 0, Width, Height, 1), new(PixelChannelLayout.Stencil, PixelComponentType.UnsignedByte)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Stencil readback failed.", request.Error);
        using var mapping = request.MapReadyReadback();
        return mapping.Bytes.ToArray();
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
