namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Identifies why an unresolved probe batch must not publish lighting.</summary>
internal enum WorldProbeTraceFailureReason
{
    None = 0,
    // Preserve the existing scheduler's unavailable-data retry backoff.
    Aborted = 1,
    Exception = 2,
    DistanceLimit = 3,
    BudgetExhausted = 4,
    Invalid = 5,
}
