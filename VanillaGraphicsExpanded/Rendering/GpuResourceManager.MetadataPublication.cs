using VanillaGraphicsExpanded.Rendering.RHI;
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
        if (entry.OperationInProgress) throw new System.InvalidOperationException("An operation owns this resource result.");
        bool interruptedExecutable = entry.Kind == GpuResourceKind.Program && entry.CompletionInterrupted;
        BeginMetadataCompletion(entry, change);
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
            RequireUninterruptedCompletion(entry);
            ulong previousStorage = entry.StorageRevision;
            change = entry.Publish(candidate, change);
            PublishBufferTextureDependencies(entry, previousStorage);
            PublishRenderbufferTextureDependencies(entry, previousStorage);
            if (entry.Kind == GpuResourceKind.Texture && entry.StorageRevision != previousStorage)
                WithdrawChangedTextureViews(entry);
            else if (entry.Kind == GpuResourceKind.Texture) SetTextureViewSourceAvailability(entry, true);
            finished = true;
            return true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            if (!finished && interruptedExecutable) entry.CompletionInterrupted = true;
            try { entry.NotifyImageDependents(suppressErrors: !finished); }
            finally { entry.NotifyProgramDependents(change, finished); }
        }
    }

    /// <summary>Publishes facts already proven by a validated native operation, without another native query.</summary>
    internal void PublishMetadata<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, TMetadata candidate, GpuMetadataChange change,
        bool renewTextureAssociation = false)
        where TMetadata : struct
        => PublishMetadataCore(entry, candidate, change, renewTextureAssociation, null);

    /// <summary>Commits a supplied result, allowing only an explicitly authorized operation past its execution guard.</summary>
    private void PublishMetadataCore<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, TMetadata candidate,
        GpuMetadataChange change, bool renewTextureAssociation, IRHIResourceOperation? operation)
        where TMetadata : struct
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (entry.OperationInProgress && operation is null) throw new System.InvalidOperationException("An operation owns this resource result.");
        RHICommandContext.RequireResourceOperationAvailable(entry.Kind, entry.Id, operation);
        bool interruptedExecutable = entry.Kind == GpuResourceKind.Program && entry.CompletionInterrupted;
        BeginMetadataCompletion(entry, change);
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
            RequireUninterruptedCompletion(entry);
            ulong previousStorage = entry.StorageRevision;
            change = entry.Publish(candidate, change);
            PublishBufferTextureDependencies(entry, previousStorage);
            PublishRenderbufferTextureDependencies(entry, previousStorage);
            if (entry.Kind == GpuResourceKind.Texture && entry.StorageRevision != previousStorage)
                WithdrawChangedTextureViews(entry);
            else if (entry.Kind == GpuResourceKind.Texture) SetTextureViewSourceAvailability(entry, true);
            finished = true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            if (!finished && interruptedExecutable) entry.CompletionInterrupted = true;
            try { entry.NotifyImageDependents(suppressErrors: !finished); }
            finally { entry.NotifyProgramDependents(change, finished); }
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects same-entry reentrant completion and poisons the outer candidate even when the caller catches it.</summary>
    private static void BeginMetadataCompletion<T>(GpuResourceMetadataEntry<T> entry, GpuMetadataChange change) where T : struct
    {
        if (entry.CompletionInProgress)
        {
            entry.CompletionInterrupted = true;
            entry.BeginRefresh(change);
            throw new System.InvalidOperationException("Metadata completion is already in progress for this resource.");
        }
        entry.CompletionInProgress = true;
        entry.CompletionInterrupted = false;
    }

    /// <summary>Prevents a reader or dependency callback from publishing an obsolete outer result.</summary>
    private static void RequireUninterruptedCompletion<T>(GpuResourceMetadataEntry<T> entry) where T : struct
    {
        if (entry.CompletionInterrupted)
            throw new System.InvalidOperationException("Metadata completion was interrupted by a reentrant update.");
    }
    #endregion
}
