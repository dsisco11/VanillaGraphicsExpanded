using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

[Flags]
internal enum LumOnWorldProbeFlags : uint
{
    None = 0,

    /// <summary>
    /// Probe payload is usable by shading.
    /// </summary>
    Valid = 1u << 0,

    /// <summary>
    /// Trace found no meaningful geometry; payload represents sky/ambient.
    /// </summary>
    SkyOnly = 1u << 1,

    /// <summary>
    /// Probe payload is valid but scheduled for refresh.
    /// </summary>
    Stale = 1u << 2,

    /// <summary>
    /// A CPU job is currently producing a newer payload.
    /// </summary>
    InFlight = 1u << 3,
}
