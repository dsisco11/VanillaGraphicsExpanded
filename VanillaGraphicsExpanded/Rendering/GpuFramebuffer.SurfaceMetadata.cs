using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes actual window-surface formats at registration or resize, independently of managed image attachments.</summary>
public abstract partial class GpuFramebuffer
{
    internal SurfaceMetadata? Surface { get; private set; }

    /// <summary>Describes the single supported front/back window color route and its exact native format.</summary>
    internal sealed record SurfaceMetadata(DrawBuffersEnum Buffer, PixelInternalFormat Color,
        PixelInternalFormat? DepthStencil, int Samples, bool HasDepth, bool HasStencil);

    #region Internal API
    /// <summary>Queries the current window surface once at its publication boundary; unsupported formats reject explicitly.</summary>
    internal void PublishSurfaceMetadata(int width, int height)
    {
        RequireMutableStorage();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer || fboId != 0 || width <= 0 || height <= 0)
            throw new InvalidOperationException("Surface metadata requires a borrowed default framebuffer and positive window dimensions.");
        var candidate = RHICommandContext.Current.ReadWindowSurface();
        Surface = candidate;
        wrappedWidth = width;
        wrappedHeight = height;
        PublishAttachmentsChanged();
    }
    #endregion
}
