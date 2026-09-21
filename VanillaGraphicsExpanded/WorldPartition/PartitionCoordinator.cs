using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Single render-thread authority for registered partition residency and publication.</summary>
/// <remarks>Construct on the render thread. Workers can only enqueue completion records.</remarks>
internal sealed partial class PartitionCoordinator
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly PartitionLimits shared;
    private readonly Dictionary<long, PartitionRegistration> registrations = new();
    private readonly ConcurrentQueue<PartitionCompletion> completions = new();
    private long nextInstance, nextGeneration, nextIncarnation, nextRequest;
    private long tick;
    private long lastServed;
    private int captures, dispatches;
    private long uploaded;
    private bool pumping;

    /// <summary>Creates a coordinator with shared resource ceilings.</summary>
    public PartitionCoordinator(PartitionLimits sharedLimits)
    {
        sharedLimits.Validate();
        shared = sharedLimits;
    }

    #region Registration and source API
    /// <summary>Registers an independent instance; no content-kind identity or half-block center is required.</summary>
    public long Register(string name, string world, PartitionLayout layout, PartitionCoveragePolicy coverage,
        PartitionLimits limits, IPartitionProvider provider)
    {
        CheckMutation();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(world);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(provider);
        coverage.Validate();
        limits.Validate();
        long id = ++nextInstance;
        registrations.Add(id, new PartitionRegistration { Instance = id, Generation = ++nextGeneration,
            Name = name, World = world, Layout = layout, Coverage = coverage, Limits = limits, Provider = provider });
        return id;
    }

    /// <summary>Replaces layout as a new generation, retiring every old logical lifetime.</summary>
    public void ReplaceLayout(long instance, PartitionLayout layout)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(layout);
        PartitionRegistration registration = registrations[instance];
        foreach (PartitionCellState cell in registration.Cells.Values) Retire(registration, cell);
        registration.Cells.Clear();
        registration.SourceCoverage.Clear();
        registration.Retained.Clear();
        registration.Layout = layout;
        registration.Generation = ++nextGeneration;
        registration.CoverageDirty = true;
    }

    /// <summary>Disposes one registration and invalidates outstanding callbacks.</summary>
    public void Unregister(long instance)
    {
        CheckMutation();
        if (!registrations.Remove(instance, out PartitionRegistration? registration)) return;
        foreach (PartitionCellState cell in registration.Cells.Values) Retire(registration, cell);
    }

    /// <summary>Retires every partition in one world without touching other scopes.</summary>
    public void UnloadWorld(string world)
    {
        CheckMutation();
        foreach (long id in registrations.Values.Where(r => r.World == world).Select(r => r.Instance).ToArray()) Unregister(id);
    }

    /// <summary>Updates a targeted source; unchanged records avoid coverage enumeration.</summary>
    public void SetSource(PartitionSource source)
    {
        CheckMutation();
        source.Required.Validate();
        if (!double.IsFinite(source.Priority) || !double.IsFinite(source.Position.X) ||
            !double.IsFinite(source.Position.Y) || !double.IsFinite(source.Position.Z)) throw new ArgumentException("Invalid source.");
        PartitionRegistration registration = registrations[source.Instance];
        if (registration.World != source.World) throw new ArgumentException("Source world does not match registration.");
        if (registration.Sources.TryGetValue(source.Id, out PartitionSource? old) && old == source) return;
        registration.Sources[source.Id] = source;
        registration.CoverageDirty = true;
    }

    /// <summary>Removes one source without affecting other contributors to the union.</summary>
    public void RemoveSource(long instance, long sourceId)
    {
        CheckMutation();
        PartitionRegistration registration = registrations[instance];
        if (registration.Sources.Remove(sourceId)) registration.CoverageDirty = true;
    }

    /// <summary>Invalidates known-stale contents immediately and cancels the previous revision.</summary>
    public void Dirty(in PartitionCellKey key)
    {
        CheckMutation();
        if (!registrations.TryGetValue(key.Instance, out PartitionRegistration? registration) || registration.World != key.World ||
            !registration.Cells.TryGetValue(key.Coordinate, out PartitionCellState? cell)) return;
        Cancel(cell);
        registration.Provider.Invalidate(key);
        cell.Ready = false;
        cell.RequiredUploadBytes = 0;
        cell.Revision++;
        cell.WaitingSince = tick;
        cell.RetryAt = tick;
    }

    /// <summary>Returns immutable observations instead of writable lifecycle objects.</summary>
    public PartitionCellInfo[] Cells(long instance)
    {
        CheckOwner();
        return registrations[instance].Cells.Values.Select(c => new PartitionCellInfo(c.Key, c.Incarnation,
            c.Revision, c.Desired, c.Actual, c.Progress, c.ContentStatus, c.Ready)).ToArray();
    }

    /// <summary>Reports desired coverage separately from readiness and capacity.</summary>
    public PartitionStatistics Statistics(long instance)
    {
        CheckOwner();
        PartitionRegistration r = registrations[instance];
        PartitionCellState[] cells = r.Cells.Values.ToArray();
        int required = cells.Count(c => c.Desired == PartitionResidency.Active);
        int totalRequired = registrations.Values.Sum(p => p.Cells.Values.Count(c => c.Desired == PartitionResidency.Active));
        return new(required, cells.Count(c => c.Actual != PartitionResidency.Unloaded), cells.Count(c => c.Ready),
            cells.Count(c => c.Actual != PartitionResidency.Unloaded && !c.Ready),
            cells.Count(c => !c.Ready && c.Request == null), Outstanding(r),
            cells.Count(c => !c.Ready && c.Snapshot == null && c.Progress != PartitionProgress.BudgetBlocked), cells.Count(c => c.Completion != null || c.Progress == PartitionProgress.BudgetBlocked),
            r.Retries, r.StaleCompletions, Math.Max(Math.Max(0, required - r.Limits.ResidentCells),
                Math.Min(required, Math.Max(0, totalRequired - shared.ResidentCells))), r.CoverageEvaluations, r.CoverageCellVisits,
            cells.Select(c => Math.Max(0, c.RequiredUploadBytes - Math.Min(shared.UploadBytes, r.Limits.UploadBytes))).DefaultIfEmpty().Max());
    }
    #endregion

    #region Thread and lifetime guards
    /// <summary>Prevents worker callbacks from accessing mutable state or GPU providers.</summary>
    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Partition state belongs to its render thread.");
    }

    /// <summary>Provider callbacks may acknowledge work but cannot reenter state mutations.</summary>
    private void CheckMutation()
    {
        CheckOwner();
        if (pumping) throw new InvalidOperationException("Provider callbacks cannot mutate the coordinator during a pump.");
    }

    /// <summary>Invalidates the matching request before advisory cancellation reaches worker code.</summary>
    private static void Cancel(PartitionCellState cell)
    {
        cell.Request = null;
        cell.Snapshot = null;
        cell.Completion = null;
        cell.Progress = PartitionProgress.Idle;
        cell.Cancellation?.Cancel();
        cell.Cancellation?.Dispose();
        cell.Cancellation = null;
    }

    /// <summary>Retires a cell on its owning render thread before releasing residency credit.</summary>
    private static void Retire(PartitionRegistration registration, PartitionCellState cell)
    {
        Cancel(cell);
        registration.Provider.Invalidate(cell.Key);
        registration.Provider.Retire(cell.Key);
        cell.Ready = false;
        cell.Reserved = false;
        cell.Actual = PartitionResidency.Unloaded;
    }
    #endregion
}
