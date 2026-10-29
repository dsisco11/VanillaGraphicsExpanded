
namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines narrow external observation and geometry certification facts authorized by the manager.</summary>
internal partial interface IRHIResourceExecution
{
    /// <summary>Acquires explicitly requested facts in the original context without publishing metadata.</summary>
    bool IsFramebufferResource(nint id);
    /// <summary>Acquires explicitly requested facts in the original context without publishing metadata.</summary>
    bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata);
    /// <summary>Acquires explicitly requested facts in the original context without publishing metadata.</summary>
    bool ReadFramebufferField(nint id, GpuFramebufferMetadata previous, FramebufferSlot? slot, FramebufferMetadataField? parameter, out GpuFramebufferMetadata metadata);
    /// <summary>Acquires explicitly requested facts in the original context without publishing metadata.</summary>
    RHIGeometryContext CaptureGeometryContext(int array);
}
