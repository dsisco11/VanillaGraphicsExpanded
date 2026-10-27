namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Provides private external surface discovery independently of portable render-pass inputs.</summary>
internal partial interface IRHIResourceExecution
{
    /// <summary>Reads the current window surface's format and sample facts once at publication.</summary>
    GpuFramebuffer.SurfaceMetadata ReadWindowSurface();
}
