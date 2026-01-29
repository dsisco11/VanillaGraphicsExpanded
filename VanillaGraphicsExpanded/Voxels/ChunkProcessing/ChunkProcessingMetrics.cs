using System.Threading;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal static class ChunkProcessingMetrics
{
    private static long queueLength;
    private static long inFlight;

    private static long completedSuccess;
    private static long completedSuperseded;
    private static long completedCanceled;
    private static long completedFailed;
    private static long completedChunkUnavailable;

    private static long snapshotBytesInUse;

    private static long artifactCacheBytesInUse;
    private static long artifactCacheHits;
    private static long artifactCacheEvictions;

    public static long QueueLength => Interlocked.Read(ref queueLength);

    public static long InFlight => Interlocked.Read(ref inFlight);

    public static long CompletedSuccess => Interlocked.Read(ref completedSuccess);

    public static long CompletedSuperseded => Interlocked.Read(ref completedSuperseded);

    public static long CompletedCanceled => Interlocked.Read(ref completedCanceled);

    public static long CompletedFailed => Interlocked.Read(ref completedFailed);

    public static long CompletedChunkUnavailable => Interlocked.Read(ref completedChunkUnavailable);

    public static long SnapshotBytesInUse => Interlocked.Read(ref snapshotBytesInUse);

    public static long ArtifactCacheBytesInUse => Interlocked.Read(ref artifactCacheBytesInUse);

    public static long ArtifactCacheHits => Interlocked.Read(ref artifactCacheHits);

    public static long ArtifactCacheEvictions => Interlocked.Read(ref artifactCacheEvictions);

    public static void OnEnqueued() => Interlocked.Increment(ref queueLength);

    public static void OnDequeued() => Interlocked.Decrement(ref queueLength);

    public static void OnInFlightAdded() => Interlocked.Increment(ref inFlight);

    public static void OnInFlightRemoved() => Interlocked.Decrement(ref inFlight);

    public static void OnCompleted(ChunkWorkStatus status)
    {
        switch (status)
        {
            case ChunkWorkStatus.Success:
                Interlocked.Increment(ref completedSuccess);
                break;
            case ChunkWorkStatus.Superseded:
                Interlocked.Increment(ref completedSuperseded);
                break;
            case ChunkWorkStatus.Canceled:
                Interlocked.Increment(ref completedCanceled);
                break;
            case ChunkWorkStatus.Failed:
                Interlocked.Increment(ref completedFailed);
                break;
            case ChunkWorkStatus.ChunkUnavailable:
                Interlocked.Increment(ref completedChunkUnavailable);
                break;
        }
    }

    public static void OnSnapshotBytesDelta(long deltaBytes) => Interlocked.Add(ref snapshotBytesInUse, deltaBytes);

    public static void SetArtifactCacheBytesInUse(long bytes) => Interlocked.Exchange(ref artifactCacheBytesInUse, bytes);

    public static void OnArtifactCacheHit() => Interlocked.Increment(ref artifactCacheHits);

    public static void OnArtifactCacheEviction() => Interlocked.Increment(ref artifactCacheEvictions);
}
