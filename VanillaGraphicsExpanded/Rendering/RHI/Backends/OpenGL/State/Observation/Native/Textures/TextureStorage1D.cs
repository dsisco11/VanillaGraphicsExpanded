using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorage1D after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorage1D")]
    internal static void ObserveTextureStorage1D(uint a0, int a1, int a2, int a3)
    {
        int @texture = unchecked((int)a0);
        int @levels = unchecked((int)a1);
        InternalFormat @internalformat = unchecked((InternalFormat)a2);
        int @width = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(unchecked((int)@texture), 0, -1, true, false, 0, unchecked((int)@internalformat), unchecked((int)@width), 1, 1, unchecked((int)@levels), true, 0, 0, 1, false, 0);
    }
    #endregion
}
