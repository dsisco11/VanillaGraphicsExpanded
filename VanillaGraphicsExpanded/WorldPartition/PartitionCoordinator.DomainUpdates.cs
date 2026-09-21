using System;
using System.Collections.Concurrent;
using System.Threading;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Authorizes domain-selected content work without creating a second residency scheduler.</summary>
internal sealed partial class PartitionCoordinator
{
    private readonly ConcurrentQueue<PartitionRequest> finishedDomainWorkers = new();

    /// <summary>Allows workers to release cancelled work credit without touching mutable residency state.</summary>
    public void AcknowledgeDomainWorker(PartitionRequest request) => finishedDomainWorkers.Enqueue(request);

    #region Coverage and observation
    /// <summary>Applies changed sources immediately without resetting or spending the shared update budgets.</summary>
    public void RefreshCoverage(long instance)
    {
        CheckMutation();
        UpdateCoverage(registrations[instance]);
    }

    /// <summary>Reads one logical cell without allocating or exposing mutable coordinator state.</summary>
    public bool TryGetCell(in PartitionCellKey key, out PartitionCellInfo info)
    {
        CheckOwner();
        if (registrations.TryGetValue(key.Instance, out PartitionRegistration? r) && r.World == key.World &&
            r.Cells.TryGetValue(key.Coordinate, out PartitionCellState? cell))
        {
            info = new(cell.Key, cell.Incarnation, cell.Revision, cell.Desired, cell.Actual,
                cell.Progress, cell.ContentStatus, cell.Ready);
            return true;
        }
        info = default;
        return false;
    }
    #endregion

    #region Domain work authorization
    /// <summary>Reserves shared work and residency credit before an existing domain queue starts its worker.</summary>
    public bool TryBeginUpdate(in PartitionCellKey key, out PartitionRequest? request)
    {
        CheckMutation();
        request = null;
        if (!registrations.TryGetValue(key.Instance, out PartitionRegistration? r) || r.World != key.World ||
            r.Provider is IPartitionProvider || !r.Cells.TryGetValue(key.Coordinate, out PartitionCellState? cell) ||
            cell.Desired == PartitionResidency.Unloaded || cell.Request != null || cell.RetryAt > tick ||
            cell.RequiredUploadBytes > Math.Min(shared.UploadBytes, r.Limits.UploadBytes) ||
            r.Captures >= r.Limits.Captures || r.Dispatches >= r.Limits.Dispatches) return false;

        if (captures >= shared.Captures || dispatches >= shared.Dispatches || !HasDomainServiceRoom(r) || !MakeWorkCapacity(r, cell))
        {
            if (r.WaitingDomainCapture == null) r.DomainWaitOrder = ++nextDomainWait;
            r.WaitingDomainCapture = key.Coordinate;
            return false;
        }
        if (!Reserve(r, cell)) return false;
        r.WaitingDomainCapture = null;

        captures++; r.Captures++;
        lastDomainAdmission = ++admissionSequence;
        dispatches++; r.Dispatches++;
        cell.Cancellation = new CancellationTokenSource();
        request = new(r.Generation, key, cell.Incarnation, cell.Revision, ++nextRequest, cell.Cancellation.Token);
        cell.Request = request;
        cell.Progress = PartitionProgress.Processing;
        workers.Add(request.RequestId, request);
        return true;
    }

    /// <summary>Checks every lease identity before a domain result can reach GPU storage.</summary>
    public bool IsCurrent(PartitionRequest request)
    {
        CheckOwner();
        return registrations.TryGetValue(request.Key.Instance, out PartitionRegistration? r) &&
            r.Generation == request.Generation && r.World == request.Key.World &&
            r.Cells.TryGetValue(request.Key.Coordinate, out PartitionCellState? cell) &&
            cell.Request == request && cell.Incarnation == request.Incarnation && cell.Revision == request.Revision &&
            cell.Desired != PartitionResidency.Unloaded && !request.Cancellation.IsCancellationRequested;
    }

    /// <summary>Executes publication only under a current lease and available upload credit, then acknowledges residency.</summary>
    /// <remarks>False with a current lease means budget deferral; retain the completed result and retry later.</remarks>
    public bool TryPublishUpdate(PartitionRequest request, long bytes, Func<bool> dependenciesValid, Func<bool> publish)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(dependenciesValid);
        ArgumentNullException.ThrowIfNull(publish);
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (!IsCurrent(request)) { FinishUpdate(request, false); return false; }
        PartitionRegistration r = registrations[request.Key.Instance];
        PartitionCellState cell = r.Cells[request.Key.Coordinate];
        cell.RequiredUploadBytes = bytes;
        cell.Progress = PartitionProgress.AwaitingPublication;
        bool valid;
        pumping = true;
        try { valid = dependenciesValid(); }
        finally { pumping = false; }
        if (!valid) { FinishUpdate(request, false); return false; }
        if (bytes > shared.UploadBytes || bytes > r.Limits.UploadBytes)
        {
            workers.Remove(request.RequestId);
            Cancel(cell);
            cell.Progress = PartitionProgress.BudgetBlocked;
            return false;
        }
        if (bytes > 0 && uploadTurn != 0 && uploadTurn != request.RequestId) return false;
        if (bytes > shared.UploadBytes - uploaded || bytes > r.Limits.UploadBytes - r.Uploaded)
        {
            uploadTurn = request.RequestId;
            return false;
        }
        if (uploadTurn == request.RequestId) uploadTurn = 0;

        // No callback can change coverage or retire/reassign the lease during this publication.
        pumping = true;
        bool success;
        try
        {
            uploaded += bytes; r.Uploaded += bytes;
            success = publish() && dependenciesValid();
        }
        finally { pumping = false; }
        if (!success) { FinishUpdate(request, false); return false; }
        workers.Remove(request.RequestId);
        cell.Ready = true;
        cell.ContentStatus = PartitionContentStatus.Supported;
        cell.Actual = PartitionResidency.Loaded;
        cell.RetryCount = 0;
        long wait = tick - cell.UnreadySince;
        r.PublicationCount++; r.PublicationWaitTicks += wait;
        r.MaximumPublicationWaitTicks = Math.Max(r.MaximumPublicationWaitTicks, wait);
        Cancel(cell);
        pumping = true;
        try
        {
            if (cell.Desired == PartitionResidency.Active && r.Provider.SetActive(cell.Key, true))
                cell.Actual = PartitionResidency.Active;
            else if (cell.Actual != cell.Desired) r.PendingParticipation = true;
        }
        finally { pumping = false; }
        return true;
    }

    /// <summary>Releases completed or cancelled worker credit even after retirement; failures never recreate cells.</summary>
    public void FinishUpdate(PartitionRequest request, bool missingDependencies)
    {
        CheckMutation();
        workers.Remove(request.RequestId);
        if (!IsCurrent(request))
        {
            if (registrations.TryGetValue(request.Key.Instance, out PartitionRegistration? owner)) owner.StaleCompletions++;
            return;
        }
        PartitionRegistration r = registrations[request.Key.Instance];
        PartitionCellState cell = r.Cells[request.Key.Coordinate];
        r.Provider.Invalidate(cell.Key);
        cell.Ready = false;
        cell.ContentStatus = missingDependencies ? PartitionContentStatus.MissingDependencies : PartitionContentStatus.Supported;
        Retry(r, cell);
    }
    #endregion
}
