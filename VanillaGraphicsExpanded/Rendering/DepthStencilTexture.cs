using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
        if (!IsValid) throw new InvalidOperationException("Stencil texture is invalid.");
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), 0, 0, 0, Width, Height, 1), new(RHIPixelChannels.Stencil, RHIPixelType.UnsignedByte)));
        if (request.Wait() != RHICompletionStatus.Ready)
            throw new InvalidOperationException("Stencil readback failed.", request.Error);
        using var mapping = request.MapReadyReadback();
        return mapping.Bytes.ToArray();
    }



    /// <inheritdoc />
    protected override PixelFormat ReadbackFormat => PixelFormat.DepthComponent;



    /// <summary>Rejects formats without packed depth-stencil storage.</summary>
    private static PixelInternalFormat ValidateFormat(PixelInternalFormat format)
    {
        if (format is not (PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8))
            throw new ArgumentException("A packed depth-stencil internal format is required.", nameof(format));

        return format;
    }
}
