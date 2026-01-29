using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal sealed class SharedSnapshotLease : IChunkSnapshot
{
    private readonly IChunkSnapshot snapshot;
    private readonly Action release;

    private int disposed;

    public SharedSnapshotLease(IChunkSnapshot snapshot, Action release)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public ChunkKey Key => snapshot.Key;

    public int Version => snapshot.Version;

    public int SizeX => snapshot.SizeX;

    public int SizeY => snapshot.SizeY;

    public int SizeZ => snapshot.SizeZ;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        release();
    }
}
