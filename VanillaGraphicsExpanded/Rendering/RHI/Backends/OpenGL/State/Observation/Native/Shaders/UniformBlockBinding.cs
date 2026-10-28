using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUniformBlockBinding after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniformBlockBinding")]
    internal static void ObserveUniformBlockBinding(uint a0, uint a1, uint a2)
    {
        int @program = unchecked((int)a0);
        int @uniformBlockIndex = unchecked((int)a1);
        int @uniformBlockBinding = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.UniformBlock, unchecked((int)@uniformBlockIndex));
    }
    #endregion
}
