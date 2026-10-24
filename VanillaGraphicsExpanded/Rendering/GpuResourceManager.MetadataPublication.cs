namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates canonical refresh and typed snapshot publication on the originating context.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Refreshes once through the manager's reader; unavailable results cannot expose old snapshots.</summary>
    internal bool RefreshMetadata<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, GpuMetadataChange change,
        bool renewTextureAssociation = false)
        where TMetadata : struct
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        entry.BeginRefresh(change);
        change |= entry.PendingChange;
        bool finished = false;
        try
        {
            if (!CanReadProgramUnitMetadata(entry)) return false;
            var reader = entry.Reader;
            if (reader is null || !reader.TryRead(entry.Id, out var candidate)) return false;
            candidate = ReconcileFramebufferAttachmentModes(entry, candidate);
            // Retirement can run while the native read is in progress. Never publish captured
            // facts into an entry removed by retirement or replaced under the same numeric ID.
            AcquireBufferTextureBacking(entry, candidate, renewTextureAssociation);
            AcquireRenderbufferTextureBacking(entry, candidate, renewTextureAssociation);
            RequireMetadataContext();
            RequireLiveEntry(entry);
            ulong previousStorage = entry.StorageRevision;
            change = entry.Publish(candidate, change);
            PublishBufferTextureDependencies(entry, previousStorage);
            PublishRenderbufferTextureDependencies(entry, previousStorage);
            finished = true;
            return true;
        }
        finally
        {
            try { entry.NotifyImageDependents(suppressErrors: !finished); }
            finally { entry.NotifyProgramDependents(change, finished); }
        }
    }

    /// <summary>Publishes facts already proven by a validated native operation, without another native query.</summary>
    internal void PublishMetadata<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, TMetadata candidate, GpuMetadataChange change,
        bool renewTextureAssociation = false)
        where TMetadata : struct
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        entry.BeginRefresh(change);
        change |= entry.PendingChange;
        bool finished = false;
        try
        {
            if (!CanReadProgramUnitMetadata(entry))
                throw new System.InvalidOperationException("Program texture-unit representation requires a replacement executable.");
            candidate = ReconcileFramebufferAttachmentModes(entry, candidate);
            AcquireBufferTextureBacking(entry, candidate, renewTextureAssociation);
            AcquireRenderbufferTextureBacking(entry, candidate, renewTextureAssociation);
            RequireMetadataContext();
            RequireLiveEntry(entry);
            ulong previousStorage = entry.StorageRevision;
            change = entry.Publish(candidate, change);
            PublishBufferTextureDependencies(entry, previousStorage);
            PublishRenderbufferTextureDependencies(entry, previousStorage);
            finished = true;
        }
        finally
        {
            try { entry.NotifyImageDependents(suppressErrors: !finished); }
            finally { entry.NotifyProgramDependents(change, finished); }
        }
    }
    #endregion
}
