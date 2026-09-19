using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

[Flags]
internal enum LumOnWorldProbeImportanceFlags : byte
{
    None = 0,
    IndirectSunlight = 1 << 0,
    StackedAboveRainMap = 1 << 1,
    NearbySolidHit = 1 << 2,
}
