using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Certifies sampler references through existing manager associations and snapshots.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Returns available original-allocation facts without acquisition or native discovery.</summary>
    internal static bool TryGetSamplerReferenceMetadata(GpuResource<GpuSamplerMetadata>? resource, out GpuSamplerMetadata data)
    {
        data = default;
        if (resource is null || !resource.IsValid) return false;
        var manager = resource.ResourceManager;
        if (manager?.GetResourceRegistration(resource) is not null && !manager.IsResourceMetadataAvailable(resource)) return false;
        data = resource.PublishedMetadata;
        return true;
    }

    /// <summary>Requires published facts and originating affinity before native use.</summary>
    internal static GpuSamplerMetadata RequireSamplerReferenceMetadata(GpuResource<GpuSamplerMetadata> resource)
    {
        resource.RequireOriginalContext();
        RequireWrapperMetadataAccess(resource);
        if (!TryGetSamplerReferenceMetadata(resource, out var data)) throw new InvalidOperationException("Sampler metadata is unavailable or retired.");
        return data;
    }
    #endregion
}
