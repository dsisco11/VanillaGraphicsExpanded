using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains explicit buffer-texture provenance without adding native-name or metadata caches.</summary>
internal sealed partial class GpuResourceManager
{
    private static readonly ConditionalWeakTable<GpuBufferView, GpuResource<GpuBufferMetadata>> bufferTextureSources = new();

    #region Internal API
    /// <summary>Retains the explicitly supplied backing wrapper for local-tool dependency validation.</summary>
    internal static void EstablishBufferTextureSource(GpuBufferView view, GpuResource<GpuBufferMetadata> source)
    {
        // Registered resources already use the canonical dependency graph, including reassociation.
        if (view.ResourceManager?.GetResourceRegistration(view) is null) bufferTextureSources.Add(view, source);
    }

    #endregion

    #region Private

    /// <summary>Checks a supplied original backing identity and range without rediscovering a reused native name.</summary>
    private static bool IsBufferTextureSourceCurrent(GpuBufferView view, GpuTextureMetadata metadata)
    {
        if (view.ResourceManager?.GetResourceRegistration(view) is not null || !bufferTextureSources.TryGetValue(view, out var source)) return true;
        if (!TryGetBufferReferenceMetadata(source, out var backing) || metadata.Buffer is not { } range || source.ResourceId != range.BufferId) return false;
        return range.WholeBuffer || range.Offset >= 0 && range.Size > 0 && range.Offset <= backing.Capacity && range.Size <= backing.Capacity - range.Offset;
    }
    #endregion
}
