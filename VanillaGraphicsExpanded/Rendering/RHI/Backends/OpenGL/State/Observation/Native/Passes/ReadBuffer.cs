using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes explicit read selectors while preserving split framebuffer bindings.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Records a valid bound read selector on its selected framebuffer.</summary>
    [InterceptOpenGL("glReadBuffer")]
    internal static void ObserveReadBuffer(int route)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        cache.ObserveFramebufferReadRouting(cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer), route);
    }

    /// <summary>Records a named selector only for an already realized native object.</summary>
    [InterceptOpenGL("glNamedFramebufferReadBuffer")]
    internal static void ObserveNamedFramebufferReadBuffer(uint framebuffer, int route)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        cache.ObserveFramebufferReadRouting(unchecked((int)framebuffer), route);
    }

    /// <summary>Preserves eligible EXT creation before interpreting its routing effect.</summary>
    [InterceptOpenGL("glFramebufferReadBufferEXT")]
    internal static void ObserveFramebufferReadBufferEXT(uint framebuffer, int route)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        cache.RealizeFramebuffer(unchecked((int)framebuffer), implicitCreation: true);
        cache.ObserveFramebufferReadRouting(unchecked((int)framebuffer), route);
    }
    #endregion

    #region Private
    /// <summary>Leaves proven rejected selectors unchanged and contains uncertain window-buffer validity.</summary>
    private void ObserveFramebufferReadRouting(int framebuffer, int route)
    {
        if (framebuffer == 0)
        {
            bool? accepted = WindowDrawBufferAccepted((DrawBufferMode)route, false, GpuSupport.Graphics.DoubleBuffered, GpuSupport.Graphics.Stereo);
            if (accepted == false) return;
            if (accepted is null) { framebufferReadRouting.Remove(framebuffer); return; }
        }
        else
        {
            if (RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Framebuffer, framebuffer)?.IsRealized != true) return;
            if (route != (int)ReadBufferMode.None && (route < (int)ReadBufferMode.ColorAttachment0
                || (long)route - (int)ReadBufferMode.ColorAttachment0 >= GpuSupport.Graphics.MaxColorAttachments)) return;
        }
        RememberFramebufferReadRouting(framebuffer, (ReadBufferMode)route);
    }
    #endregion
}
