using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glCompressedTextureImage3DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCompressedTextureImage3DEXT")]
    internal static void ObserveCompressedTextureImage3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        int @texture = unchecked((int)a0);
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
        OpenGlStateCache.ObserveTextureDefinition(unchecked((int)@texture), unchecked((int)@target), -1, true, false, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@depth), 1, false, unchecked((int)@border), 0, 3, true, 0, compressedSize: unchecked((int)@imageSize), pixels: @bits);
    }
    #endregion
}
