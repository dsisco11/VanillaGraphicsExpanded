using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayElementBuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayElementBuffer")]
    internal static void ObserveVertexArrayElementBuffer(uint a0, uint a1)
    {
        int @vaobj = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, true);
    }
    #endregion
}
