using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferTexture1DEXT")]
    [InterceptOpenGL("glNamedFramebufferTexture2DEXT")]
    internal static void ObserveNamedFramebufferTexture1DEXT(uint a0, int a1, int a2, uint a3, int a4)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        TextureTarget @textarget = unchecked((TextureTarget)a2);
        int @texture = unchecked((int)a3);
        int @level = unchecked((int)a4);
        _ = OpenGLObservationContext.CurrentCache;
        ObserveFramebufferMutation(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
    }
    #endregion
}
