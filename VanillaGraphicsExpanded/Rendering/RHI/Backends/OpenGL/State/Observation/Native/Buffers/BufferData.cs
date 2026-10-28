using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBufferData after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBufferData")]
    internal static void ObserveBufferData(int a0, nint a1, nint a2, int a3)
    {
        BufferTarget @target = unchecked((BufferTarget)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        IntPtr @data = unchecked((IntPtr)a2);
        BufferUsageHint @usage = unchecked((BufferUsageHint)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferStorage(unchecked((int)@target), false);
    }
    #endregion
}
