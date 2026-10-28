using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureBufferRange after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureBufferRange")]
    internal static void ObserveTextureBufferRange(uint a0, int a1, uint a2, nint a3, nint a4)
    {
        int @texture = unchecked((int)a0);
        InternalFormat @internalformat = unchecked((InternalFormat)a1);
        int @buffer = unchecked((int)a2);
        IntPtr @offset = unchecked((IntPtr)a3);
        IntPtr @size = unchecked((IntPtr)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
    }
    #endregion
}
