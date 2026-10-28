using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayBindingDivisor")]
    [InterceptOpenGL("glVertexArrayVertexBindingDivisorEXT")]
    internal static void ObserveVertexArrayBindingDivisor(uint a0, uint a1, uint a2)
    {
        int @vaobj = unchecked((int)a0);
        int @bindingindex = unchecked((int)a1);
        int @divisor = unchecked((int)a2);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
