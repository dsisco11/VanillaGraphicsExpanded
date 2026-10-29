using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorage3D after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorage3D")]
    internal static void ObserveTexStorage3D(int a0, int a1, int a2, int a3, int a4, int a5)
    {
        TextureTarget3d @target = unchecked((TextureTarget3d)a0);
        int @levels = unchecked((int)a1);
        SizedInternalFormat @internalformat = unchecked((SizedInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @depth = unchecked((int)a5);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), -1, false, false, 0, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@depth), unchecked((int)@levels), true, 0, 0, 3, false, 0);
    }
    #endregion
}
