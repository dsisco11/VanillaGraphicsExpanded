namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates canonical refresh and typed snapshot publication on the originating context.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Refreshes once through the manager's reader; unavailable results cannot expose old snapshots.</summary>
    internal bool RefreshMetadata<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, GpuMetadataChange change)
        where TMetadata : struct
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        entry.BeginRefresh(change);
        var reader = entry.Reader;
        if (reader is null || !reader.IsResource(entry.Id) || !reader.TryRead(entry.Id, out var candidate)) return false;
        // Retirement can run while the native read is in progress. Never publish the captured
        // facts into an entry removed by that retirement or replaced under the same numeric ID.
        RequireMetadataContext();
        RequireLiveEntry(entry);
        entry.Publish(candidate, change);
        return true;
    }

    /// <summary>Publishes facts already proven by a validated native operation, without another native query.</summary>
    internal void PublishMetadata<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, TMetadata candidate, GpuMetadataChange change)
        where TMetadata : struct
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        entry.BeginRefresh(change);
        entry.Publish(candidate, change);
    }
    #endregion
}