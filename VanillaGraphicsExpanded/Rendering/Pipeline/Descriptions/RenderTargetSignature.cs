using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Exact normalized output formats and samples, independent of dimensions and resource names.</summary>
internal sealed record RenderTargetSignature
{
    public PipelineValues<ColorTargetSlot> Colors { get; }
    public PixelInternalFormat? DepthStencilFormat { get; }
    public int Samples { get; }
    public bool HasDepth => DepthStencilFormat is PixelInternalFormat.DepthComponent16 or PixelInternalFormat.DepthComponent24
        or PixelInternalFormat.DepthComponent32 or PixelInternalFormat.DepthComponent32f
        or PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8;
    public bool HasStencil => DepthStencilFormat is PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8
        or TextureFormatHelper.StencilIndex8;

    #region Public API
    /// <summary>Preserves sparse slots and rejects unknown, unsized and non-renderable format declarations.</summary>
    public RenderTargetSignature(IEnumerable<ColorTargetSlot> colors, PixelInternalFormat? depthStencilFormat = null, int samples = 1)
    {
        Colors = new(colors);
        DepthStencilFormat = depthStencilFormat;
        if (samples < 0) throw new ArgumentOutOfRangeException(nameof(samples));
        Samples = Math.Max(1, samples);
        foreach (var slot in Colors)
        {
            if (slot.Format is { } format && (slot.DiscardOutput || !TargetFormatPolicy.IsColor(format)))
                throw new ArgumentException("A routed color slot requires a supported sized format and cannot discard.", nameof(colors));
        }
        if (depthStencilFormat is not null && !HasDepth && !HasStencil)
            throw new ArgumentException("Unsupported depth/stencil format.", nameof(depthStencilFormat));
    }
    #endregion
}
