using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayVertexAttribLOffsetEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexAttribLOffsetEXT")]
    internal static void ObserveVertexArrayVertexAttribLOffsetEXT(uint a0, uint a1, uint a2, int a3, int a4, int a5, nint a6)
    {
        int @vaobj = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        int @index = unchecked((int)a2);
        int @size = unchecked((int)a3);
        ExtDirectStateAccess @type = unchecked((ExtDirectStateAccess)a4);
        int @stride = unchecked((int)a5);
        IntPtr @offset = unchecked((IntPtr)a6);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
