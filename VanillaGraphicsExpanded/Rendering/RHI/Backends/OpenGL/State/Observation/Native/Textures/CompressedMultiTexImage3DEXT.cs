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
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@depth), 1, false, unchecked((int)@border), 0, 3, false, 0, compressedSize: unchecked((int)@imageSize), pixels: @bits);
    }
    #endregion
}
