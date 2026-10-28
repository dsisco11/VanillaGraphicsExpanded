using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glMultiTexImage3DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glMultiTexImage3DEXT")]
    internal static void ObserveMultiTexImage3DEXT(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, nint a10)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @depth = unchecked((int)a6);
        int @border = unchecked((int)a7);
        PixelFormat @format = unchecked((PixelFormat)a8);
        PixelType @type = unchecked((PixelType)a9);
        IntPtr @pixels = unchecked((IntPtr)a10);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
