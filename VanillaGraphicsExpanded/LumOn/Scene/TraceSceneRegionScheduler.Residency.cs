using System;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Delegates tracing-region coverage and publication authorization to the shared coordinator.</summary>
internal sealed partial class TraceSceneRegionScheduler
{
    private long instance;

    #region Coverage and publication
    /// <summary>Uses coordinator range deltas; content records retain only domain scheduling metadata.</summary>
    private void UpdateCoverage()
    {
        if (instance == 0) instance = worldPartition.Register("Tracing scene", "primary", new(new(32, 32, 32)),
            new(0, 0, 0), new(16384, 256, 128, 128, 16L * 1024 * 1024), this);
        var bounds = new PartitionBounds(new(windowMin.X * 32d, windowMin.Y * 32d, windowMin.Z * 32d),
            new((windowMax.X + 1d) * 32, (windowMax.Y + 1d) * 32, (windowMax.Z + 1d) * 32));
        worldPartition.SetSource(new(0, instance, "primary", bounds.Min, bounds));
        worldPartition.RefreshCoverage(instance);
        foreach (PartitionCellInfo info in worldPartition.Cells(instance))
        {
            ChunkKey key = ChunkKey.FromChunkCoords(checked((int)info.Key.Coordinate.X),
                checked((int)info.Key.Coordinate.Y), checked((int)info.Key.Coordinate.Z));
            if (cellsByPacked.ContainsKey(key.Packed)) continue;
            GetOrCreateCell(key);
            MarkDirty(key.Packed);
        }
    }

    /// <summary>Visits coordinator-selected domain records for source-version refresh after ring movement.</summary>
    public void VisitCoverage(Action<ChunkKey> visitor)
    {
        foreach (TraceSceneRegionCell cell in cellsByPacked.Values) visitor(cell.ChunkKey);
    }

    /// <summary>Returns the immutable identity issued before starting a worker.</summary>
    public PartitionRequest RequestFor(ChunkKey key) => cellsByPacked[key.Packed].Request
        ?? throw new InvalidOperationException("No coordinator-authorized capture.");

    /// <summary>Releases worker credit through the thread-safe acknowledgement boundary.</summary>
    public void WorkerCompleted(PartitionRequest request) => worldPartition.AcknowledgeDomainWorker(request);

    /// <summary>Checks identities before a completed region can be considered for GPU upload.</summary>
    public bool IsCurrent(PartitionRequest request) => worldPartition.IsCurrent(request);

    /// <summary>Authorizes the existing backend upload and acknowledges only a coherent current result.</summary>
    public bool TryPublish(PartitionRequest request, int version, long bytes, Func<bool> dependenciesValid, Func<bool> upload)
    {
        if (!worldPartition.TryPublishUpdate(request, bytes, dependenciesValid, upload)) return false;
        ChunkKey key = ChunkKey.FromChunkCoords(checked((int)request.Key.Coordinate.X),
            checked((int)request.Key.Coordinate.Y), checked((int)request.Key.Coordinate.Z));
        if (cellsByPacked.TryGetValue(key.Packed, out TraceSceneRegionCell? cell))
        {
            cell.Request = null;
            CompleteDomainUpdate(cell, ChunkWorkStatus.Success, version, lastNowTick);
        }
        return true;
    }
    #endregion

    #region Residency backend
    /// <summary>Tracing participation does not require another GPU upload.</summary>
    bool IPartitionResidencyBackend.SetActive(in PartitionCellKey key, bool active) => true;

    /// <summary>Invalidates residency readiness; occupancy ring clearing remains a GPU-backend responsibility.</summary>
    void IPartitionResidencyBackend.Invalidate(in PartitionCellKey key) { }

    /// <summary>Removes departing domain metadata and eligibility without retaining an independent cell lifetime.</summary>
    void IPartitionResidencyBackend.Retire(in PartitionCellKey key)
    {
        ChunkKey chunk = ChunkKey.FromChunkCoords(checked((int)key.Coordinate.X), checked((int)key.Coordinate.Y), checked((int)key.Coordinate.Z));
        if (!cellsByPacked.Remove(chunk.Packed, out TraceSceneRegionCell? cell)) return;
        RemoveFromHeaps(chunk.Packed);
        dirtySet.Remove(chunk.Packed);
        cooldownUntilByKey.Remove(chunk.Packed);
        if (cell.AppliedVersion != 0) appliedCount = Math.Max(0, appliedCount - 1);
        if (cell.InFlightVersion != 0) InFlightCount = Math.Max(0, InFlightCount - 1);
    }
    #endregion

    #region Identity
    /// <summary>Uses world-zero chunk coordinates inside this registration's identity.</summary>
    private PartitionCellKey PartitionKey(ChunkKey chunk)
    {
        chunk.Decode(out int x, out int y, out int z);
        return new(instance, "primary", new(x, y, z));
    }

    /// <summary>Gates notifications through authoritative coverage instead of creating out-of-window residency.</summary>
    private bool Contains(ChunkKey chunk) => instance != 0 && worldPartition.TryGetCell(PartitionKey(chunk), out _);
    #endregion
}
