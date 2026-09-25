using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Lifetime measurements; GPU and latency totals cover only collected samples, not skipped dispatches.</summary>
internal readonly record struct SurfaceWorkMeasurement(
    long Submitted, long Collected, long Skipped, long ReadFailures, long Pages,
    double SubmitMilliseconds, double GpuMilliseconds, double CompletionMilliseconds,
    ImmutableArray<ulong> Counters);
