using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Restores framebuffer-local output routing through the available native API.</summary>
public sealed partial class GpuFramebuffer
{
    #region Internal API
    /// <summary>Restores framebuffer-local output routing without rebinding image targets when direct state access is available.</summary>
    internal void RestoreDrawRouting(DrawBuffersEnum[] routing)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (fboId != 0 && GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            GL.NamedFramebufferDrawBuffers(fboId, routing.Length, routing);
            return;
        }
        // Window selectors such as Back require DrawBuffer. Older contexts retain the bound path.
        StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fboId);
        if (fboId == 0) GL.DrawBuffer((DrawBufferMode)routing[0]);
        else GL.DrawBuffers(routing.Length, routing);
    }
    #endregion
}
