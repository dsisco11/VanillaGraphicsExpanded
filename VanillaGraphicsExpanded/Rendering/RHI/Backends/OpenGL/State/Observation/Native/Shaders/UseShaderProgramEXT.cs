using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUseShaderProgramEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUseShaderProgramEXT")]
    internal static void ObserveUseShaderProgramEXT(int a0, uint a1)
    {
        ExtSeparateShaderObjects @type = unchecked((ExtSeparateShaderObjects)a0);
        int @program = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.Stage, @program, stage: (int)@type);
    }
    #endregion
}
