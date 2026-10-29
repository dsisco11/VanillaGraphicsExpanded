using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glFramebufferTexture1D")]
    [InterceptOpenGL("glFramebufferTexture1DEXT")]
    internal static void ObserveFramebufferTexture1D(int a0, int a1, int a2, uint a3, int a4)
    {
        FramebufferTarget @target = unchecked((FramebufferTarget)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        TextureTarget @textarget = unchecked((TextureTarget)a2);
        int @texture = unchecked((int)a3);
        int @level = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (@textarget != TextureTarget.Texture1D) return;
        ObserveFramebufferMutation(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3, imageTarget: unchecked((int)@textarget));
    }

    /// <summary>Publishes an accepted two-dimensional framebuffer texture selection.</summary>
    [InterceptOpenGL("glFramebufferTexture2D")]
    [InterceptOpenGL("glFramebufferTexture2DEXT")]
    internal static void ObserveFramebufferTexture2D(int target, int attachment, int imageTarget, uint texture, int level)
    {
        if (!IsFramebufferTexture2DTarget((TextureTarget)imageTarget)) return;
        ObserveFramebufferMutation(target, false, attachment, unchecked((int)texture), level, -1, 3, imageTarget: imageTarget);
    }
    #endregion
}
