using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorage2DMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorage2DMultisample")]
    internal static void ObserveTexStorage2DMultisample(int a0, int a1, int a2, int a3, int a4, byte a5)
    {
        TextureTargetMultisample2d @target = unchecked((TextureTargetMultisample2d)a0);
        int @samples = unchecked((int)a1);
        SizedInternalFormat @internalformat = unchecked((SizedInternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        bool @fixedsamplelocations = a5 != 0;
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
