using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexImage3D after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexImage3D")]
    internal static void ObserveTexImage3D(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @level = unchecked((int)a1);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @depth = unchecked((int)a5);
        int @border = unchecked((int)a6);
        PixelFormat @format = unchecked((PixelFormat)a7);
        PixelType @type = unchecked((PixelType)a8);
        IntPtr @pixels = unchecked((IntPtr)a9);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), -1, false, false, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@depth), 1, false, unchecked((int)@border), 0, 3, false, 0, pixelFormat: unchecked((int)@format), pixelType: unchecked((int)@type), pixels: @pixels);
    }
    #endregion
}
