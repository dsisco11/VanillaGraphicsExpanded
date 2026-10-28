using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferTextureLayer")]
    [InterceptOpenGL("glNamedFramebufferTextureLayerEXT")]
    internal static void ObserveNamedFramebufferTextureLayer(uint a0, int a1, uint a2, int a3, int a4)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        int @texture = unchecked((int)a2);
        int @level = unchecked((int)a3);
        int @layer = unchecked((int)a4);
        _ = OpenGLObservationContext.CurrentCache;
        ObserveFramebufferMutation(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
    }
    #endregion
}
