using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCompressedTexImage2D")]
    [InterceptOpenGL("glCompressedTexImage2DARB")]
    internal static void ObserveCompressedTexImage2D(int a0, int a1, int a2, int a3, int a4, int a5, int a6, nint a7)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @level = unchecked((int)a1);
        InternalFormat @internalformat = unchecked((InternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @border = unchecked((int)a5);
        int @imageSize = unchecked((int)a6);
        IntPtr @data = unchecked((IntPtr)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
