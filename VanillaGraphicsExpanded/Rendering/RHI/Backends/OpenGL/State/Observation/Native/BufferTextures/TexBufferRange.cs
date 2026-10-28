using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexBufferRange after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexBufferRange")]
    internal static void ObserveTexBufferRange(int a0, int a1, uint a2, nint a3, nint a4)
    {
        TextureBufferTarget @target = unchecked((TextureBufferTarget)a0);
        SizedInternalFormat @internalformat = unchecked((SizedInternalFormat)a1);
        int @buffer = unchecked((int)a2);
        IntPtr @offset = unchecked((IntPtr)a3);
        IntPtr @size = unchecked((IntPtr)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
    }
    #endregion
}
