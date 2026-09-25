namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Separates resolved geometry or sky from incomplete traversal; a finite clear segment is not sky.</summary>
internal enum WorldProbeTraceOutcome
{
    Invalid = 0,
    Hit = 1,
    Sky = 2,
    DistanceLimit = 3,
    BudgetExhausted = 4,
    Unavailable = 5,
}
