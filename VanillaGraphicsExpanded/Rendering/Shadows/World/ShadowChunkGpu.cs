using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shadows.World;

/// <summary>Explicit 64-byte std430 lookup record; 64-bit identities use low/high words on GL 4.5.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ShadowChunkGpu
{
    internal int X, Y, Z, Dimension;
    internal uint NativeLow, NativeHigh, LifetimeLow, LifetimeHigh;
    internal uint RevisionLow, RevisionHigh, Flags, Connectivity;
    internal uint RangeOffset, RangeCount, Reserved0, Reserved1;
    // Flags: occupied=1, geometry ready=2, cached empty=4, empty fresh=8,
    // cached connectivity fresh=16. No metadata authorizes pruning without opacity proof.
    // Reserved0 bit 0 retains the native asynchronous freshness claim for diagnostics only.
}
