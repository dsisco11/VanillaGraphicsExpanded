using System;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal sealed class SharedSnapshotEntry
{
    private readonly SnapshotKey key;
    private readonly Lazy<Task<IChunkSnapshot?>> snapshotTask;

    private IChunkSnapshot? snapshot;
    private int refCount;

    private readonly Action<long> onSnapshotBytesAdd;
    private readonly Action<long> onSnapshotBytesRemove;

    public SharedSnapshotEntry(
        SnapshotKey key,
        Func<Task<IChunkSnapshot?>> snapshotFactory,
        Action<long> onSnapshotBytesAdd,
        Action<long> onSnapshotBytesRemove)
    {
        this.key = key;
        this.onSnapshotBytesAdd = onSnapshotBytesAdd;
        this.onSnapshotBytesRemove = onSnapshotBytesRemove;

        snapshotTask = new Lazy<Task<IChunkSnapshot?>>(snapshotFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SnapshotKey Key => key;

    public Task<IChunkSnapshot?> SnapshotTask => snapshotTask.Value;

    public async ValueTask<IChunkSnapshot?> GetOrAwaitSnapshotAsync(CancellationToken ct)
    {
        IChunkSnapshot? created = await SnapshotTask.WaitAsync(ct).ConfigureAwait(false);
        if (created is null)
        {
            return null;
        }

        if (Interlocked.CompareExchange(ref snapshot, created, null) is null)
        {
            long bytes = created is IChunkSnapshotSizeInfo sizeInfo ? sizeInfo.EstimatedBytes : 0;
            if (bytes > 0)
            {
                onSnapshotBytesAdd(bytes);
            }
        }

        return created;
    }

    public void AddRef() => Interlocked.Increment(ref refCount);

    public int ReleaseAndMaybeDispose()
    {
        int remaining = Interlocked.Decrement(ref refCount);
        if (remaining != 0)
        {
            return remaining;
        }

        IChunkSnapshot? toDispose = Interlocked.Exchange(ref snapshot, null);
        if (toDispose is not null)
        {
            long bytes = toDispose is IChunkSnapshotSizeInfo sizeInfo ? sizeInfo.EstimatedBytes : 0;
            if (bytes > 0)
            {
                onSnapshotBytesRemove(bytes);
            }

            toDispose.Dispose();
        }

        return 0;
    }
}
