using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureView after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureView")]
    internal static void ObserveTextureView(uint a0, int a1, uint a2, int a3, uint a4, uint a5, uint a6, uint a7)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @origtexture = unchecked((int)a2);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a3);
        int @minlevel = unchecked((int)a4);
        int @numlevels = unchecked((int)a5);
        int @minlayer = unchecked((int)a6);
        int @numlayers = unchecked((int)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
