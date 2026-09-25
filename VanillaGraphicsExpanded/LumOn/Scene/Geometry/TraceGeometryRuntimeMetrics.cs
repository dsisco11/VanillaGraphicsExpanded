using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Detached shared-pipeline measurements; payload bytes exclude object overhead and driver allocations.</summary>
internal sealed record TraceGeometryRuntimeMetrics(long Instance, int Resolution, long SourceReads, int SourceInFlight,
    long SnapshotBytes, long StagedPayloadBytes, long TextureBytes, long UploadedBytes, long PublishedCells,
    double LastUpdateMilliseconds, double MeanUpdateMilliseconds, double PeakUpdateMilliseconds,
    int NearRequired, int SurfaceRequired, int Overlap, PartitionStatistics Residency, long CaptureIdentityBytes = 0,
    int SurfaceResolution = 0, TraceGeometryWorkBudget? WorkBudget = null)
{
    /// <summary>Latest detached sample for profiling callbacks that run off the render thread.</summary>
    public static TraceGeometryRuntimeMetrics? Current { get; internal set; }
}
