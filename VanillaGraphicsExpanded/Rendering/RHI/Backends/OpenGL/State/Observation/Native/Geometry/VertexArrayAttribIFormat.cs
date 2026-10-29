using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayAttribIFormat after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayAttribIFormat")]
    internal static void ObserveVertexArrayAttribIFormat(uint a0, uint a1, int a2, int a3, uint a4)
    {
        int @vaobj = unchecked((int)a0);
        int @attribindex = unchecked((int)a1);
        int @size = unchecked((int)a2);
        VertexAttribIntegerType @type = unchecked((VertexAttribIntegerType)a3);
        int @relativeoffset = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!Current.PrepareVertexObservation(vaobj, true, false, RejectVertexIndex(attribindex) || RejectVertexFormat(size, (int)type, 1, false, relativeoffset))) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
