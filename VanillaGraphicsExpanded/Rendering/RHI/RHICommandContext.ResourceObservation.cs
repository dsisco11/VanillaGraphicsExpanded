
namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes manager-authorized observation through the admitted execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool IsFramebufferResource(nint id) => backend.IsFramebufferResource(id);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata) => backend.ReadFramebufferMetadata(id, out metadata);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadFramebufferField(nint id, GpuFramebufferMetadata previous, FramebufferSlot? slot, FramebufferMetadataField? parameter, out GpuFramebufferMetadata metadata) => backend.ReadFramebufferField(id, previous, slot, parameter, out metadata);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal RHIGeometryContext CaptureGeometryContext(int array) => backend.CaptureGeometryContext(array);
    #endregion
}
