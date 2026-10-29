namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Certifies auxiliary consumption using existing original associations and canonical availability.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Checks already-published usability without discovery, registration or resource-specific caches.</summary>
    internal static bool IsAuxiliaryReferenceUsable(GpuResource? resource)
    {
        if (resource is null || resource.IsDisposed || resource.ResourceId == 0) return false;
        RequireWrapperMetadataAccess(resource);
        resource.RequireOriginalContext();
        if (!resource.IsValid) return false;
        if (resource is GpuSemaphore { ImportRequested: false }
            && resource.ResourceManager?.GetResourceRegistration(resource) is null) return false;
        return resource.ResourceManager is not { } manager || !manager.CanAcquireWrapperMetadata(resource)
            || manager.IsResourceMetadataAvailable(resource);
    }
    #endregion
}
