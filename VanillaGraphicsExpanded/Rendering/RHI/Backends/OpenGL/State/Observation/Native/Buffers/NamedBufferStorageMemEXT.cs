using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedBufferStorageMemEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedBufferStorageMemEXT")]
    internal static void ObserveNamedBufferStorageMemEXT(uint a0, nint a1, uint a2, ulong a3)
    {
        int @buffer = unchecked((int)a0);
        IntPtr @size = unchecked((IntPtr)a1);
        int @memory = unchecked((int)a2);
        long @offset = unchecked((long)a3);
        if (a2 == 0 || a1 < 0 || a3 > ulong.MaxValue - (ulong)a1) return;
        ObserveBufferStorage(unchecked((int)a0), true, false, a1, (int)BufferUsageHint.DynamicDraw, 0, true, unresolvedImport: true);
    }
    #endregion
}
