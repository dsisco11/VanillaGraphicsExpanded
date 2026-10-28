using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glFramebufferTextureLayer")]
    [InterceptOpenGL("glFramebufferTextureLayerARB")]
    [InterceptOpenGL("glFramebufferTextureLayerEXT")]
    internal static void ObserveFramebufferTextureLayer(int a0, int a1, uint a2, int a3, int a4)
    {
        FramebufferTarget @target = unchecked((FramebufferTarget)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        int @texture = unchecked((int)a2);
        int @level = unchecked((int)a3);
        int @layer = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        ObserveFramebufferMutation(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
    }
    #endregion
}
