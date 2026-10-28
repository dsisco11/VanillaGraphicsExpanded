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
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
