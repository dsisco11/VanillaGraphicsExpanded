using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Restores an executed native executable selector without activating an asset or replaying its mutable inputs.</summary>
internal static class OpenGLProgramSelection
{
    #region Internal API
    /// <summary>Changes only unknown/different selection and publishes cache knowledge after native return.</summary>
    internal static void Select(OpenGlStateCache cache, int program)
    {
        if (!cache.PrepareProgramSelection(program)) return;
        var origin=GpuResource.CaptureCreationContext(GpuResourceKind.Program);
        OpenGLDriverCalls.UseProgram(program);
        if(!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new System.InvalidOperationException("Program selection returned in a foreign context.");
        cache.NotifyProgramBound(program);
    }
    #endregion
}
