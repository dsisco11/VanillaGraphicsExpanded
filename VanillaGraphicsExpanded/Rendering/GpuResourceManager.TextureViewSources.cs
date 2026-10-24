using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains managed immutable-view dependencies without putting registry entry references on wrappers.</summary>
internal sealed partial class GpuResourceManager
{
    private readonly ConditionalWeakTable<GpuTextureView, GpuResourceRegistration> textureViewSources = new();

    #region Internal API
    /// <summary>Captures the source's original native allocation when a managed immutable view is constructed.</summary>
    internal void RecordTextureViewSource(GpuTextureView view, GpuTexture source)
    {
        if (GetResourceRegistration(source) is { } original) textureViewSources.Add(view, original);
    }

    /// <summary>Requires the original source association and its available snapshot, or defers to untracked local validation.</summary>
    internal bool? IsTextureViewSourceCurrent(GpuTextureView view, GpuTexture source)
    {
        if (!textureViewSources.TryGetValue(view, out var original)) return null;
        return ReferenceEquals(original, GetResourceRegistration(source)) && IsResourceMetadataAvailable(source);
    }
    #endregion
}
