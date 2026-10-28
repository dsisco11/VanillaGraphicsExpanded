using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUseProgram after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUseProgram")]
    internal static void ObserveUseProgram(uint a0)
    {
        int @program = unchecked((int)a0);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.Program, @program);
    }
    #endregion
}
