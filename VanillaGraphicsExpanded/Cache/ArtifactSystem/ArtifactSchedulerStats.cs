namespace VanillaGraphicsExpanded.Cache.Artifacts;

internal readonly record struct ArtifactSchedulerStats(
    long Queued,
    long InFlight,
    long Completed,
    long Errors,
    double AvgComputeMs,
    double AvgOutputMs);
