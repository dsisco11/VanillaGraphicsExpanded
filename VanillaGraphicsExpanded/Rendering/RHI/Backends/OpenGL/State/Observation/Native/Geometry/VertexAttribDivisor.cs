using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexAttribDivisor")]
    [InterceptOpenGL("glVertexAttribDivisorARB")]
    internal static void ObserveVertexAttribDivisor(uint a0, uint a1)
    {
        int @index = unchecked((int)a0);
        int @divisor = unchecked((int)a1);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
