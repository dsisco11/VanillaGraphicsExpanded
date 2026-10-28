using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayVertexAttribOffsetEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexAttribOffsetEXT")]
    internal static void ObserveVertexArrayVertexAttribOffsetEXT(uint a0, uint a1, uint a2, int a3, int a4, byte a5, int a6, nint a7)
    {
        int @vaobj = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        int @index = unchecked((int)a2);
        int @size = unchecked((int)a3);
        VertexAttribPointerType @type = unchecked((VertexAttribPointerType)a4);
        bool @normalized = a5 != 0;
        int @stride = unchecked((int)a6);
        IntPtr @offset = unchecked((IntPtr)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
