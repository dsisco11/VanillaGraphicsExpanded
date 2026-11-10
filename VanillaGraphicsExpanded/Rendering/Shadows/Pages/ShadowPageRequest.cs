using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>One fixed-size page command carrying enclosure and exact scene/depth dependencies.</summary>
internal readonly record struct ShadowPageRequest(ShadowPageOperation Operation, ShadowPageKey Key,
    ShadowBounds Region, long WorldRevision, long SceneEpoch, long SceneSequence,
    long Assignment = 0, bool Complete = false)
{
    #region Public API
    /// <summary>Packs a 256-byte GPU command; full 64-bit identity is retained rather than hashed into equality.</summary>
    internal uint[] Pack()
    {
        if (!Enum.IsDefined(Operation) || ((Operation <= ShadowPageOperation.RetireKey || Operation == ShadowPageOperation.CancelReplacement)
            && (!Key.Source.IsKnown || Key.Projection <= 0 || !Region.IsValid || WorldRevision <= 0 || SceneEpoch <= 0 || SceneSequence < 0)))
            throw new ArgumentOutOfRangeException(nameof(Key));
        var w = new uint[64]; var s = Key.Source;
        Put(w, 0, s.World); w[2] = (uint)s.Dimension; w[3] = (uint)s.Kind; Put(w, 4, s.Owner); Put(w, 6, s.Lifetime);
        w[8] = (uint)s.Slot; w[9] = (uint)s.X; w[10] = (uint)s.Y; w[11] = (uint)s.Z;
        Put(w, 12, Key.Projection); w[14] = (uint)Key.FaceOrLevel; w[15] = (uint)Key.X; w[16] = (uint)Key.Y;
        w[20] = (uint)Operation; Put(w, 21, Assignment); w[27] = Complete ? 1u : 0u;
        Put(w, 28, BitConverter.DoubleToInt64Bits(Region.MinX)); Put(w, 30, BitConverter.DoubleToInt64Bits(Region.MinY));
        Put(w, 32, BitConverter.DoubleToInt64Bits(Region.MinZ)); Put(w, 34, BitConverter.DoubleToInt64Bits(Region.MaxX));
        Put(w, 36, BitConverter.DoubleToInt64Bits(Region.MaxY)); Put(w, 38, BitConverter.DoubleToInt64Bits(Region.MaxZ));
        Put(w, 40, WorldRevision); Put(w, 42, SceneEpoch); Put(w, 44, SceneSequence);
        return w;
    }
    /// <summary>Writes low/high words independent of signed interpretation on GPU.</summary>
    internal static void Put(Span<uint> words, int offset, long value) { words[offset] = (uint)value; words[offset + 1] = (uint)(value >> 32); }
    #endregion
}
