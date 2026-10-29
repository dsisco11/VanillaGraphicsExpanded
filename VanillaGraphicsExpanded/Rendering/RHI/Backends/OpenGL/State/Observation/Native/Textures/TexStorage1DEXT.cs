using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorage1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorage1DEXT")]
    internal static void ObserveTexStorage1DEXT(int a0, int a1, int a2, int a3)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @levels = unchecked((int)a1);
        SizedInternalFormat @internalformat = unchecked((SizedInternalFormat)a2);
        int @width = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), -1, false, false, 0, unchecked((int)@internalformat), unchecked((int)@width), 1, 1, unchecked((int)@levels), true, 0, 0, 1, false, 0);
    }
    #endregion
}
