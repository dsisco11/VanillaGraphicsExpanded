using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Budgeted scheduling and acknowledged publication for registered cells.</summary>
internal sealed partial class PartitionCoordinator
{
    // Cancelled workers retain their dispatch credit until they acknowledge completion.
    private readonly Dictionary<long, PartitionRequest> workers = new();
    private long uploadTurn;

    #region Scheduling
    /// <summary>Advances coverage and budgeted work on the render thread using monotonically increasing ticks.</summary>
    public void Pump(long currentTick)
    {
        CheckMutation();
        if (currentTick < tick) throw new ArgumentOutOfRangeException(nameof(currentTick));
        tick = currentTick;
        pumping = true;
        try
        {
            captures = dispatches = 0;
            uploaded = 0;
            foreach (PartitionRegistration r in registrations.Values)
            {
                r.Captures = r.Dispatches = 0;
                r.Uploaded = 0;
                UpdateCoverage(r);
            }
            DrainCompletions();
            PartitionCellState? reservedUpload = registrations.Values.SelectMany(r => r.Cells.Values).FirstOrDefault(c => c.Completion?.Request.RequestId == uploadTurn);
            if (reservedUpload == null || (reservedUpload.Desired == PartitionResidency.Loaded &&
                registrations.Values.Any(r => r.Cells.Values.Any(c => c.Desired == PartitionResidency.Active && !c.Ready)))) uploadTurn = 0;
            // Round-robin at each unit of progress prevents one partition spending a shared frame budget first forever.
            bool progress;
            do
            {
                progress = false;
                PartitionRegistration[] order = registrations.Values.OrderBy(r => r.Instance <= lastServed ? 1 : 0).ThenBy(r => r.Instance).ToArray();
                foreach (PartitionRegistration r in order)
                {
                    if (!Advance(r, PartitionResidency.Active)) continue;
                    lastServed = r.Instance;
                    progress = true;
                }
                DrainCompletions();
            } while (progress);
            // Speculative work uses only service left after required work has had its opportunity.
            do
            {
                progress = false;
                foreach (PartitionRegistration r in registrations.Values.OrderBy(r => r.Instance <= lastServed ? 1 : 0).ThenBy(r => r.Instance).ToArray())
                {
                    if (!Advance(r, PartitionResidency.Loaded)) continue;
                    lastServed = r.Instance;
                    progress = true;
                }
                DrainCompletions();
            } while (progress);
        }
        finally { pumping = false; }
    }

    /// <summary>Finds eligible work, prioritizing required cells while aging requests within a partition.</summary>
    private bool Advance(PartitionRegistration r, PartitionResidency desired)
    {
        foreach (PartitionCellState cell in r.Cells.Values.Where(c => c.Desired == desired && (!c.Ready || c.Actual != c.Desired)).OrderByDescending(c =>
            Priority(r, c) + (tick - c.WaitingSince)).ThenBy(c => c.Incarnation))
        {
            if (cell.RetryAt > tick || cell.RequiredUploadBytes > Math.Min(shared.UploadBytes, r.Limits.UploadBytes)) continue;
            if (cell.Ready)
            {
                if (cell.Actual == cell.Desired) continue;
                if (r.Provider.SetActive(cell.Key, cell.Desired == PartitionResidency.Active))
                {
                    cell.Actual = cell.Desired;
                    return true;
                }
                cell.RetryAt = tick + 1;
                r.Retries++;
                continue;
            }
            if (cell.Completion != null)
            {
                PartitionCompletion completion = cell.Completion;
                // A payload larger than an entire budget is explicit backlog, not a global service reservation.
                if (completion.UploadBytes > shared.UploadBytes || completion.UploadBytes > r.Limits.UploadBytes) continue;
                if (uploadTurn != 0 && uploadTurn != completion.Request.RequestId) continue;
                if (completion.UploadBytes > shared.UploadBytes - uploaded || completion.UploadBytes > r.Limits.UploadBytes - r.Uploaded)
                {
                    // Reserve the next frame's first upload for this feasible payload. Repeated cheap uploads cannot starve it.
                    uploadTurn = completion.Request.RequestId;
                    continue;
                }
                uploadTurn = 0;
                if (!r.Provider.DependenciesValid(completion.Request, completion.Snapshot))
                {
                    Retry(r, cell);
                    return true;
                }
                // The owner cannot be reentered. Recheck external dependencies after upload before exposing readiness.
                uploaded += completion.UploadBytes;
                r.Uploaded += completion.UploadBytes;
                if (!r.Provider.Publish(completion) || !r.Provider.DependenciesValid(completion.Request, completion.Snapshot))
                {
                    r.Provider.Invalidate(cell.Key);
                    Retry(r, cell);
                    return true;
                }
                cell.Ready = true;
                cell.ContentStatus = completion.Status;
                if (cell.Actual == PartitionResidency.Unloaded) cell.Actual = PartitionResidency.Loaded;
                Cancel(cell);
                return true;
            }
            if (cell.Request != null && cell.Snapshot != null && cell.Progress == PartitionProgress.Capturing)
            {
                if (dispatches >= shared.Dispatches || r.Dispatches >= r.Limits.Dispatches) continue;
                dispatches++;
                r.Dispatches++;
                cell.Progress = PartitionProgress.Processing;
                workers.Add(cell.Request.RequestId, cell.Request);
                r.Provider.Dispatch(cell.Request, cell.Snapshot, completions.Enqueue);
                return true;
            }
            if (cell.Request != null || captures >= shared.Captures || r.Captures >= r.Limits.Captures) continue;
            if (!MakeWorkCapacity(r, cell) || !Reserve(r, cell)) continue;
            cell.WaitingSince = tick;
            captures++;
            r.Captures++;
            cell.Cancellation = new CancellationTokenSource();
            cell.Request = new(r.Generation, cell.Key, cell.Incarnation, cell.Revision, ++nextRequest, cell.Cancellation.Token);
            cell.Progress = PartitionProgress.Capturing;
            PartitionCapture capture = r.Provider.Capture(cell.Request);
            cell.ContentStatus = capture.Status;
            if (capture.Status == PartitionContentStatus.MissingDependencies || capture.Snapshot == null) Retry(r, cell);
            else cell.Snapshot = capture.Snapshot;
            return true;
        }
        return false;
    }

    /// <summary>Reclaims speculative queued work before allowing it to block newly required cells.</summary>
    private bool MakeWorkCapacity(PartitionRegistration r, PartitionCellState cell)
    {
        while (Outstanding(null) >= shared.InFlight || Outstanding(r) >= r.Limits.InFlight)
        {
            if (cell.Desired != PartitionResidency.Active) return false;
            bool localFull = Outstanding(r) >= r.Limits.InFlight;
            PartitionCellState? victim = registrations.Values.Where(p => !localFull || p == r)
                .SelectMany(p => p.Cells.Values).FirstOrDefault(c => c.Desired == PartitionResidency.Loaded && c.Request != null);
            if (victim == null) return false;
            // Captured/completed snapshots release credit immediately. A cancelled worker still counts until its acknowledgement.
            if (victim.Request?.RequestId == uploadTurn) uploadTurn = 0;
            Cancel(victim);
        }
        return true;
    }
    /// <summary>Reserves finite storage, evicting speculative cells before reporting required pressure.</summary>
    private bool Reserve(PartitionRegistration r, PartitionCellState cell)
    {
        if (cell.Reserved) return true;
        if (cell.Desired != PartitionResidency.Active && registrations.Values.Any(p => p.Cells.Values.Any(c =>
            c.Desired == PartitionResidency.Active && !c.Reserved))) return false;
        while (r.Cells.Values.Count(c => c.Reserved) >= r.Limits.ResidentCells ||
            registrations.Values.Sum(p => p.Cells.Values.Count(c => c.Reserved)) >= shared.ResidentCells)
        {
            if (cell.Desired != PartitionResidency.Active) return false;
            bool localFull = r.Cells.Values.Count(c => c.Reserved) >= r.Limits.ResidentCells;
            var victim = registrations.Values.Where(p => !localFull || p == r)
                .SelectMany(p => p.Cells.Values.Select(c => (Registration: p, Cell: c)))
                .Where(v => v.Cell.Reserved && v.Cell.Desired == PartitionResidency.Loaded)
                .OrderBy(v => Priority(v.Registration, v.Cell)).FirstOrDefault();
            if (victim.Cell == null) return false;
            // Keep desired coverage intact under pressure, but retire physical residency and old requests.
            if (victim.Cell.Request?.RequestId == uploadTurn) uploadTurn = 0;
            Retire(victim.Registration, victim.Cell);
            victim.Cell.Incarnation = ++nextIncarnation;
        }
        cell.Reserved = true;
        return true;
    }
    #endregion

    #region Completion validation
    /// <summary>Matches all identities and the captured snapshot before accepting a worker acknowledgement.</summary>
    private void DrainCompletions()
    {
        while (completions.TryDequeue(out PartitionCompletion? completion))
        {
            PartitionRequest request = completion.Request;
            bool workerMatches = workers.TryGetValue(request.RequestId, out PartitionRequest? issued) && issued == request;
            if (workerMatches) workers.Remove(request.RequestId);
            if (!registrations.TryGetValue(request.Key.Instance, out PartitionRegistration? r)) continue;
            if (!workerMatches || r.Generation != request.Generation || r.World != request.Key.World ||
                !r.Cells.TryGetValue(request.Key.Coordinate, out PartitionCellState? cell) || cell.Request != request ||
                cell.Incarnation != request.Incarnation || cell.Revision != request.Revision ||
                cell.Desired == PartitionResidency.Unloaded || !ReferenceEquals(cell.Snapshot, completion.Snapshot))
            {
                r.StaleCompletions++;
                continue;
            }
            if (completion.Status == PartitionContentStatus.MissingDependencies || completion.Content == null || completion.UploadBytes < 0)
            {
                cell.ContentStatus = completion.Status;
                Retry(r, cell);
                continue;
            }
            cell.RequiredUploadBytes = completion.UploadBytes;
            if (completion.UploadBytes > shared.UploadBytes || completion.UploadBytes > r.Limits.UploadBytes)
            {
                // Impossible uploads must not retain shared work credit and starve unrelated registrations.
                // Preserve desired demand and report the byte shortfall; Dirty or re-registration retries new content/configuration.
                Cancel(cell);
                cell.Progress = PartitionProgress.BudgetBlocked;
                continue;
            }
            cell.Completion = completion;
            cell.Progress = PartitionProgress.AwaitingPublication;
        }
    }

    /// <summary>Counts capture-to-publication work plus cancelled workers still holding executor resources.</summary>
    private int Outstanding(PartitionRegistration? registration)
    {
        PartitionRegistration[] owners = registration == null ? registrations.Values.ToArray() : new[] { registration };
        var active = owners.SelectMany(r => r.Cells.Values).Where(c => c.Request != null).Select(c => c.Request!.RequestId).ToHashSet();
        return active.Count + workers.Values.Count(w => (registration == null || w.Key.Instance == registration.Instance) && !active.Contains(w.RequestId));
    }
    /// <summary>Schedules bounded retry without treating absent source data as valid empty contents.</summary>
    private void Retry(PartitionRegistration r, PartitionCellState cell)
    {
        Cancel(cell);
        cell.Progress = PartitionProgress.Retry;
        cell.RetryAt = tick + 1;
        r.Retries++;
    }
    #endregion
}
