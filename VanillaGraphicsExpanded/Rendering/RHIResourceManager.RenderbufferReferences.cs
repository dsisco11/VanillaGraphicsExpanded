using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Certifies renderbuffer references through existing manager associations and snapshots.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Returns available original-allocation facts without acquisition or native discovery.</summary>
    internal static bool TryGetRenderbufferReferenceMetadata(GpuResource<GpuRenderbufferMetadata>? resource, out GpuRenderbufferMetadata data)
    {
        data = default;
        if (resource is null || !resource.HasMetadataSnapshot || !resource.IsValid) return false;
        var manager = resource.ResourceManager;
        if (manager?.GetResourceRegistration(resource) is not null && !manager.IsResourceMetadataAvailable(resource)) return false;
        data = resource.PublishedMetadata;
        return true;
    }

    /// <summary>Requires published facts and originating affinity before native use.</summary>
    internal static GpuRenderbufferMetadata RequireRenderbufferReferenceMetadata(GpuResource<GpuRenderbufferMetadata> resource)
    {
        resource.RequireOriginalContext();
        RequireWrapperMetadataAccess(resource);
        if (!TryGetRenderbufferReferenceMetadata(resource, out var data)) throw new InvalidOperationException("Renderbuffer metadata is unavailable or retired.");
        return data;
    }
    #endregion
}
