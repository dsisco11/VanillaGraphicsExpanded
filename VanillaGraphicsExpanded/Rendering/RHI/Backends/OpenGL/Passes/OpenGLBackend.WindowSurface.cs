using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Reads explicit external window-surface facts at registration or resize only.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public GpuFramebuffer.SurfaceMetadata ReadWindowSurface()
    {
        RequireContext();
        using var binding = OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, 0);
        OpenGlStateCache.Current.InvalidateFramebufferDrawRouting(0);
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
            (8, 8, 8, 8, false) => TextureFormat.Rgba8,
            (8, 8, 8, 8, true) => TextureFormat.Srgb8Alpha8,
            (8, 8, 8, 0, false) => TextureFormat.Rgb8,
            (8, 8, 8, 0, true) => TextureFormat.Srgb8,
            (10, 10, 10, 2, false) => TextureFormat.Rgb10A2,
            _ => throw new NotSupportedException("Unsupported window color storage.")
        };
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Depth,
            FramebufferParameterName.FramebufferAttachmentDepthSize, out int depth);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Stencil,
            FramebufferParameterName.FramebufferAttachmentStencilSize, out int stencil);
        TextureFormat? depthStencil = (depth, stencil) switch
        {
            (0, 0) => null,
            (16, 0) => TextureFormat.DepthComponent16,
            (24, 0) => TextureFormat.DepthComponent24,
            (24, 8) => TextureFormat.Depth24Stencil8,
            (0, 8) => TextureFormat.StencilIndex8,
            _ => throw new NotSupportedException("Unsupported window depth/stencil storage.")
        };
        var candidate = new GpuFramebuffer.SurfaceMetadata(buffered ? SurfaceColorBuffer.BackLeft : SurfaceColorBuffer.FrontLeft,
            color, depthStencil, Math.Max(1, GL.GetInteger(GetPName.Samples)), depth > 0, stencil > 0);
        RequireContext();
        return candidate;
    }
    #endregion
}
