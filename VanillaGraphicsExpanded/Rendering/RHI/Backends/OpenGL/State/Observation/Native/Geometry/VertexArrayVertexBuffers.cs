using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayVertexBuffers after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexBuffers")]
    internal static void ObserveVertexArrayVertexBuffers(uint a0, uint a1, int a2, uint* a3, nint* a4, int* a5)
    {
        int @vaobj = unchecked((int)a0);
        int @first = unchecked((int)a1);
        int @count = unchecked((int)a2);
        int* @buffers = unchecked((int*)a3);
        IntPtr* @offsets = unchecked((IntPtr*)a4);
        int* @strides = unchecked((int*)a5);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
