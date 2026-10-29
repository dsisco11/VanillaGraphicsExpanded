namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains query, fence and semaphore identity without discovering empty descriptors.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Registers returned query names without replacing stale live outputs.</summary>
    internal void ObserveQueryCreation(nint id, bool realized)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(GpuResourceKind.Query, id) is not null) return;
        var entry = RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, id);
        if (realized) PublishMetadata(entry, new GpuQueryMetadata(), GpuMetadataChange.None);
    }

    /// <summary>Establishes the empty schema only on the original reserved query after legal realization.</summary>
    internal void RealizeObservedQuery(nint id)
    {
        if (FindObservedResource(GpuResourceKind.Query, id) is GpuResourceMetadataEntry<GpuQueryMetadata> { IsRealized: false } entry)
            PublishMetadata(entry, new GpuQueryMetadata(), GpuMetadataChange.None);
    }

    /// <summary>Records a non-null pointer-width fence result with no status read.</summary>
    internal void ObserveSyncCreation(nint id)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(GpuResourceKind.Sync, id) is not null) return;
        var entry = RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        PublishMetadata(entry, new GpuSyncMetadata(), GpuMetadataChange.None);
    }

    /// <summary>Retains semaphore name reservation independently of opaque payload import.</summary>
    internal void ObserveSemaphoreGeneration(nint id)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(GpuResourceKind.Semaphore, id) is not null) return;
        RegisterCreatedResource<GpuSemaphoreMetadata>(GpuResourceKind.Semaphore, id);
    }

    /// <summary>Publishes only a caller-observed positive realization result, never a guessed import outcome.</summary>
    internal void ObserveSemaphoreRealization(nint id)
    {
        if (FindObservedResource(GpuResourceKind.Semaphore, id) is GpuResourceMetadataEntry<GpuSemaphoreMetadata> { IsRealized: false } entry)
            PublishMetadata(entry, new GpuSemaphoreMetadata(), GpuMetadataChange.None);
    }
    #endregion
}
