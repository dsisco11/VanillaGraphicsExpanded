namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains original storage dependencies for native views independently of wrapper class.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Withdraws dependent projections when a native texture result is uncertain.</summary>
    internal void NotifyTextureViewSourceUnavailable(GpuResourceRegistration source)
        => SetTextureViewSourceAvailability(source, false);

    /// <summary>Records a newly established view's original backing, including nested views.</summary>
    internal void RecordNativeTextureView(GpuResourceRegistration view, GpuResourceRegistration source)
    {
        RequireLiveEntry(view);
        RequireLiveEntry(source);
        textureBackingIdentities.Remove(view);
        textureBackingIdentities.Add(view, new(source, source.StorageRevision));
    }

    /// <summary>Resolves original shared storage for owned views and ordinary wrappers borrowing native views.</summary>
    internal static int TextureStorageName(GpuResource? resource)
    {
        if (resource is null) return 0;
        if (resource.ResourceManager is { } manager && manager.GetResourceRegistration(resource) is { } entry)
        {
            while (manager.textureBackingIdentities.TryGetValue(entry, out var dependency) && dependency.StorageRevision.HasValue) entry = dependency.Source;
            return (int)entry.Id;
        }
        return resource is GpuTextureView view ? TextureViewStorageName(view) : (int)resource.ResourceId;
    }

    /// <summary>Returns cumulative view coordinates from canonical facts, including projected attachment aliases.</summary>
    internal static GpuTextureViewMetadata? TextureStorageSelection(GpuResource resource)
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<GpuTextureMetadata> entry)
            return entry.Metadata.View;
        return resource is GpuResource<GpuTextureMetadata> texture ? texture.PublishedMetadata.View : null;
    }
    #endregion

    #region Private
    /// <summary>Withdraws views whose original source allocation has changed or retired.</summary>
    private void WithdrawChangedTextureViews(GpuResourceRegistration source)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is not GpuResourceMetadataEntry<GpuTextureMetadata> view || !view.IsUsable) continue;
            var current = (GpuResourceRegistration)view;
            while (textureBackingIdentities.TryGetValue(current, out var dependency) && dependency.StorageRevision.HasValue)
            {
                if (ReferenceEquals(dependency.Source, source))
                {
                    view.BeginRefresh(GpuMetadataChange.Metadata);
                    view.SetImageRecipientAvailability(false);
                    break;
                }
                current = dependency.Source;
            }
        }
    }

    /// <summary>Propagates a source's temporary availability without changing view storage identity.</summary>
    private void SetTextureViewSourceAvailability(GpuResourceRegistration source, bool available)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is not GpuResourceMetadataEntry<GpuTextureMetadata> view || !view.IsUsable) continue;
            var current = (GpuResourceRegistration)view;
            while (textureBackingIdentities.TryGetValue(current, out var dependency) && dependency.StorageRevision.HasValue)
            {
                if (ReferenceEquals(dependency.Source, source))
                {
                    view.SetImageRecipientAvailability(available && IsNativeTextureViewSourceAvailable(view));
                    break;
                }
                current = dependency.Source;
            }
        }
    }

    /// <summary>Rejects views after original source replacement, withdrawal or context retirement.</summary>
    private bool IsNativeTextureViewSourceAvailable(GpuResourceRegistration entry)
    {
        if (!textureBackingIdentities.TryGetValue(entry, out var dependency) || !dependency.StorageRevision.HasValue) return true;
        var source = dependency.Source;
        return IsRegistered(source) && source.IsUsable && source.StorageRevision == dependency.StorageRevision
            && IsNativeTextureViewSourceAvailable(source);
    }
    #endregion
}
