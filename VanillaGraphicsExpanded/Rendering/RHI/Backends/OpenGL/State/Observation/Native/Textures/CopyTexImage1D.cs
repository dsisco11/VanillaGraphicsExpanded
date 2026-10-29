using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCopyTexImage1D")]
    [InterceptOpenGL("glCopyTexImage1DEXT")]
    internal static void ObserveCopyTexImage1D(int a0, int a1, int a2, int a3, int a4, int a5, int a6)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @level = unchecked((int)a1);
        InternalFormat @internalformat = unchecked((InternalFormat)a2);
        int @x = unchecked((int)a3);
        int @y = unchecked((int)a4);
        int @width = unchecked((int)a5);
        int @border = unchecked((int)a6);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), -1, false, false, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), 1, 1, 1, false, unchecked((int)@border), 0, 1, false, 0, copy: true);
    }
    #endregion
}
