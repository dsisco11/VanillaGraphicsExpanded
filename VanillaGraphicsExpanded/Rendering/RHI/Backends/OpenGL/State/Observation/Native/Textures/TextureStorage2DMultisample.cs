using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorage2DMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorage2DMultisample")]
    internal static void ObserveTextureStorage2DMultisample(uint a0, int a1, int a2, int a3, int a4, byte a5)
    {
        int @texture = unchecked((int)a0);
        int @samples = unchecked((int)a1);
        InternalFormat @internalformat = unchecked((InternalFormat)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        bool @fixedsamplelocations = a5 != 0;
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
