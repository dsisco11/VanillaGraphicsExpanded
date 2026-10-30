using System.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns canonical typed metadata and concurrent weak recipients for one native resource.</summary>
internal sealed class GpuResourceMetadataEntry<TMetadata> : GpuResourceRegistration where TMetadata : struct
{
    private readonly ConcurrentDictionary<WeakReference<GpuResource>, bool> recipients = new();
    private GpuMetadataChange pendingChange;
    private bool publishedAvailable;
    private Action? retirementDelivery;
    /// <summary>Distinguishes committed facts from revision changes caused by availability or retirement.</summary>
    internal bool HasCommittedMetadata { get; private set; }
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
    internal GpuResourceMetadataEntry(RHIResourceManager manager, GpuResourceKind kind, nint id,
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
    /// <summary>Marks canonical metadata unavailable before reading a replacement candidate.</summary>
    internal void BeginRefresh(GpuMetadataChange change, bool fromBacking = false)
    {
        lock (recipients)
        {
            if (State != GpuResourceRegistrationState.Live) return;
            if (!fromBacking) Manager.ClearDependencyWithdrawal(this);
            pendingChange |= change;
            MetadataAvailable = false;
            PruneAssociations();
        }
    }

    /// <summary>Retains supplied initial facts privately while an unknown EXT identity still needs discovery.</summary>
    internal void RetainInitialCandidate(TMetadata candidate)
    {
        if (IsUsable || HasCommittedMetadata || InitialDiscovery != GpuInitialDiscovery.ImplicitCreation)
            throw new InvalidOperationException("Only an unresolved initial EXT identity can retain supplied facts.");
        Metadata = candidate;
    }

    /// <summary>Discards unpublished supplied facts after a later potentially accepted effect makes them uncertain.</summary>
    internal void DiscardInitialCandidate()
    {
        if (!IsUsable && !HasCommittedMetadata)
            Metadata = Metadata is GpuTextureMetadata texture
                ? (TMetadata)(object)(texture with { Buffer = null }) : default;
    }

    /// <summary>Commits facts and revisions, assigns snapshots, then schedules one classified transition.</summary>
    internal void Publish(TMetadata candidate, GpuMetadataChange effects, GpuResource? origin = null, bool backingIdentityChanged = false)
    {
        using var delivery = Manager.BeginChangeDelivery();
        lock (recipients)
        {
            if (State != GpuResourceRegistrationState.Live || Manager.IsMetadataRegistryRetired)
                throw new InvalidOperationException("The original resource retired before snapshot commit.");
            effects |= pendingChange;
            var reasons = backingIdentityChanged || !HasCommittedMetadata || !EqualityComparer<TMetadata>.Default.Equals(Metadata, candidate)
                ? ResourceChangeKind.Metadata : ResourceChangeKind.None;
            if (!publishedAvailable) reasons |= ResourceChangeKind.Availability;
            if ((effects & GpuMetadataChange.Storage) != 0) reasons |= ResourceChangeKind.Storage;
            if ((effects & GpuMetadataChange.Executable) != 0) reasons |= ResourceChangeKind.Executable;
            Metadata = candidate;
            HasCommittedMetadata = true;
            Realize();
            InitialDiscovery = GpuInitialDiscovery.None;
            if (reasons != ResourceChangeKind.None) MetadataRevision++;
            if ((reasons & ResourceChangeKind.Storage) != 0) StorageRevision++;
            pendingChange = GpuMetadataChange.None;
            publishedAvailable = MetadataAvailable = true;
            foreach (var recipient in recipients.ToArray())
            {
                if (!TryGetRecipient(recipient.Key, out var resource)) continue;
                bool assigned = AssignSnapshot(resource);
                recipients.TryUpdate(recipient.Key, assigned, recipient.Value);
            }
            ScheduleChange(reasons, origin);
        }
        delivery.Complete();
    }

    /// <summary>Publishes a real loss of facts; temporary operation exclusion uses BeginRefresh instead.</summary>
    internal void Withdraw(GpuMetadataChange effects = GpuMetadataChange.Metadata, GpuResource? origin = null, bool fromBacking = false)
    {
        using var delivery = Manager.BeginChangeDelivery();
        lock (recipients)
        {
            if (State != GpuResourceRegistrationState.Live || Manager.IsMetadataRegistryRetired) return;
            BeginRefresh(effects, fromBacking);
            Manager.WithdrawResourceDependencies(this, effects);
            if (!publishedAvailable) return;
            publishedAvailable = false;
            MetadataRevision++;
            foreach (var recipient in recipients.ToArray())
                if (TryGetRecipient(recipient.Key, out var resource) && resource is GpuFramebufferAttachment image)
                {
                    image.AssignMetadataSnapshot(default);
                    image.WithdrawMetadataSnapshot();
                }
            ScheduleChange(ResourceChangeKind.Availability, origin);
        }
        delivery.Complete();
    }

    /// <summary>Captures the terminal recipients before membership is cleared.</summary>
    internal override Action CaptureRetirement()
    {
        lock (recipients)
        {
            // A queued retirement and context shutdown may both capture this lifetime.
            // They share one idempotent delivery so teardown cannot lose or repeat it.
            if (retirementDelivery is not null) return retirementDelivery;
            var reasons = ResourceChangeKind.Retired;
            if (publishedAvailable) reasons |= ResourceChangeKind.Availability;
            publishedAvailable = MetadataAvailable = false;
            MetadataRevision++;
            foreach (var recipient in recipients.ToArray())
                if (TryGetRecipient(recipient.Key, out var resource) && resource is GpuFramebufferAttachment image)
                {
                    image.AssignMetadataSnapshot(default);
                    image.WithdrawMetadataSnapshot();
                }
            var captured = CaptureChange(reasons, null, terminal: true);
            int delivered = 0;
            return retirementDelivery = () =>
            {
                if (System.Threading.Interlocked.Exchange(ref delivered, 1) == 0) captured();
            };
        }
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
    /// <summary>Enqueues only meaningful transitions; equal publication still delivered snapshots above.</summary>
    private void ScheduleChange(ResourceChangeKind reasons, GpuResource? origin)
    {
        if (reasons != ResourceChangeKind.None) Manager.DeliverResourceChange(CaptureChange(reasons, origin, false));
    }

    /// <summary>Retains weak recipients and stops obsolete delivery after native reentry.</summary>
    private Action CaptureChange(ResourceChangeKind reasons, GpuResource? origin, bool terminal)
    {
        var captured = recipients.Keys.ToArray();
        var change = new RHIResourceChange(reasons, !terminal && publishedAvailable, MetadataRevision, StorageRevision, origin);
        return () =>
        {
            List<Exception>? failures = null;
            foreach (var recipient in captured)
            {
                if (MetadataRevision != change.MetadataRevision || (!terminal && (!Manager.IsRegistered(this) || IsUsable != change.Available))) break;
                if (!recipient.TryGetTarget(out var resource) || resource.IsDisposed
                    || !ReferenceEquals(Manager.GetResourceRegistration(resource), this)
                    || !terminal && !ContainsRecipient(resource)) continue;
                var delivered = resource is GpuFramebufferAttachment && !terminal
                    ? change with { Available = change.Available && HasSnapshot(resource) } : change;
                try { resource.OnResourceChanged(delivered); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
            if (failures is not null) throw new AggregateException("Resource recipients failed after publication.", failures);
        };
    }
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
            typed.AssignMetadataSnapshot(Metadata);
            return true;
        }
        return Manager.AssignImageMetadata(this, resource);
    }
    #endregion
}
