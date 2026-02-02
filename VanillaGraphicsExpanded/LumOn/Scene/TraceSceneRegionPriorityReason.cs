using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

[Flags]
internal enum TraceSceneRegionPriorityReason : ushort
{
    None = 0,

    InWindow = 1 << 0,
    Distance = 1 << 1,
    Stale = 1 << 2,
    NeverApplied = 1 << 3,
    SeenLoadedRecently = 1 << 4,
    MissingPenalty = 1 << 5,
    CooldownSuppressed = 1 << 6,
}
