using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glActiveProgramEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glActiveProgramEXT")]
    internal static void ObserveActiveProgramEXT(uint a0)
    {
        int @program = unchecked((int)a0);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.ContextUniform, @program);
    }
    #endregion
}
