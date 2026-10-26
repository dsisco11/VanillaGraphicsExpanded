using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains managed immutable-view dependencies without putting registry entry references on wrappers.</summary>
internal sealed partial class GpuResourceManager
{
    private readonly ConditionalWeakTable<GpuTextureView, GpuResourceRegistration> textureViewSources = new();

    #region Internal API
    /// <summary>Resolves managed view chains to their original storage name for existing feedback checks.</summary>
    internal static int TextureViewStorageName(GpuTextureView view)
        => view.Source is GpuTextureView parent ? TextureViewStorageName(parent) : view.SourceTextureId;

    /// <summary>Captures source provenance after the separate view has completed manager handoff.</summary>
    internal static void EstablishTextureViewSource(GpuTextureView view, GpuResource<GpuTextureMetadata> source)
    {
        view.SetSource(source, TextureStorageRevision(source), TextureStorageIdentity(source));
        view.ResourceManager?.RecordTextureViewSource(view, source);
    }
    /// <summary>Reads the existing registry storage revision without another captured token.</summary>
    internal static ulong TextureStorageRevision(GpuResource<GpuTextureMetadata> resource) => resource.ResourceManager?.GetResourceRegistration(resource)?.StorageRevision ?? 0;
    /// <summary>Uses the existing owner certificate or immutable view identity for context-local tools.</summary>
    internal static object TextureStorageIdentity(GpuResource<GpuTextureMetadata> resource) => resource is GpuTexture texture ? texture.UntrackedStorageIdentity : resource;
    /// <summary>Captures the source's original native allocation when a managed immutable view is constructed.</summary>
    internal void RecordTextureViewSource(GpuTextureView view, GpuResource<GpuTextureMetadata> source)
    {
        if (GetResourceRegistration(source) is { } original) textureViewSources.Add(view, original);
    }

    /// <summary>Requires the original source association and its available snapshot, or defers to untracked local validation.</summary>
    internal bool? IsTextureViewSourceCurrent(GpuTextureView view, GpuResource<GpuTextureMetadata> source)
    {
        if (!textureViewSources.TryGetValue(view, out var original)) return null;
        return ReferenceEquals(original, GetResourceRegistration(source)) && IsResourceMetadataAvailable(source);
    }
    #endregion
}
