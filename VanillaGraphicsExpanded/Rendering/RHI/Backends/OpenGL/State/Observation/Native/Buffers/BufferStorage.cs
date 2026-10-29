using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBufferStorage after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBufferStorage")]
    internal static void ObserveBufferStorage(int a0, nint a1, nint a2, int a3)
    {
        BufferTarget @target = unchecked((BufferTarget)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        IntPtr @data = unchecked((IntPtr)a2);
        BufferStorageFlags @flags = unchecked((BufferStorageFlags)a3);
        ObserveBufferStorage(unchecked((int)a0), false, false, a1, (int)BufferUsageHint.DynamicDraw, a3, true);
    }
    #endregion
}
