using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayVertexAttribBindingEXT")]
    internal static void ObserveVertexArrayAttribBindingEXT(uint a0, uint a1, uint a2)
    {
        int @vaobj = unchecked((int)a0);
        int @attribindex = unchecked((int)a1);
        int @bindingindex = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!Current.PrepareVertexObservation(vaobj, true, true, RejectVertexIndex(attribindex) || RejectVertexIndex(bindingindex, true))) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, false);
    }
    #endregion
}
