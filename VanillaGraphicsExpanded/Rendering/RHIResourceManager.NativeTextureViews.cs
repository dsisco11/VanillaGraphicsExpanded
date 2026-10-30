namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains original storage dependencies for native views independently of wrapper class.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Records a newly established view's original backing, including nested views.</summary>
    internal void RecordNativeTextureView(GpuResourceRegistration view, GpuResourceRegistration source)
    {
        RequireLiveEntry(view);
        RequireLiveEntry(source);
        RecordTextureBacking(view, source, source.StorageRevision);
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
        // Withdrawal propagates through direct backing edges, including nested views.
        foreach (var dependent in GetResourceDependents(source))
        {
            if (dependent is not GpuResourceMetadataEntry<GpuTextureMetadata> view || !view.HasCommittedMetadata
                || !textureBackingIdentities.TryGetValue(view, out var backing)
                || !ReferenceEquals(backing.Source, source) || !backing.StorageRevision.HasValue) continue;
            view.Withdraw();
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
