using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>One diagnostic sample; bytes describe payload storage and API transfers, not driver allocations.</summary>
internal sealed record NearFieldRuntimeMetrics(long Instance, PartitionPoint Camera, int Resolution,
    double OriginRadius, double PrefetchMargin, long SourceReads, long CacheHits, int SourceInFlight,
    long SnapshotBytes, long TextureBytes, long UploadedBytes, long PublishedCells,
    double LastUpdateMilliseconds, double MeanUpdateMilliseconds, double PeakUpdateMilliseconds, long UpdateCount,
    long CaptureCount, double CaptureMilliseconds, double PeakCaptureMilliseconds);
