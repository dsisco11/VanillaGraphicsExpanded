using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexImage3DMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexImage3DMultisample")]
    internal static void ObserveTexImage3DMultisample(int a0, int a1, int a2, int a3, int a4, int a5, byte a6)
    {
        TextureTargetMultisample @target = unchecked((TextureTargetMultisample)a0);
        int @samples = unchecked((int)a1);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @depth = unchecked((int)a5);
        bool @fixedsamplelocations = a6 != 0;
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
