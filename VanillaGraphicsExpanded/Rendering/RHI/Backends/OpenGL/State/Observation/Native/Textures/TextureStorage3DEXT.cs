using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorage3DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorage3DEXT")]
    internal static void ObserveTextureStorage3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6)
    {
        int @texture = unchecked((int)a0);
        All @target = unchecked((All)a1);
        int @levels = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @depth = unchecked((int)a6);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(unchecked((int)@texture), unchecked((int)@target), -1, true, false, 0, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@depth), unchecked((int)@levels), true, 0, 0, 3, true, 0);
    }
    #endregion
}
