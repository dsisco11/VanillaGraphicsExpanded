using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Compact bitset for <see cref="GlPipelineStateId"/> (Phase 1: <c>ulong</c>, up to 64 knobs).
/// </summary>
internal readonly struct GlPipelineStateMask : IEquatable<GlPipelineStateMask>
{
    public ulong Bits { get; }

    public GlPipelineStateMask(ulong bits)
    {
        Bits = bits;
    }

    public bool IsEmpty => Bits == 0;

    public bool Contains(GlPipelineStateId id) => (Bits & Bit(id)) != 0;

    public GlPipelineStateMask With(GlPipelineStateId id) => new(Bits | Bit(id));

    public GlPipelineStateMask Without(GlPipelineStateId id) => new(Bits & ~Bit(id));

    public static GlPipelineStateMask From(GlPipelineStateId id) => new(Bit(id));

    public static ulong Bit(GlPipelineStateId id)
    {
        int bitIndex = (int)id;
#if DEBUG
        if ((uint)bitIndex >= (uint)GlPipelineStateId.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "State id is out of range.");
        }
#endif
        return 1UL << bitIndex;
    }

    public static ulong ValidBits
    {
        get
        {
            int count = (int)GlPipelineStateId.Count;
            if (count <= 0) return 0;
            if (count >= 64) return ulong.MaxValue;
            return (1UL << count) - 1UL;
        }
    }

    public override string ToString() => $"0x{Bits:X}";

    public bool Equals(GlPipelineStateMask other) => Bits == other.Bits;

    public override bool Equals(object? obj) => obj is GlPipelineStateMask other && Equals(other);

    public override int GetHashCode() => Bits.GetHashCode();

    public static GlPipelineStateMask operator |(GlPipelineStateMask a, GlPipelineStateMask b) => new(a.Bits | b.Bits);

    public static GlPipelineStateMask operator &(GlPipelineStateMask a, GlPipelineStateMask b) => new(a.Bits & b.Bits);

    public static bool operator ==(GlPipelineStateMask a, GlPipelineStateMask b) => a.Bits == b.Bits;

    public static bool operator !=(GlPipelineStateMask a, GlPipelineStateMask b) => a.Bits != b.Bits;
}

