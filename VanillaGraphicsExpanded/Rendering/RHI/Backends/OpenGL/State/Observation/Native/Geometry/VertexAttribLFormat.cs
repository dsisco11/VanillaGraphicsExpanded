using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribLFormat after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribLFormat")]
    internal static void ObserveVertexAttribLFormat(uint a0, int a1, int a2, uint a3)
    {
        int @attribindex = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribDoubleType @type = unchecked((VertexAttribDoubleType)a2);
        int @relativeoffset = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
