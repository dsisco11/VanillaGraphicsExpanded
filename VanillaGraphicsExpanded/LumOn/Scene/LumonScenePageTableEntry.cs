using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Packed GPU page-table entry (v1).
/// Stored as <c>R32UI</c> per virtual page texel.
/// </summary>
internal readonly struct LumonScenePageTableEntry : IEquatable<LumonScenePageTableEntry>
{
    public readonly uint Packed;

    public LumonScenePageTableEntry(uint packed)
    {
        Packed = packed;
    }

    public bool Equals(LumonScenePageTableEntry other)
        => Packed == other.Packed;

    public override bool Equals(object? obj)
        => obj is LumonScenePageTableEntry other && Equals(other);

    public override int GetHashCode()
        => Packed.GetHashCode();

    public static bool operator ==(LumonScenePageTableEntry left, LumonScenePageTableEntry right)
        => left.Equals(right);

    public static bool operator !=(LumonScenePageTableEntry left, LumonScenePageTableEntry right)
        => !left.Equals(right);
}

internal static class LumonScenePageTableEntryPacking
{
    // v1: 24-bit physical page id + 8-bit flags.
    // physicalPageId is 1-based (0 = invalid/unmapped).
    private const int PhysicalPageIdBits = 24;
    private const uint PhysicalPageIdMask = (1u << PhysicalPageIdBits) - 1u;

    public const int FlagShift = PhysicalPageIdBits;
    public const uint FlagsMask = 0xFFu;

    [Flags]
    public enum Flags : byte
    {
        None = 0,

        /// <summary>Page-table entry has a valid physical mapping.</summary>
        Resident = 1 << 0,

        /// <summary>Capture is required before this page can be used.</summary>
        NeedsCapture = 1 << 1,

        /// <summary>Relight is required before this page can be used.</summary>
        NeedsRelight = 1 << 2,

        /// <summary>Page is currently being captured.</summary>
        Capturing = 1 << 3,

        /// <summary>Page is currently being relit.</summary>
        Relighting = 1 << 4,
    }

    public static LumonScenePageTableEntry Pack(uint physicalPageId, Flags flags)
    {
        if ((physicalPageId & ~PhysicalPageIdMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalPageId), physicalPageId, $"physicalPageId must fit in {PhysicalPageIdBits} bits.");
        }

        uint f = (uint)flags;
        if ((f & ~FlagsMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags), flags, "flags must fit in 8 bits.");
        }

        uint packed = (physicalPageId & PhysicalPageIdMask) | ((f & FlagsMask) << FlagShift);
        return new LumonScenePageTableEntry(packed);
    }

    public static uint UnpackPhysicalPageId(in LumonScenePageTableEntry entry)
        => entry.Packed & PhysicalPageIdMask;

    public static Flags UnpackFlags(in LumonScenePageTableEntry entry)
        => (Flags)((entry.Packed >> FlagShift) & FlagsMask);
}

