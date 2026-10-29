using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayBindVertexBufferEXT")]
    internal static void ObserveVertexArrayBindVertexBufferEXT(uint a0, uint a1, uint a2, nint a3, int a4)
    {
        int @vaobj = unchecked((int)a0);
        int @bindingindex = unchecked((int)a1);
        int @buffer = unchecked((int)a2);
        IntPtr @offset = unchecked((IntPtr)a3);
        int @stride = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.RealizeExtVertexArray(vaobj);
        Current.ObserveVertexBufferRealization(vaobj, true, a1, buffer, offset, stride);
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
