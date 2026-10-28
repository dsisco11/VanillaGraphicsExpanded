using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBufferStorageMemEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBufferStorageMemEXT")]
    internal static void ObserveBufferStorageMemEXT(int a0, nint a1, uint a2, ulong a3)
    {
        BufferTargetArb @target = unchecked((BufferTargetArb)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        int @memory = unchecked((int)a2);
        long @offset = unchecked((long)a3);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveBufferStorage(unchecked((int)@target), false);
    }
    #endregion
}
