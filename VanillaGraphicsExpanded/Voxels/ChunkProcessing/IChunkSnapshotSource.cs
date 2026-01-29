using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public interface IChunkSnapshotSource
{
    /// <summary>
    /// Creates a copy-based snapshot off-thread for <paramref name="key"/> at <paramref name="expectedVersion"/>.
    /// </summary>
    /// <remarks>
    /// Any synchronization required to copy safely is handled externally (outside the chunk processing system).
    /// Returning <see langword="null"/> indicates the chunk cannot be snapshotted (e.g., unloaded or otherwise unavailable).
    /// </remarks>
    ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct);
}
