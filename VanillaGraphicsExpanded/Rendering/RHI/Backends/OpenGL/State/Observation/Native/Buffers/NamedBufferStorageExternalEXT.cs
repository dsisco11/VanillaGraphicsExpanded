using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedBufferStorageExternalEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedBufferStorageExternalEXT")]
    internal static void ObserveNamedBufferStorageExternalEXT(uint a0, nint a1, nint a2, nint a3, int a4)
    {
        int @buffer = unchecked((int)a0);
        IntPtr @offset = unchecked((IntPtr)a1);
        IntPtr @size = unchecked((IntPtr)a2);
        IntPtr @clientBuffer = unchecked((IntPtr)a3);
        BufferStorageMask @flags = unchecked((BufferStorageMask)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveBufferStorage(unchecked((int)@buffer), true);
    }
    #endregion
}
