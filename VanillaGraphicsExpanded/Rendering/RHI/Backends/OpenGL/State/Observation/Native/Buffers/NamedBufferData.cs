using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedBufferData")]
    internal static void ObserveNamedBufferData(uint a0, nint a1, nint a2, int a3)
    {
        int @buffer = unchecked((int)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        IntPtr @data = unchecked((IntPtr)a2);
        BufferUsageHint @usage = unchecked((BufferUsageHint)a3);
        ObserveBufferStorage(unchecked((int)a0), true, false, a1, a3, 0, false);
    }
    #endregion
}
