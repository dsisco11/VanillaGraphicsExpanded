namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes explicit external window registration through the backend query contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Reads native surface facts for the original window publication boundary.</summary>
    internal GpuFramebuffer.SurfaceMetadata ReadWindowSurface() => backend.ReadWindowSurface();
    #endregion
}
