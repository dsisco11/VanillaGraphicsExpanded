using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Checks a changed external framebuffer configuration without perturbing read or draw selectors.</summary>
internal static class OpenGLFramebufferCompleteness
{
    #region Internal API
    /// <summary>Requires core named completeness after the existing metadata/routing proof becomes obsolete.</summary>
    internal static void RequireComplete(int framebuffer)
    {
        var origin=GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer);
        var status=GL.CheckNamedFramebufferStatus(framebuffer,FramebufferTarget.Framebuffer);
        if(!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))throw new InvalidOperationException("Framebuffer completeness returned in a foreign context.");
        if(status!=FramebufferStatus.FramebufferComplete)throw new InvalidOperationException($"Framebuffer incomplete: {status}.");
    }
    #endregion
}
