using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glCopyTextureImage1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glCopyTextureImage1DEXT")]
    internal static void ObserveCopyTextureImage1DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        InternalFormat @internalformat = unchecked((InternalFormat)a3);
        int @x = unchecked((int)a4);
        int @y = unchecked((int)a5);
        int @width = unchecked((int)a6);
        int @border = unchecked((int)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
