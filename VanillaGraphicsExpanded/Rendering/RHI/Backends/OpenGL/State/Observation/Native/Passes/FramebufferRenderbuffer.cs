using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glFramebufferRenderbuffer")]
    [InterceptOpenGL("glFramebufferRenderbufferEXT")]
    internal static void ObserveFramebufferRenderbuffer(int a0, int a1, int a2, uint a3)
    {
        FramebufferTarget @target = unchecked((FramebufferTarget)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        RenderbufferTarget @renderbuffertarget = unchecked((RenderbufferTarget)a2);
        int @renderbuffer = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        ObserveFramebufferMutation(unchecked((int)@target), false, unchecked((int)@attachment), 0, 0, -1, 3);
    }
    #endregion
}
