using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniform1i")]
    [InterceptOpenGL("glUniform1iARB")]
    internal static void ObserveUniform1i(int a0, int a1)
    {
        int @location = unchecked((int)a0);
        int @v0 = unchecked((int)a1);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), 1, false, false);
    }
    #endregion
}
