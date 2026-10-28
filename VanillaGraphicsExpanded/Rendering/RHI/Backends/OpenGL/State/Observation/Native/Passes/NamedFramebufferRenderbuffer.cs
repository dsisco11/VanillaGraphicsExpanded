using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferRenderbuffer")]
    [InterceptOpenGL("glNamedFramebufferRenderbufferEXT")]
    internal static void ObserveNamedFramebufferRenderbuffer(uint a0, int a1, int a2, uint a3)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        RenderbufferTarget @renderbuffertarget = unchecked((RenderbufferTarget)a2);
        int @renderbuffer = unchecked((int)a3);
        _ = OpenGLObservationContext.CurrentCache;
        ObserveFramebufferMutation(unchecked((int)@framebuffer), true, unchecked((int)@attachment), 0, 0, -1, 3);
    }
    #endregion
}
