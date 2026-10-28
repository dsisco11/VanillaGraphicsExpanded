using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribIPointerEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribIPointerEXT")]
    internal static void ObserveVertexAttribIPointerEXT(uint a0, int a1, int a2, int a3, nint a4)
    {
        int @index = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribIType @type = unchecked((VertexAttribIType)a2);
        int @stride = unchecked((int)a3);
        IntPtr @pointer = unchecked((IntPtr)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
