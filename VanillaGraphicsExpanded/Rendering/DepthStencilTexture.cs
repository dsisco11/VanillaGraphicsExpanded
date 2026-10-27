using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns packed depth-stencil storage and exposes each aspect for readback.</summary>
public sealed class DepthStencilTexture : DynamicTexture2D
{
    /// <summary>Creates a packed depth-stencil texture.</summary>
    public DepthStencilTexture(int width, int height,
        TextureFormat format = TextureFormat.Depth24Stencil8,
        string? debugName = null)
        : base(width, height, ValidateFormat(format), TextureFilterMode.Nearest, debugName) { }


    /// <summary>Reads the stencil aspect as unsigned bytes.</summary>
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
    protected override RHI.RHIPixelEncoding ReadbackEncoding => new(RHI.PixelChannelLayout.Depth, RHI.PixelComponentType.Float);


    /// <summary>Rejects formats without packed depth-stencil storage.</summary>
    private static TextureFormat ValidateFormat(TextureFormat format)
    {
        if (format is not (TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8))
            throw new ArgumentException("A packed depth-stencil internal format is required.", nameof(format));

        return format;
    }
}
