using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedFramebufferTextureFaceEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferTextureFaceEXT")]
    internal static void ObserveNamedFramebufferTextureFaceEXT(uint a0, int a1, uint a2, int a3, int a4)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        int @texture = unchecked((int)a2);
        int @level = unchecked((int)a3);
        TextureTarget @face = unchecked((TextureTarget)a4);
        _ = OpenGLObservationContext.CurrentCache;
        ObserveFramebufferMutation(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
    }
    #endregion
}
