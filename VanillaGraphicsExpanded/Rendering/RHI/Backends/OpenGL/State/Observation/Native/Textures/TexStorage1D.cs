using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorage1D after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorage1D")]
    internal static void ObserveTexStorage1D(int a0, int a1, int a2, int a3)
    {
        TextureTarget1d @target = unchecked((TextureTarget1d)a0);
        int @levels = unchecked((int)a1);
        SizedInternalFormat @internalformat = unchecked((SizedInternalFormat)a2);
        int @width = unchecked((int)a3);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
