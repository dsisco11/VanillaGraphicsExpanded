using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Adapts coalesced source chunks to independent partition-owned publication cells.</summary>
internal sealed class NearFieldGeometryPartition : IPartitionProvider
{
    private readonly INearFieldChunkSource source;
    private readonly INearFieldPublicationBackend backend;
    private readonly NearFieldMaterialRegistry materials;
    private readonly HashSet<PartitionCellKey> unsupportedEnvelope = new();
    public int UnsupportedCellCount => unsupportedEnvelope.Count;
    private readonly Dictionary<PartitionCellKey, (ChunkKey Chunk, int Version)> dependencies = new();

    /// <summary>Injects source and publication boundaries without coupling to occupancy scheduling.</summary>
    public NearFieldGeometryPartition(INearFieldChunkSource source, INearFieldPublicationBackend backend, NearFieldMaterialRegistry materials)
    {
        this.source = source; this.backend = backend; this.materials = materials;
    }

    #region Dependency maintenance
    /// <summary>Invalidates all affected subcells before the frame's tracing consumers can see stale contents.</summary>
    public void RefreshDependencies(PartitionCoordinator coordinator)
    {
        foreach (var entry in new List<KeyValuePair<PartitionCellKey, (ChunkKey Chunk, int Version)>>(dependencies))
            if (!backend.ContainsCell(entry.Key.Coordinate) || !source.IsCurrent(entry.Value.Chunk, entry.Value.Version)) coordinator.Dirty(entry.Key);
    }

    /// <summary>Maps negative cell coordinates by floor division to their owning 32-block chunk.</summary>
    public static ChunkKey SourceChunk(in PartitionCoordinate coordinate)
    {
        long x = coordinate.X >> 1, y = coordinate.Y >> 1, z = coordinate.Z >> 1;
        const long limit = 1L << 20;
        if (x < -limit || x >= limit || y < -limit || y >= limit || z < -limit || z >= limit)
            throw new ArgumentOutOfRangeException(nameof(coordinate), "Source chunk exceeds its packed coordinate range.");
        return ChunkKey.FromChunkCoords((int)x, (int)y, (int)z);
    }
    #endregion

    #region Provider acknowledgements
    /// <summary>Shares one immutable chunk snapshot among all eight dependent publication cells.</summary>
    public PartitionCapture Capture(PartitionRequest request)
    {
        if (!backend.ContainsCell(request.Key.Coordinate))
        {
            unsupportedEnvelope.Add(request.Key);
            return new(PartitionContentStatus.MissingDependencies, null);
        }
        unsupportedEnvelope.Remove(request.Key);
        ChunkKey key = SourceChunk(request.Key.Coordinate);
        if (!source.TryGet(key, out NearFieldChunkSnapshot? snapshot) || snapshot == null)
            return new(PartitionContentStatus.MissingDependencies, null);
        dependencies[request.Key] = (key, snapshot.Version);
        return new(PartitionContentStatus.Supported, snapshot);
    }

    /// <summary>Slices an already processed snapshot; expensive world capture runs through the existing chunk executor.</summary>
    public void Dispatch(PartitionRequest request, IPartitionSnapshot snapshot, Action<PartitionCompletion> complete)
    {
        var chunk = (NearFieldChunkSnapshot)snapshot;
        NearFieldCellContent content = chunk.Extract(request.Key.Coordinate);
        // Capture can discover more materials before publication; reserve the worst-case palette transfer.
        complete(new(request, snapshot, content, 4096L * (sizeof(uint) + 4 * sizeof(float)) + 1 + NearFieldMaterialRegistry.MaximumUploadBytes,
            content.Unsupported ? PartitionContentStatus.Unsupported : PartitionContentStatus.Supported));
    }

    /// <summary>Revalidates source availability/version and the current backend window around publication.</summary>
    public bool DependenciesValid(PartitionRequest request, IPartitionSnapshot snapshot)
    {
        var chunk = (NearFieldChunkSnapshot)snapshot;
        return backend.ContainsCell(request.Key.Coordinate) && source.IsCurrent(chunk.Key, chunk.Version);
    }

    /// <summary>Publishes coherent cell content only on coordinator authorization.</summary>
    public bool Publish(PartitionCompletion completion) => backend.ClaimCell(completion.Request) &&
        backend.PublishCell(completion.Request, ((NearFieldCellContent)completion.Content!).Cells, materials);

    /// <summary>Geometry participation needs no second upload after loaded readiness.</summary>
    public bool SetActive(in PartitionCellKey key, bool active) => true;

    /// <summary>Hides dirty content and removes its old dependency observation.</summary>
    public void Invalidate(in PartitionCellKey key)
    {
        backend.InvalidateCell(key);
        dependencies.Remove(key);
    }

    /// <summary>Returns physical ownership when the coordinator retires a cell.</summary>
    public void Retire(in PartitionCellKey key)
    {
        backend.RetireCell(key);
        unsupportedEnvelope.Remove(key);
        dependencies.Remove(key);
    }
    #endregion
}
