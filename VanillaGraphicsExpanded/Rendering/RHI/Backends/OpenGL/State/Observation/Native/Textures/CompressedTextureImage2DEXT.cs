using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glCompressedTextureImage2DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCompressedTextureImage2DEXT")]
    internal static void ObserveCompressedTextureImage2DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, nint a8)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        InternalFormat @internalformat = unchecked((InternalFormat)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @border = unchecked((int)a6);
        int @imageSize = unchecked((int)a7);
        IntPtr @bits = unchecked((IntPtr)a8);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(unchecked((int)@texture), unchecked((int)@target), -1, true, false, unchecked((int)@level), unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), 1, 1, false, unchecked((int)@border), 0, 2, true, 0, compressedSize: unchecked((int)@imageSize), pixels: @bits);
    }
    #endregion
}
