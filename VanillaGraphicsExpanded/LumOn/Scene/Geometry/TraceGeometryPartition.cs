using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Domain capture/publication queues beneath the coordinator's sole residency authority.</summary>
internal sealed class TraceGeometryPartition : IPartitionResidencyBackend, IDisposable
{
    private readonly PartitionCoordinator coordinator;
    private readonly TraceGeometrySourceCache source;
    private readonly TraceGeometryMaterials materials;
    private readonly ITraceGeometryBackend backend;
    private readonly string world;
    private readonly Dictionary<PartitionCellKey, (ChunkKey Key, int Version)> dependencies = new();
    private readonly Dictionary<PartitionCellKey, Pending> pending = new();
    private readonly Dictionary<PartitionCellKey, Capture> captures = new();
    private readonly PartitionLayout layout = new(new(16, 16, 16));
    private (bool Near, double? SurfaceSize)? configuration;
    private TraceGeometryTables? stagingTables;
    private int tableOffset;
    private long serviceFrame;
    public long Instance { get; }
    public long StagedPayloadBytes => pending.Count * TraceGeometryCell.UploadBytes;
    public long PublishedCells { get; private set; }

    /// <summary>Immutable cell work retained across shared-upload-budget deferrals.</summary>
    private sealed record Pending(PartitionRequest Request, ChunkKey Chunk, int Version, TraceGeometryCell Cell);

    /// <summary>Retains worker completion independently of cache cancellation or source replacement.</summary>
    private sealed class Capture
    {
        public required PartitionRequest Request;
        public int Completed;
    }

    /// <summary>Registers one shared owner; its source/cache and backend lifetime end with this object.</summary>
    public TraceGeometryPartition(PartitionCoordinator coordinator, TraceGeometrySourceCache source,
        TraceGeometryMaterials materials, ITraceGeometryBackend backend, string world = "primary")
    {
        this.coordinator = coordinator; this.source = source; this.materials = materials; this.backend = backend; this.world = world;
        int slots = backend.Resolution / 16;
        Instance = coordinator.Register("Shared TraceScene geometry", world, layout, new(0, 0, 0), new(slots * slots * slots, 64, 64, 32, 8L * 1024 * 1024), this);
    }

    #region Frame preparation and service
    /// <summary>Updates immediate demand and invalidations before the host pumps the shared coordinator.</summary>
    public void Prepare(TraceGeometryCoverage coverage)
    {
        if (coverage.Resolution != backend.Resolution) throw new InvalidOperationException("Recreate the registration when storage configuration changes.");
        var next = (coverage.NearField.HasValue, coverage.Surface is { } s ? (double?)(s.Max.X - s.Min.X) : null);
        if (configuration.HasValue && configuration.Value != next) throw new InvalidOperationException("Consumer configuration changes require a new registration generation.");
        configuration = next;
        backend.SetWindow(coverage);
        if (coverage.NearField is { } near) coordinator.SetSource(new(1, Instance, world, near.Min, coverage.Clip(near), 100));
        else coordinator.RemoveSource(Instance, 1);
        if (coverage.Surface is { } surface) coordinator.SetSource(new(2, Instance, world, surface.Min, coverage.Clip(surface)));
        else coordinator.RemoveSource(Instance, 2);
        coordinator.RefreshCoverage(Instance);
        foreach (var pair in dependencies.ToArray())
            if (!source.IsCurrent(pair.Value.Key, pair.Value.Version)) coordinator.Dirty(pair.Key);
    }

    /// <summary>Services source and upload work after the host pumps global budgets; does not pump a second frame.</summary>
    public void Service(TraceGeometryCoverage coverage, TraceGeometryWorkBudget? budget = null)
    {
        budget ??= TraceGeometryWorkBudget.Default;
        budget.Validate();
        // Pausing does not cancel leases or discard completed sources; resume continues the same publication.
        if (budget.CellsPerFrame == 0 || budget.UploadBytesPerFrame == 0) return;
        serviceFrame++;
        long remainingUpload = Math.Min(budget.UploadBytesPerFrame, coordinator.GetUploadLimit(Instance));
        var demand = new HashSet<PartitionCoordinate>();
        if (coverage.NearField is { } near) demand.UnionWith(layout.Intersecting(coverage.Clip(near)));
        if (coverage.Surface is { } surface) demand.UnionWith(layout.Intersecting(coverage.Clip(surface)));
        var wanted = demand.Where(c => coordinator.TryGetCell(new(Instance, world, c), out var info) && !info.Ready).ToArray();
        source.Update(demand.ToArray(), coverage, wanted.Where(c => !pending.ContainsKey(new(Instance, world, c))).ToHashSet(), AuthorizeCapture,
            budget.SourceChunksPerFrame);
        foreach (var pair in captures.ToArray())
            if (Volatile.Read(ref pair.Value.Completed) != 0 && !source.HasSnapshot(pair.Key.Coordinate))
                coordinator.FinishUpdate(pair.Value.Request, true);
        var nearCells = wanted.Where(c => coverage.IsNear(c)).OrderBy(c => coverage.DistanceSquared(c)).ToArray();
        var surfaceCells = wanted.Where(c => !coverage.IsNear(c) &&
            (pending.ContainsKey(new(Instance, world, c)) || source.HasSnapshot(c))).OrderBy(c => coverage.DistanceSquared(c)).ToArray();
        var tables = materials.Snapshot();
        int published = 0;
        // When only one cell fits, alternate consumer preference rather than starving NearField behind surface work.
        bool singleCell = budget.CellsPerFrame == 1 || remainingUpload < ((TraceGeometryCell.UploadBytes + 2) << 1);
        var ordered = singleCell && (serviceFrame & 1) == 0
            ? nearCells.Take(1).Concat(surfaceCells).Concat(nearCells.Skip(1))
            : surfaceCells.Take(1).Concat(nearCells).Concat(surfaceCells.Skip(1));
        foreach (var coordinate in ordered)
        {
            if (published >= budget.CellsPerFrame) break;
            var key = new PartitionCellKey(Instance, world, coordinate);
            if (!pending.TryGetValue(key, out var work))
            {
                if (!source.HasSnapshot(coordinate)) continue;
                if (captures.TryGetValue(key, out var running) && Volatile.Read(ref running.Completed) == 0) continue;
                PartitionRequest? request;
                if (captures.Remove(key, out var capture)) request = capture.Request;
                else if (!coordinator.TryBeginUpdate(key, out request)) continue;
                if (!source.TryExtract(coordinate, out var chunk, out var cell))
                { coordinator.FinishUpdate(request!, true); continue; }
                work = new(request!, chunk!.Key, chunk.Version, cell!); pending.Add(key, work);
                dependencies[key] = (chunk.Key, chunk.Version);
                coordinator.AcknowledgeDomainWorker(request!);
                // Worker capture may have added entries since the initial snapshot.
                tables = materials.Snapshot();
            }
            if (backend.TablesRevision != tables.Revision)
            {
                stagingTables ??= tables;
                int remaining = (int)TraceGeometryTables.MaximumUploadBytes - tableOffset;
                // Four-kilobyte alignment spans complete rows in every table, including the final light LUT.
                int batch = (int)Math.Min(remaining, (remainingUpload >> 12) << 12);
                if (batch == 0 || !coordinator.TryStageUpdate(work.Request, batch,
                    () => backend.UploadTableRange(stagingTables, tableOffset, batch))) continue;
                tableOffset += batch; remainingUpload -= batch;
                if (tableOffset == TraceGeometryTables.MaximumUploadBytes) { stagingTables = null; tableOffset = 0; }
                if (backend.TablesRevision != tables.Revision) break;
            }
            long bytes = TraceGeometryCell.UploadBytes + 2;
            if (bytes > remainingUpload) break;
            bool submitted = false;
            bool success = coordinator.TryPublishUpdate(work.Request, bytes,
                () => source.IsCurrent(work.Chunk, work.Version),
                () => { submitted = true; return backend.Publish(work.Request, work.Cell, tables); },
                work.Cell.Unsupported ? PartitionContentStatus.Unsupported : PartitionContentStatus.Supported);
            // Charge submitted bytes even on rejection, but keep looking for the coordinator's reserved upload turn.
            if (submitted) remainingUpload -= bytes;
            if (success) { pending.Remove(key); published++; PublishedCells++; }
            else if (!coordinator.IsCurrent(work.Request)) pending.Remove(key);
        }
    }

    /// <summary>Reserves global work credit before a source worker starts, retaining cancellation credit until it exits.</summary>
    private Action? AuthorizeCapture(PartitionCoordinate coordinate)
    {
        var key = new PartitionCellKey(Instance, world, coordinate);
        if (!coordinator.TryBeginUpdate(key, out var request)) return null;
        var capture = new Capture { Request = request! }; captures.Add(key, capture);
        return () => { Volatile.Write(ref capture.Completed, 1); coordinator.AcknowledgeDomainWorker(request!); };
    }
    #endregion

    #region Residency callbacks
    /// <summary>Geometry participation requires no second upload.</summary>
    public bool SetActive(in PartitionCellKey key, bool active) => true;
    /// <summary>Clears stale readiness immediately and discards superseded staged data.</summary>
    public void Invalidate(in PartitionCellKey key) { backend.Invalidate(key); dependencies.Remove(key); pending.Remove(key); captures.Remove(key); }
    /// <summary>Releases departing storage through the same owner-checked invalidation path.</summary>
    public void Retire(in PartitionCellKey key) { backend.Invalidate(key); dependencies.Remove(key); pending.Remove(key); captures.Remove(key); }
    /// <summary>Retires registration before releasing source and render storage.</summary>
    public void Dispose() { coordinator.Unregister(Instance); source.Dispose(); backend.Dispose(); }
    #endregion
}
