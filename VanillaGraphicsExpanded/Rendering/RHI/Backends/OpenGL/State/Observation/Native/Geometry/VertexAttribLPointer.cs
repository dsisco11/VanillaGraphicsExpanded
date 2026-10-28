using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribLPointer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribLPointer")]
    internal static void ObserveVertexAttribLPointer(uint a0, int a1, int a2, int a3, nint a4)
    {
        int @index = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribDoubleType @type = unchecked((VertexAttribDoubleType)a2);
        int @stride = unchecked((int)a3);
        IntPtr @pointer = unchecked((IntPtr)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
