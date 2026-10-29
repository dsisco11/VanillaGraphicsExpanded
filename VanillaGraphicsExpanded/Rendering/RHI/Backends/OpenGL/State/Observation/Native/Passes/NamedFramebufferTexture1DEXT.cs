using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferTexture1DEXT")]
    internal static void ObserveNamedFramebufferTexture1DEXT(uint a0, int a1, int a2, uint a3, int a4)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferAttachment @attachment = unchecked((FramebufferAttachment)a1);
        TextureTarget @textarget = unchecked((TextureTarget)a2);
        int @texture = unchecked((int)a3);
        int @level = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        cache.RealizeFramebuffer(@framebuffer, implicitCreation: true);
        if (@textarget != TextureTarget.Texture1D) return;
        ObserveFramebufferMutation(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3, imageTarget: unchecked((int)@textarget), extNamed: true);
    }

    /// <summary>Publishes an accepted EXT named two-dimensional framebuffer texture selection.</summary>
    [InterceptOpenGL("glNamedFramebufferTexture2DEXT")]
    internal static void ObserveNamedFramebufferTexture2DEXT(uint framebuffer, int attachment, int imageTarget, uint texture, int level)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        cache.RealizeFramebuffer(unchecked((int)framebuffer), implicitCreation: true);
        if (!IsFramebufferTexture2DTarget((TextureTarget)imageTarget)) return;
        ObserveFramebufferMutation(unchecked((int)framebuffer), true, attachment, unchecked((int)texture), level, -1, 3, imageTarget: imageTarget, extNamed: true);
    }
    #endregion
}
