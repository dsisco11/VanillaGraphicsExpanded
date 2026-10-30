using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Certifies buffer references through existing manager associations and snapshots.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Returns available original-allocation facts without acquisition or native discovery.</summary>
    internal static bool TryGetBufferReferenceMetadata(GpuResource<GpuBufferMetadata>? resource, out GpuBufferMetadata data)
    {
        data = default;
        if (resource is null || !resource.HasMetadataSnapshot || !resource.IsValid) return false;
        var manager = resource.ResourceManager;
        if (manager?.GetResourceRegistration(resource) is not null && !manager.IsResourceMetadataAvailable(resource)) return false;
        data = resource.PublishedMetadata;
        return data.Capacity >= 0 && Enum.IsDefined(data.Usage) && data.Usage != RHI.BufferUsage.Undefined;
    }

    /// <summary>Requires published facts and originating affinity before native use.</summary>
    internal static GpuBufferMetadata RequireBufferReferenceMetadata(GpuResource<GpuBufferMetadata> resource)
    {
        resource.RequireOriginalContext();
        RequireWrapperMetadataAccess(resource);
        if (!TryGetBufferReferenceMetadata(resource, out var data)) throw new InvalidOperationException("Buffer metadata is unavailable or retired.");
        return data;
    }
    #endregion
}
