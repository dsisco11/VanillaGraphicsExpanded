using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal interface ISharedSnapshotLeaseProvider
{
    ValueTask<IChunkSnapshot?> TryAcquireSnapshotLeaseAsync(ChunkKey key, int version, CancellationToken ct);
}
