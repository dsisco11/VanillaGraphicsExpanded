using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorage1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorage1DEXT")]
    internal static void ObserveTextureStorage1DEXT(uint a0, int a1, int a2, int a3, int a4)
    {
        int @texture = unchecked((int)a0);
        All @target = unchecked((All)a1);
        int @levels = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
