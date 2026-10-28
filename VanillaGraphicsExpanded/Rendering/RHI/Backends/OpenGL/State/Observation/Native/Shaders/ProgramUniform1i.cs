using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glProgramUniform1i")]
    [InterceptOpenGL("glProgramUniform1iEXT")]
    internal static void ObserveProgramUniform1i(uint a0, int a1, int a2)
    {
        int @program = unchecked((int)a0);
        int @location = unchecked((int)a1);
        int @v0 = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, false);
    }
    #endregion
}
