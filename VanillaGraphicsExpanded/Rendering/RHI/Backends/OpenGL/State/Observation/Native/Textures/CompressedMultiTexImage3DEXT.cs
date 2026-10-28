using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glCompressedMultiTexImage3DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCompressedMultiTexImage3DEXT")]
    internal static void ObserveCompressedMultiTexImage3DEXT(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        InternalFormat @internalformat = unchecked((InternalFormat)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @depth = unchecked((int)a6);
        int @border = unchecked((int)a7);
        int @imageSize = unchecked((int)a8);
        IntPtr @bits = unchecked((IntPtr)a9);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
