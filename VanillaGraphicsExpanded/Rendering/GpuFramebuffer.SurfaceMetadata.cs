using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes actual window-surface formats at registration or resize, independently of managed image attachments.</summary>
public sealed partial class GpuFramebuffer
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
        using var binding = StateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, 0);
        using var errors = new GlDebug.ErrorScope("Window surface metadata");
        bool buffered = GL.GetInteger(GetPName.Doublebuffer) != 0;
        var attachment = buffered ? FramebufferAttachment.BackLeft : FramebufferAttachment.FrontLeft;
        /// <summary>Reads one property of the selected native window color image.</summary>
        int Read(FramebufferParameterName parameter)
        {
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, attachment, parameter, out int value);
            return value;
        }
        int red = Read(FramebufferParameterName.FramebufferAttachmentRedSize);
        int green = Read(FramebufferParameterName.FramebufferAttachmentGreenSize);
        int blue = Read(FramebufferParameterName.FramebufferAttachmentBlueSize);
        int alpha = Read(FramebufferParameterName.FramebufferAttachmentAlphaSize);
        bool srgb = Read(FramebufferParameterName.FramebufferAttachmentColorEncoding) == (int)All.Srgb;
        var color = (red, green, blue, alpha, srgb) switch
        {
            (8, 8, 8, 8, false) => PixelInternalFormat.Rgba8,
            (8, 8, 8, 8, true) => PixelInternalFormat.Srgb8Alpha8,
            (8, 8, 8, 0, false) => PixelInternalFormat.Rgb8,
            (8, 8, 8, 0, true) => PixelInternalFormat.Srgb8,
            (10, 10, 10, 2, false) => PixelInternalFormat.Rgb10A2,
            _ => throw new NotSupportedException("Unsupported window color storage.")
        };
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Depth,
            FramebufferParameterName.FramebufferAttachmentDepthSize, out int depth);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Stencil,
            FramebufferParameterName.FramebufferAttachmentStencilSize, out int stencil);
        PixelInternalFormat? depthStencil = (depth, stencil) switch
        {
            (0, 0) => null,
            (16, 0) => PixelInternalFormat.DepthComponent16,
            (24, 0) => PixelInternalFormat.DepthComponent24,
            (24, 8) => PixelInternalFormat.Depth24Stencil8,
            (0, 8) => TextureFormatHelper.StencilIndex8,
            _ => throw new NotSupportedException("Unsupported window depth/stencil storage.")
        };
        var candidate = new SurfaceMetadata(buffered ? DrawBuffersEnum.BackLeft : DrawBuffersEnum.FrontLeft,
            color, depthStencil, Math.Max(1, GL.GetInteger(GetPName.Samples)), depth > 0, stencil > 0);
        GlDebug.ThrowIfErrors("Window surface metadata");
        Surface = candidate;
        wrappedWidth = width;
        wrappedHeight = height;
        PublishAttachmentsChanged();
    }
    #endregion
}
