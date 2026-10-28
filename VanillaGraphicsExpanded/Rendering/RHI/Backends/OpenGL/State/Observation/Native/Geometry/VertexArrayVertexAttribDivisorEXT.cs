using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayVertexAttribDivisorEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexAttribDivisorEXT")]
    internal static void ObserveVertexArrayVertexAttribDivisorEXT(uint a0, uint a1, uint a2)
    {
        int @vaobj = unchecked((int)a0);
        int @index = unchecked((int)a1);
        int @divisor = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
