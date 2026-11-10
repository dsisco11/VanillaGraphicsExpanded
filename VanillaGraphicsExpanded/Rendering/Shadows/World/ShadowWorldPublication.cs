using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.World;

/// <summary>Freezes coordinate lookup, geometry descriptions and the complete per-dimension loaded envelope.</summary>
internal sealed record ShadowWorldPublication(long Generation, bool Complete, string? Failure,
    ImmutableArray<ShadowChunkGpu> Table, ImmutableArray<ShadowGeometryRange> Ranges,
    ImmutableDictionary<int, ShadowBounds> Envelopes, int ProbeLimit)
{
    /// <summary>Scopes revisions to one adapter lifetime, including reconfiguration and reconnect.</summary>
    internal long Owner { get; init; }
    /// <summary>Accounts packed table/range storage separately from native geometry ownership.</summary>
    internal long PackedBytes => checked((long)Table.Length * 64 + (long)Ranges.Length * 32);

    #region Public API
    /// <summary>Mirrors the bounded GPU lookup for diagnostics; an absent interior key remains unknown.</summary>
    internal bool TryFind(ShadowChunkIdentity key, out ShadowChunkGpu value)
    {
        value = default;
        if (!Complete || Table.IsDefaultOrEmpty) return false;
        uint mask = (uint)Table.Length - 1;
        for (uint probe = 0; probe < ProbeLimit; probe++)
        {
            var entry = Table[(int)((key.Hash() + probe) & mask)];
            if (entry.Flags == 0) return false;
            if (entry.Dimension == key.Dimension && entry.X == key.X && entry.Y == key.Y && entry.Z == key.Z)
            { value = entry; return true; }
        }
        return false;
    }
    #endregion
}
