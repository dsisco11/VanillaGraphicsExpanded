using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribIPointer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribIPointer")]
    internal static void ObserveVertexAttribIPointer(uint a0, int a1, int a2, int a3, nint a4)
    {
        int @index = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribIntegerType @type = unchecked((VertexAttribIntegerType)a2);
        int @stride = unchecked((int)a3);
        IntPtr @pointer = unchecked((IntPtr)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!Current.PrepareVertexObservation(0, false, false, RejectVertexIndex(index) || RejectVertexFormat(size, (int)type, 1, false, 0) || Current.RejectVertexPointer(pointer, stride))) return;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
