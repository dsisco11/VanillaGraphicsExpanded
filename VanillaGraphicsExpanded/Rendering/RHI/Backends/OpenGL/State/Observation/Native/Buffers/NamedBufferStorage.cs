using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedBufferStorage after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedBufferStorage")]
    internal static void ObserveNamedBufferStorage(uint a0, nint a1, nint a2, int a3)
    {
        int @buffer = unchecked((int)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        IntPtr @data = unchecked((IntPtr)a2);
        BufferStorageFlags @flags = unchecked((BufferStorageFlags)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferStorage(unchecked((int)@buffer), true);
    }
    #endregion
}
