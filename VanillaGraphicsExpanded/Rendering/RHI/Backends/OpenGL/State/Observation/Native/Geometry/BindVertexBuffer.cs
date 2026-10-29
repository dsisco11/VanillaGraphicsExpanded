using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindVertexBuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindVertexBuffer")]
    internal static void ObserveBindVertexBuffer(uint a0, uint a1, nint a2, int a3)
    {
        int @bindingindex = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        IntPtr @offset = unchecked((IntPtr)a2);
        int @stride = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.ObserveVertexBufferRealization(0, false, a0, buffer, offset, stride);
        if (!Current.PrepareVertexObservation(0, false, false, RejectVertexIndex(bindingindex, true) || RejectVertexRange(offset, stride))) return;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
