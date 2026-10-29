using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    
    [InterceptOpenGL("glVertexArrayVertexAttribLFormatEXT")]
    internal static void ObserveVertexArrayVertexAttribLFormatEXT(uint a0, uint a1, int a2, int a3, uint a4)
    {
        int @vaobj = unchecked((int)a0);
        int @attribindex = unchecked((int)a1);
        int @size = unchecked((int)a2);
        ExtDirectStateAccess @type = unchecked((ExtDirectStateAccess)a3);
        int @relativeoffset = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!Current.PrepareVertexObservation(vaobj, true, true, RejectVertexIndex(attribindex) || RejectVertexFormat(size, (int)type, 2, false, relativeoffset))) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
