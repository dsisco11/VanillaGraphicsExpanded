using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexAttribFormat after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribFormat")]
    internal static void ObserveVertexAttribFormat(uint a0, int a1, int a2, byte a3, uint a4)
    {
        int @attribindex = unchecked((int)a0);
        int @size = unchecked((int)a1);
        VertexAttribType @type = unchecked((VertexAttribType)a2);
        bool @normalized = a3 != 0;
        int @relativeoffset = unchecked((int)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
