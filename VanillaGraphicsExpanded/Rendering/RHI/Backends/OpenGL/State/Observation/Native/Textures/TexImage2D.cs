using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexImage2D after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexImage2D")]
    internal static void ObserveTexImage2D(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, nint a8)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @level = unchecked((int)a1);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @border = unchecked((int)a5);
        PixelFormat @format = unchecked((PixelFormat)a6);
        PixelType @type = unchecked((PixelType)a7);
        IntPtr @pixels = unchecked((IntPtr)a8);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
