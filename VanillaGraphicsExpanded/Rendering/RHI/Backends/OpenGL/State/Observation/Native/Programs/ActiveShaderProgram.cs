using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glActiveShaderProgram after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glActiveShaderProgram")]
    internal static void ObserveActiveShaderProgram(uint a0, uint a1)
    {
        OpenGlStateCache.ObserveProgramSelection(OpenGlStateCache.NativeProgramSelector.PipelineUniform, unchecked((int)a1), unchecked((int)a0));
    }
    #endregion
}
