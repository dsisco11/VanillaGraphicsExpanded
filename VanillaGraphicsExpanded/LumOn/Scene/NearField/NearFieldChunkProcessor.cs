using System;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Copies only near-field geometry/light data through the existing chunk worker infrastructure.</summary>
internal sealed class NearFieldChunkProcessor : IChunkProcessor<NearFieldChunkSnapshot>
{
    public string Id => "LumOn.NearField.ChunkSnapshot";

    /// <summary>Detaches local contents from the pooled source lease before it can be released.</summary>
    public ValueTask<NearFieldChunkSnapshot> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (snapshot is IChunkSnapshotLease lease) snapshot = lease.InnerSnapshot;
        if (snapshot is not PooledChunkSnapshot<LumonSceneTraceSceneSourceCell> source || source.Voxels.Length != 32768)
            throw new ArgumentException("Expected the existing 32-block source snapshot.");
        var cells = new NearFieldSourceCell[32768];
        for (int i = 0; i < cells.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            cells[i] = source.Voxels.Span[i].NearField;
        }
        return ValueTask.FromResult(new NearFieldChunkSnapshot(snapshot.Key, snapshot.Version, cells));
    }
}
