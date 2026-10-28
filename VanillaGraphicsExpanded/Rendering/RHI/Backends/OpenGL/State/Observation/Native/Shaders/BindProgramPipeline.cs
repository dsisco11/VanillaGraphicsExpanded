using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindProgramPipeline after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindProgramPipeline")]
    internal static void ObserveBindProgramPipeline(uint a0)
    {
        int @pipeline = unchecked((int)a0);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.Pipeline, @pipeline);
    }
    #endregion
}
