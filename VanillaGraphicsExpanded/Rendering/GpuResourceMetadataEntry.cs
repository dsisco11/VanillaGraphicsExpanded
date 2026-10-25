using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns canonical typed metadata and concurrent weak recipients for one native resource.</summary>
internal sealed class GpuResourceMetadataEntry<TMetadata> : GpuResourceRegistration where TMetadata : struct
{
    private readonly ConcurrentDictionary<WeakReference<GpuResource>, bool> recipients = new();
    private GpuMetadataChange pendingChange;
    /// <summary>Prevents recursive native acquisition while a complete result is being recovered or committed.</summary>
    internal bool CompletionInProgress { get; set; }
    /// <summary>Rejects a stale outer candidate after a caught reentrant mutation attempt.</summary>
    internal bool CompletionInterrupted { get; set; }
    /// <summary>Blocks acquisition while an explicit operation constructs a complete result.</summary>
    internal bool OperationInProgress { get; set; }

    #region Internal API
    #region Recipient initialization
    internal TMetadata Metadata { get; private set; }
    internal IGpuResourceMetadataReader<TMetadata>? Reader { get; set; }
    /// <summary>Retains unfinished effects so recovery cannot misclassify an interrupted executable change.</summary>
    internal GpuMetadataChange PendingChange => pendingChange;
    internal override int AssociationCount => recipients.Count;

    /// <summary>Creates an initially unavailable entry for a realized or reserved native name.</summary>
    internal GpuResourceMetadataEntry(GpuResourceManager manager, GpuResourceKind kind, nint id,
        IGpuResourceMetadataReader<TMetadata>? reader) : base(manager, kind, id)
    {
        Reader = reader;
    }

    /// <summary>Registers a weak recipient and assigns initial data without installing wrapper tracking state.</summary>
    internal void Associate(GpuResource resource)
    {
        PruneAssociations();
        Manager.RecordResourceAssociation(this, resource);
        if (ContainsRecipient(resource))
            throw new InvalidOperationException("The resource is already associated with this registration.");
        var recipient = new WeakReference<GpuResource>(resource);
        recipients.TryAdd(recipient, false);
        if (IsUsable && TryGetRecipient(recipient, out _) && AssignSnapshot(resource))
            recipients.TryUpdate(recipient, true, false);
        // Retirement can finish its sweep before admission. Reject the late slot without
        // requiring a wrapper-local state machine or allowing the entry to become live again.
        if (State != GpuResourceRegistrationState.Live || Manager.IsMetadataRegistryRetired)
        {
            recipients.TryRemove(recipient, out _);
            throw new InvalidOperationException("The original resource registration is no longer live.");
        }
    }

    /// <inheritdoc />
    internal override void AssociateImage(GpuResource<GpuImageMetadata> resource) => Associate(resource);

    /// <inheritdoc />
    internal override bool ContainsRecipient(GpuResource resource)
    {
        foreach (var recipient in recipients)
            if (recipient.Key.TryGetTarget(out var target) && ReferenceEquals(target, resource)) return true;
        return false;
    }

    /// <inheritdoc />
    internal override bool HasSnapshot(GpuResource resource)
    {
        foreach (var recipient in recipients)
            if (recipient.Value && recipient.Key.TryGetTarget(out var target) && ReferenceEquals(target, resource)) return true;
        return false;
    }
    #endregion

    #region Snapshot publication
    /// <summary>Invalidates retained framebuffer helper state after external native configuration observation.</summary>
    internal void NotifyFramebufferDependents(GpuResource? origin = null)
    {
        foreach (var recipient in recipients.ToArray())
            if (TryGetRecipient(recipient.Key, out var resource) && !ReferenceEquals(resource, origin)
                && resource is GpuFramebuffer framebuffer)
                framebuffer.OnNativeConfigurationChanged();
    }

    /// <summary>Marks canonical metadata unavailable before reading a replacement candidate.</summary>
    internal void BeginRefresh(GpuMetadataChange change)
    {
        pendingChange |= change;
        MetadataAvailable = false;
        PruneAssociations();
    }

    /// <summary>Assigns recipient snapshots before publishing canonical availability and revisions.</summary>
    internal GpuMetadataChange Publish(TMetadata candidate, GpuMetadataChange change)
    {
        change |= pendingChange;
        MetadataAvailable = false;
        Metadata = candidate;
        List<Exception>? failures = null;
        foreach (var recipient in recipients.ToArray())
        {
            if (!TryGetRecipient(recipient.Key, out var resource)) continue;
            bool assigned = false;
            try { assigned = AssignSnapshot(resource); }
            catch (Exception error) { (failures ??= new()).Add(error); }
            // Conditional update cannot recreate a slot removed concurrently by disposal.
            recipients.TryUpdate(recipient.Key, assigned, recipient.Value);
        }
        if (MetadataRevision == 0 || change != GpuMetadataChange.None) MetadataRevision++;
        if ((change & GpuMetadataChange.Storage) != 0) StorageRevision++;
        pendingChange = GpuMetadataChange.None;
        MetadataAvailable = true;
        // Entry lifecycle gates availability even if retirement overlaps these assignments.
        if (failures is not null) throw new AggregateException("GPU metadata publication failed.", failures);
        return change;
    }

    /// <summary>Refreshes every program recipient's derived preparation after canonical snapshot delivery.</summary>
    internal void NotifyProgramDependents(GpuMetadataChange change, bool available)
    {
        if (Kind != GpuResourceKind.Program) return;
        List<Exception>? failures = null;
        foreach (var recipient in recipients.ToArray())
        {
            if (!TryGetRecipient(recipient.Key, out var resource)) continue;
            try
            {
                if (resource is GpuProgramObject program) program.OnNativeProgramMetadataChanged(change, available);
                else if (resource is GpuComputePipeline compute) compute.OnNativeProgramMetadataChanged(change, available);
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        if (available && failures is not null)
            throw new AggregateException("GPU program preparation notification failed.", failures);
    }

    /// <summary>Invalidates existing image consumers after all snapshots have been assigned or withdrawn.</summary>
    internal void NotifyImageDependents(bool suppressErrors)
    {
        List<Exception>? failures = null;
        foreach (var recipient in recipients.ToArray())
        {
            if (!TryGetRecipient(recipient.Key, out var resource) || resource is not GpuFramebufferAttachment image) continue;
            try { image.NotifyDependents(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        // A reader/publication exception remains the primary failure. All callbacks still run.
        if (!suppressErrors && failures is not null)
            throw new AggregateException("GPU image dependency notification failed.", failures);
    }
    #endregion

    #region Recipient removal
    /// <inheritdoc />
    internal override void PruneAssociations()
    {
        foreach (var recipient in recipients)
            if (!recipient.Key.TryGetTarget(out var resource) || resource.IsDisposed)
                recipients.TryRemove(recipient.Key, out _);
    }

    /// <inheritdoc />
    internal override void Unregister(GpuResource resource)
    {
        foreach (var recipient in recipients)
            if (recipient.Key.TryGetTarget(out var target) && ReferenceEquals(target, resource))
                recipients.TryRemove(recipient.Key, out _);
    }

    /// <inheritdoc />
    internal override void ClearAssociations() => recipients.Clear();
    #endregion
    #endregion

    #region Private
    /// <summary>Resolves live wrappers whose weak slot still belongs to this manager entry.</summary>
    private bool TryGetRecipient(WeakReference<GpuResource> recipient, out GpuResource resource)
    {
        if (recipients.ContainsKey(recipient) && recipient.TryGetTarget(out resource!) && !resource.IsDisposed) return true;
        resource = null!;
        return false;
    }

    /// <summary>Assigns a typed snapshot or projects a selected image from canonical backing data.</summary>
    private bool AssignSnapshot(GpuResource resource)
    {
        if (resource is GpuResource<TMetadata> typed)
        {
            typed.HandleMetadataChanged(Metadata);
            return true;
        }
        return Manager.AssignImageMetadata(this, resource);
    }
    #endregion
}
