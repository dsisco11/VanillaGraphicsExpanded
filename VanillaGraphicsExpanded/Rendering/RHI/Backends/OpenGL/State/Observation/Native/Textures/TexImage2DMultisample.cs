using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexImage2DMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexImage2DMultisample")]
    internal static void ObserveTexImage2DMultisample(int a0, int a1, int a2, int a3, int a4, byte a5)
    {
        TextureTargetMultisample @target = unchecked((TextureTargetMultisample)a0);
        int @samples = unchecked((int)a1);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        bool @fixedsamplelocations = a5 != 0;
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), -1, false, false, 0, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), 1, 1, false, 0, unchecked((int)@samples), 2, false, 0);
    }
    #endregion
}
