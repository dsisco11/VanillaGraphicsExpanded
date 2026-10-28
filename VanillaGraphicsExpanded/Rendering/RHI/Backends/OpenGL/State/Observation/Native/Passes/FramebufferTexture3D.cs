using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glFramebufferTexture3D")]
    [InterceptOpenGL("glFramebufferTexture3DEXT")]
    internal static void ObserveFramebufferTexture3D(int a0, int a1, int a2, uint a3, int a4, int a5)
    {
        FramebufferTarget @target = unchecked((FramebufferTarget)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        TextureTarget @textarget = unchecked((TextureTarget)a2);
        int @texture = unchecked((int)a3);
        int @level = unchecked((int)a4);
        int @zoffset = unchecked((int)a5);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        ObserveFramebufferMutation(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
    }
    #endregion
}
