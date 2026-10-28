using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindVertexBuffers after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindVertexBuffers")]
    internal static void ObserveBindVertexBuffers(uint a0, int a1, uint* a2, nint* a3, int* a4)
    {
        int @first = unchecked((int)a0);
        int @count = unchecked((int)a1);
        int* @buffers = unchecked((int*)a2);
        IntPtr* @offsets = unchecked((IntPtr*)a3);
        int* @strides = unchecked((int*)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
