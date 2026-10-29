namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes backend command results to canonical entries and their synchronized wrapper snapshots.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Discards uncertified local facts while retaining the immutable texture target.</summary>
    internal static void DiscardUntrackedMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is not null) return;
        if ((change & GpuMetadataChange.Storage) != 0 && resource is GpuTexture changedTexture)
            changedTexture.UntrackedStorageIdentity = new();
        // There is no availability registry in context-local tools. Empty descriptors cannot
        // masquerade as the previous allocation; object target alone remains a stable native fact.
        if (resource is GpuResource<GpuTextureMetadata> texture)
        {
            var target = resource is GpuTexture owner ? owner.TextureTarget
                : resource is GpuTextureView view ? view.Target
                : resource is GpuBufferView ? RHI.TextureDimension.Buffer : 0;
            texture.HandleMetadataChanged(default(GpuTextureMetadata) with { Target = target });
        }
        else resource.HandleMetadataChanged(default);
    }

    /// <summary>Withdraws usability when an acquisition or a grouped native command cannot certify a complete snapshot.</summary>
    internal static void InvalidateWrapperMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<T> entry)
        {
            entry.BeginRefresh(change);
            entry.NotifyProgramDependents(change, available: false);
            entry.NotifyImageDependents(suppressErrors: true);
            if (entry.Kind == GpuResourceKind.Texture) entry.Manager.SetTextureViewSourceAvailability(entry, false);
        }
        else
        {
            DiscardUntrackedMetadata(resource, change);
            PublishOwnerImages(resource, false);
        }
    }

    #endregion
}
