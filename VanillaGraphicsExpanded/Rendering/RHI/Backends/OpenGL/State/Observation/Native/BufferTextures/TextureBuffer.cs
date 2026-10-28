using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureBuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureBuffer")]
    internal static void ObserveTextureBuffer(uint a0, int a1, uint a2)
    {
        int @texture = unchecked((int)a0);
        InternalFormat @internalformat = unchecked((InternalFormat)a1);
        int @buffer = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
    }
    #endregion
}
