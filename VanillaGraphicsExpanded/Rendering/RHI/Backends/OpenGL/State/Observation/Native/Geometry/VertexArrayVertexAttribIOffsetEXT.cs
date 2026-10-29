using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayVertexAttribIOffsetEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexAttribIOffsetEXT")]
    internal static void ObserveVertexArrayVertexAttribIOffsetEXT(uint a0, uint a1, uint a2, int a3, int a4, int a5, nint a6)
    {
        int @vaobj = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        int @index = unchecked((int)a2);
        int @size = unchecked((int)a3);
        VertexAttribEnum @type = unchecked((VertexAttribEnum)a4);
        int @stride = unchecked((int)a5);
        IntPtr @offset = unchecked((IntPtr)a6);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.ObserveExtVertexBufferRealization(vaobj, buffer);
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
