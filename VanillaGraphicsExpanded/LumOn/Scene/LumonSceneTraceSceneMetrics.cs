using System.Threading;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.7: Lightweight counters for TraceScene scheduling + GPU updates.
/// These are intended for debug overlays and EventSource polling counters.
/// </summary>
internal static class LumonSceneTraceSceneMetrics
{
    private static long regionsUploaded;
    private static long regionsDispatched;
    private static long bytesUploaded;

    private static long computeDispatchCount;

    private static int queueLength;
    private static int queueHighLength;
    private static int queueLowLength;
    private static int suppressedCooldown;
    private static int inFlight;
    private static int appliedRegions;

    private static long cooldownSkips;

    private static long regionCompleteSuccess;
    private static long regionCompleteChunkUnavailable;
    private static long regionCompleteCanceled;
    private static long regionCompleteSuperseded;
    private static long regionCompleteFailed;

    private static long regionRequestsIssued;
    private static long snapshotsRequested;
    private static long snapshotsUnavailable;
    private static long snapshotsSucceeded;

    private static long snapshotFailCanceled;
    private static long snapshotFailChunkMissing;
    private static long snapshotFailUnpack;
    private static long snapshotFailException;

    private static int lastSnapshotNonAirCells;
    private static int lastSnapshotSolidCells;
    private static int lastSnapshotChunkFound;
    private static int lastSnapshotBulkCloneUsed;
    private static int lastSnapshotBlockId0;
    private static int lastSnapshotBlockIdCenter;
    private static int lastSnapshotBlockIdLast;
    private static int lastSnapshotDirectBlockIdCenter;
    private static int lastSnapshotChunkX;
    private static int lastSnapshotChunkY;
    private static int lastSnapshotChunkZ;
    private static int lastSnapshotExpectedVersion;
    private static int lastSnapshotCurrentVersion;

    public static long RegionsUploaded => Interlocked.Read(ref regionsUploaded);
    public static long RegionsDispatched => Interlocked.Read(ref regionsDispatched);
    public static long BytesUploaded => Interlocked.Read(ref bytesUploaded);
    public static long ComputeDispatchCount => Interlocked.Read(ref computeDispatchCount);

    public static long RegionRequestsIssued => Interlocked.Read(ref regionRequestsIssued);
    public static long SnapshotsRequested => Interlocked.Read(ref snapshotsRequested);
    public static long SnapshotsUnavailable => Interlocked.Read(ref snapshotsUnavailable);
    public static long SnapshotsSucceeded => Interlocked.Read(ref snapshotsSucceeded);

    public static long SnapshotFailCanceled => Interlocked.Read(ref snapshotFailCanceled);
    public static long SnapshotFailChunkMissing => Interlocked.Read(ref snapshotFailChunkMissing);
    public static long SnapshotFailUnpack => Interlocked.Read(ref snapshotFailUnpack);
    public static long SnapshotFailException => Interlocked.Read(ref snapshotFailException);

    public static int LastSnapshotNonAirCells => Volatile.Read(ref lastSnapshotNonAirCells);
    public static int LastSnapshotSolidCells => Volatile.Read(ref lastSnapshotSolidCells);
    public static int LastSnapshotChunkFound => Volatile.Read(ref lastSnapshotChunkFound);
    public static int LastSnapshotBulkCloneUsed => Volatile.Read(ref lastSnapshotBulkCloneUsed);
    public static int LastSnapshotBlockId0 => Volatile.Read(ref lastSnapshotBlockId0);
    public static int LastSnapshotBlockIdCenter => Volatile.Read(ref lastSnapshotBlockIdCenter);
    public static int LastSnapshotBlockIdLast => Volatile.Read(ref lastSnapshotBlockIdLast);
    public static int LastSnapshotDirectBlockIdCenter => Volatile.Read(ref lastSnapshotDirectBlockIdCenter);
    public static int LastSnapshotChunkX => Volatile.Read(ref lastSnapshotChunkX);
    public static int LastSnapshotChunkY => Volatile.Read(ref lastSnapshotChunkY);
    public static int LastSnapshotChunkZ => Volatile.Read(ref lastSnapshotChunkZ);
    public static int LastSnapshotExpectedVersion => Volatile.Read(ref lastSnapshotExpectedVersion);
    public static int LastSnapshotCurrentVersion => Volatile.Read(ref lastSnapshotCurrentVersion);

    public static int QueueLength => Volatile.Read(ref queueLength);
    public static int QueueHighLength => Volatile.Read(ref queueHighLength);
    public static int QueueLowLength => Volatile.Read(ref queueLowLength);
    public static int SuppressedCooldown => Volatile.Read(ref suppressedCooldown);
    public static int InFlight => Volatile.Read(ref inFlight);
    public static int AppliedRegions => Volatile.Read(ref appliedRegions);

    public static long CooldownSkips => Interlocked.Read(ref cooldownSkips);

    public static long RegionCompleteSuccess => Interlocked.Read(ref regionCompleteSuccess);
    public static long RegionCompleteChunkUnavailable => Interlocked.Read(ref regionCompleteChunkUnavailable);
    public static long RegionCompleteCanceled => Interlocked.Read(ref regionCompleteCanceled);
    public static long RegionCompleteSuperseded => Interlocked.Read(ref regionCompleteSuperseded);
    public static long RegionCompleteFailed => Interlocked.Read(ref regionCompleteFailed);

    public static void SetState(int queueHighLength, int queueLowLength, int inFlight, int appliedRegions, int suppressed = 0)
    {
        int total = queueHighLength + queueLowLength;
        Volatile.Write(ref LumonSceneTraceSceneMetrics.queueLength, total);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.queueHighLength, queueHighLength);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.queueLowLength, queueLowLength);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.suppressedCooldown, suppressed);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.inFlight, inFlight);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.appliedRegions, appliedRegions);
    }

    public static void OnCooldownSkip()
    {
        Interlocked.Increment(ref cooldownSkips);
    }

    public static void OnRegionCompleted(ChunkWorkStatus status)
    {
        switch (status)
        {
            case ChunkWorkStatus.Success:
                Interlocked.Increment(ref regionCompleteSuccess);
                break;

            case ChunkWorkStatus.ChunkUnavailable:
                Interlocked.Increment(ref regionCompleteChunkUnavailable);
                break;

            case ChunkWorkStatus.Canceled:
                Interlocked.Increment(ref regionCompleteCanceled);
                break;

            case ChunkWorkStatus.Superseded:
                Interlocked.Increment(ref regionCompleteSuperseded);
                break;

            case ChunkWorkStatus.Failed:
                Interlocked.Increment(ref regionCompleteFailed);
                break;

            default:
                break;
        }
    }

    public static void OnUploaded(int regions, long bytes)
    {
        if (regions > 0) Interlocked.Add(ref regionsUploaded, regions);
        if (bytes > 0) Interlocked.Add(ref bytesUploaded, bytes);
    }

    public static void OnDispatched(int regions)
    {
        if (regions > 0) Interlocked.Add(ref regionsDispatched, regions);
        Interlocked.Increment(ref computeDispatchCount);
    }

    public static void OnRegionRequestsIssued(int regions)
    {
        if (regions > 0) Interlocked.Add(ref regionRequestsIssued, regions);
    }

    public static void OnSnapshotRequested()
    {
        Interlocked.Increment(ref snapshotsRequested);
    }

    public static void OnSnapshotUnavailable()
    {
        Interlocked.Increment(ref snapshotsUnavailable);
    }

    public static void OnSnapshotFailedCanceled()
    {
        Interlocked.Increment(ref snapshotFailCanceled);
    }

    public static void OnSnapshotFailedChunkMissing()
    {
        Interlocked.Increment(ref snapshotFailChunkMissing);
    }

    public static void OnSnapshotFailedUnpack()
    {
        Interlocked.Increment(ref snapshotFailUnpack);
    }

    public static void OnSnapshotFailedException()
    {
        Interlocked.Increment(ref snapshotFailException);
    }

    public static void OnSnapshotSucceeded()
    {
        Interlocked.Increment(ref snapshotsSucceeded);
    }

    public static void SetLastSnapshotInfo(
        int chunkX,
        int chunkY,
        int chunkZ,
        int expectedVersion,
        int currentVersion,
        bool chunkFound,
        bool bulkCloneUsed,
        int blockId0,
        int blockIdCenter,
        int blockIdLast,
        int directBlockIdCenter,
        int nonAirCells,
        int solidCells)
    {
        Volatile.Write(ref lastSnapshotChunkX, chunkX);
        Volatile.Write(ref lastSnapshotChunkY, chunkY);
        Volatile.Write(ref lastSnapshotChunkZ, chunkZ);
        Volatile.Write(ref lastSnapshotExpectedVersion, expectedVersion);
        Volatile.Write(ref lastSnapshotCurrentVersion, currentVersion);
        Volatile.Write(ref lastSnapshotChunkFound, chunkFound ? 1 : 0);
        Volatile.Write(ref lastSnapshotBulkCloneUsed, bulkCloneUsed ? 1 : 0);
        Volatile.Write(ref lastSnapshotBlockId0, blockId0);
        Volatile.Write(ref lastSnapshotBlockIdCenter, blockIdCenter);
        Volatile.Write(ref lastSnapshotBlockIdLast, blockIdLast);
        Volatile.Write(ref lastSnapshotDirectBlockIdCenter, directBlockIdCenter);
        Volatile.Write(ref lastSnapshotNonAirCells, nonAirCells);
        Volatile.Write(ref lastSnapshotSolidCells, solidCells);
    }
}
