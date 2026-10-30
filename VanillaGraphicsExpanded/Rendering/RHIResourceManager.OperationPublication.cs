namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies existing resource-specific delivery and dependency policies at explicit operation boundaries.</summary>
internal sealed partial class RHIResourceManager
{
    #region Private
    /// <summary>Delivers a local result and advances texture storage identity only for storage changes.</summary>
    private static RHIResourceChange DeliverUntrackedOperationResult<T>(GpuResource<T> resource, T candidate, GpuMetadataChange effects) where T : struct
    {
        effects |= resource.PendingMetadataEffects;
        var reasons = !resource.HasAssignedMetadata || !System.Collections.Generic.EqualityComparer<T>.Default.Equals(resource.PublishedMetadata, candidate)
            ? ResourceChangeKind.Metadata : ResourceChangeKind.None;
        if (!resource.HasMetadataSnapshot) reasons |= ResourceChangeKind.Availability;
        if ((effects & GpuMetadataChange.Storage) != 0) reasons |= ResourceChangeKind.Storage;
        if ((effects & GpuMetadataChange.Executable) != 0) reasons |= ResourceChangeKind.Executable;
        if ((effects & GpuMetadataChange.Storage) != 0 && resource is GpuTexture texture)
            texture.UntrackedStorageIdentity = new();
        resource.AssignMetadataSnapshot(candidate);
        return new(reasons, true, 0, 0, resource);
    }

    #endregion
}
