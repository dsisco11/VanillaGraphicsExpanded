namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal enum LumOnWorldProbeLifecycleState : byte
{
    Uninitialized = 0,
    Valid = 1,
    Stale = 2,
    Dirty = 3,
    InFlight = 4,
}
