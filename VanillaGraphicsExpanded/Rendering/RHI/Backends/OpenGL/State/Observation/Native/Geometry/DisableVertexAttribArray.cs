using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDisableVertexAttribArray")]
    [InterceptOpenGL("glDisableVertexAttribArrayARB")]
    [InterceptOpenGL("glEnableVertexAttribArray")]
    [InterceptOpenGL("glEnableVertexAttribArrayARB")]
    internal static void ObserveDisableVertexAttribArray(uint a0)
    {
        int @index = unchecked((int)a0);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(0, false, false);
    }
    #endregion
}
