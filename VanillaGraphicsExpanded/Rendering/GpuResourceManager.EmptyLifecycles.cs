namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains empty-schema allocations without discovering unrepresented native configuration.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Consumes a factory output without replacing a surviving original allocation.</summary>
    internal bool ObserveEmptyCreation<T>(GpuResourceKind kind, int id, bool realized) where T : struct
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(kind, id) is not null) return false;
        var entry = RegisterCreatedResource<T>(kind, id);
        if (realized) PublishMetadata(entry, default(T), GpuMetadataChange.None);
        return true;
    }

    /// <summary>Realizes only an original generated name; unknown names never manufacture usable defaults.</summary>
    internal bool RealizeEmptyResource<T>(GpuResourceKind kind, int id) where T : struct
    {
        if (FindObservedResource(kind, id) is not GpuResourceMetadataEntry<T> entry) return false;
        if (entry.InitialDiscovery != GpuInitialDiscovery.None || entry.PendingChange != GpuMetadataChange.None) return false;
        if (!entry.IsRealized) PublishMetadata(entry, default(T), GpuMetadataChange.None);
        return entry.IsUsable;
    }
    #endregion
}
