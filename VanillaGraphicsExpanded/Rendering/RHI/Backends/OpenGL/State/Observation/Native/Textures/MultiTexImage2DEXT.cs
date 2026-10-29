using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glMultiTexImage2DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glMultiTexImage2DEXT")]
    internal static void ObserveMultiTexImage2DEXT(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @border = unchecked((int)a6);
        PixelFormat @format = unchecked((PixelFormat)a7);
        PixelType @type = unchecked((PixelType)a8);
        IntPtr @pixels = unchecked((IntPtr)a9);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), 1, 1, false, unchecked((int)@border), 0, 2, false, 0, pixelFormat: unchecked((int)@format), pixelType: unchecked((int)@type), pixels: @pixels);
    }
    #endregion
}
