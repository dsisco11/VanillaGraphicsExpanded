using System.Threading;

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
    private static int inFlight;
    private static int appliedRegions;

    public static long RegionsUploaded => Interlocked.Read(ref regionsUploaded);
    public static long RegionsDispatched => Interlocked.Read(ref regionsDispatched);
    public static long BytesUploaded => Interlocked.Read(ref bytesUploaded);
    public static long ComputeDispatchCount => Interlocked.Read(ref computeDispatchCount);

    public static int QueueLength => Volatile.Read(ref queueLength);
    public static int InFlight => Volatile.Read(ref inFlight);
    public static int AppliedRegions => Volatile.Read(ref appliedRegions);

    public static void SetState(int queueLength, int inFlight, int appliedRegions)
    {
        Volatile.Write(ref LumonSceneTraceSceneMetrics.queueLength, queueLength);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.inFlight, inFlight);
        Volatile.Write(ref LumonSceneTraceSceneMetrics.appliedRegions, appliedRegions);
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
}

