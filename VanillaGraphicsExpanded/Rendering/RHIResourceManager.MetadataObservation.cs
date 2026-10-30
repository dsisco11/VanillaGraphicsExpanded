namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes backend command results to canonical entries and their synchronized wrapper snapshots.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Discards uncertified local facts while retaining the immutable texture target.</summary>
    internal static void DiscardUntrackedMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is not null) return;
        if ((change & GpuMetadataChange.Storage) != 0 && resource is GpuTexture changedTexture)
            changedTexture.UntrackedStorageIdentity = new();
        // Keep the last supplied facts for recovery classification; ordinary local properties
        // expose an unavailable snapshot until explicit publication restores it.
        resource.WithdrawMetadataSnapshot(change);
    }

    /// <summary>Withdraws usability when an acquisition or a grouped native command cannot certify a complete snapshot.</summary>
    internal static void InvalidateWrapperMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<T> entry)
        {
            using var delivery = entry.Manager.BeginChangeDelivery();
            entry.Withdraw(change);
            if (entry.Kind == GpuResourceKind.Texture) entry.Manager.WithdrawResourceDependencies(entry, GpuMetadataChange.Metadata);
        }
        else
        {
            bool available = resource.HasMetadataSnapshot;
            DiscardUntrackedMetadata(resource, change);
            PublishOwnerImages(resource, false);
            if (available) resource.OnResourceChanged(new(ResourceChangeKind.Availability, false, 0, 0));
        }
    }

    /// <summary>Completes an owned local lifetime without inventing a native registration or blocking deletion.</summary>
    internal static void CompleteLocalRetirement(GpuResource resource)
    {
        try { PublishOwnerImages(resource, false); } catch { }
        // The owner is already disposed. Affinity was checked by its existing disposal policy;
        // observer failure cannot prevent the native owner from completing cleanup.
        try { resource.OnResourceChanged(new(ResourceChangeKind.Retired | ResourceChangeKind.Availability, false, 0, 0, resource)); }
        catch { }
    }
    #endregion
}
