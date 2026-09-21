using System;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Detaches shared geometry source data using the existing bounded chunk executor.</summary>
internal sealed class TraceGeometryChunkProcessor : IChunkProcessor<TraceGeometryChunk>
{
    public string Id => "LumOn.TraceGeometry.Shared";
    /// <summary>Copies a pooled snapshot before its worker lease is released.</summary>
    public ValueTask<TraceGeometryChunk> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (snapshot is IChunkSnapshotLease lease) snapshot = lease.InnerSnapshot;
        if (snapshot is not PooledChunkSnapshot<TraceGeometryVoxel> source) throw new ArgumentException("Expected shared geometry source.");
        return ValueTask.FromResult(new TraceGeometryChunk(source.Key, source.Version, source.Voxels.Span));
    }
}
