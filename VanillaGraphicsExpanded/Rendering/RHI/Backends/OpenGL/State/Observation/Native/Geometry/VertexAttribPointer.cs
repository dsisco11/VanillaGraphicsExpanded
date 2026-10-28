using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribPointer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribPointer")]
    internal static void ObserveVertexAttribPointer(uint a0, int a1, int a2, byte a3, int a4, nint a5)
    {
        int @index = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribPointerType @type = unchecked((VertexAttribPointerType)a2);
        bool @normalized = a3 != 0;
        int @stride = unchecked((int)a4);
        IntPtr @pointer = unchecked((IntPtr)a5);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
