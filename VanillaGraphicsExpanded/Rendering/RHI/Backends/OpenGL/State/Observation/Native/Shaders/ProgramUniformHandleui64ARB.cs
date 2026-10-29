using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glProgramUniformHandleui64ARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glProgramUniformHandleui64ARB")]
    internal static void ObserveProgramUniformHandleui64ARB(uint a0, int a1, ulong a2)
    {
        int @program = unchecked((int)a0);
        int @location = unchecked((int)a1);
        long @value = unchecked((long)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, true);
    }
    #endregion
}
