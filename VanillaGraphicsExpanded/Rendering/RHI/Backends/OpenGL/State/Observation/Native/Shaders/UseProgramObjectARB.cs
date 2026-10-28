using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUseProgramObjectARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUseProgramObjectARB")]
    internal static void ObserveUseProgramObjectARB(uint a0)
    {
        int @programObj = unchecked((int)a0);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.Program, @programObj);
    }
    #endregion
}
