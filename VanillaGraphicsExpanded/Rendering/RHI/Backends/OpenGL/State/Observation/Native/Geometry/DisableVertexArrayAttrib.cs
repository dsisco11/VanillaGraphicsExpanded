using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDisableVertexArrayAttrib")]
    [InterceptOpenGL("glDisableVertexArrayAttribEXT")]
    [InterceptOpenGL("glEnableVertexArrayAttrib")]
    [InterceptOpenGL("glEnableVertexArrayAttribEXT")]
    internal static void ObserveDisableVertexArrayAttrib(uint a0, uint a1)
    {
        int @vaobj = unchecked((int)a0);
        int @index = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
