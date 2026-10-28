using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUniform1uiEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniform1uiEXT")]
    internal static void ObserveUniform1uiEXT(int a0, uint a1)
    {
        int @location = unchecked((int)a0);
        int @v0 = unchecked((int)a1);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), 1, false, false);
    }
    #endregion
}
