using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayAttribFormat after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayAttribFormat")]
    internal static void ObserveVertexArrayAttribFormat(uint a0, uint a1, int a2, int a3, byte a4, uint a5)
    {
        int @vaobj = unchecked((int)a0);
        int @attribindex = unchecked((int)a1);
        int @size = unchecked((int)a2);
        VertexAttribType @type = unchecked((VertexAttribType)a3);
        bool @normalized = a4 != 0;
        int @relativeoffset = unchecked((int)a5);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
