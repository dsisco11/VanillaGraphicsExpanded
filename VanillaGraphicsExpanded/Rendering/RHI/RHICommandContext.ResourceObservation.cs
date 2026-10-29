
namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes manager-authorized observation through the admitted execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata) => backend.ReadFramebufferMetadata(id, out metadata);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal RHIGeometryContext CaptureGeometryContext(int array) => backend.CaptureGeometryContext(array);
    #endregion
}
