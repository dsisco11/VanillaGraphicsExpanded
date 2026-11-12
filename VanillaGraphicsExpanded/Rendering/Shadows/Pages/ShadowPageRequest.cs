using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>One fixed-size page command carrying enclosure and exact scene/depth dependencies.</summary>
internal readonly record struct ShadowPageRequest(ShadowPageOperation Operation, ShadowPageKey Key,
    ShadowBounds Region, long WorldRevision, long SceneEpoch, long SceneSequence,
    long Assignment = 0, bool Complete = false, ShadowProjectionSnapshot? Projection = null, double Timestamp = 0,
    long CurrentProjection = 0, bool ReuseCurrent = false)
{
    #region Public API
    /// <summary>Packs a 512-byte GPU command with retained projection and full 64-bit identity.</summary>
    internal uint[] Pack()
    {
        if (!Enum.IsDefined(Operation) || !double.IsFinite(Timestamp) || Timestamp < 0 ||
            ((Operation <= ShadowPageOperation.RetireKey || Operation is ShadowPageOperation.CancelReplacement or ShadowPageOperation.Invalidate or ShadowPageOperation.SelectReplacement or ShadowPageOperation.ValidateCurrent or ShadowPageOperation.ValidateSource)
            && (!Key.Source.IsKnown || Key.Projection <= 0 || !Region.IsValid || WorldRevision <= 0 || SceneEpoch <= 0 || SceneSequence < 0)))
            throw new ArgumentOutOfRangeException(nameof(Key));
        if ((Operation is ShadowPageOperation.Allocate or ShadowPageOperation.Publish) && Projection == null)
            throw new ArgumentException("Replacement depths require their rendered projection.", nameof(Projection));
        if (Projection != null && (Projection.Source != Key.Source || Projection.Generation != Key.Projection || Projection.FaceOrLevel != Key.FaceOrLevel))
            throw new ArgumentException("Rendered projection and virtual page identity disagree.", nameof(Projection));
        var w = new uint[128];
        WriteKey(w, Key);
        w[20] = (uint)Operation; Put(w, 21, Assignment); w[27] = Complete ? 1u : 0u;
        Put(w, 28, BitConverter.DoubleToInt64Bits(Region.MinX)); Put(w, 30, BitConverter.DoubleToInt64Bits(Region.MinY));
        Put(w, 32, BitConverter.DoubleToInt64Bits(Region.MinZ)); Put(w, 34, BitConverter.DoubleToInt64Bits(Region.MaxX));
        Put(w, 36, BitConverter.DoubleToInt64Bits(Region.MaxY)); Put(w, 38, BitConverter.DoubleToInt64Bits(Region.MaxZ));
        Put(w, 40, WorldRevision); Put(w, 42, SceneEpoch); Put(w, 44, SceneSequence);
        Put(w, 46, BitConverter.DoubleToInt64Bits(Timestamp));
        Projection?.WriteTo(w);
        w[125] = ReuseCurrent ? 1u : 0u;
        if(Operation==ShadowPageOperation.ValidateCurrent)
        { if(CurrentProjection<=0)throw new ArgumentOutOfRangeException(nameof(CurrentProjection));Put(w,126,CurrentProjection); }
        return w;
    }
    /// <summary>Writes the common exact identity without inventing a page operation for other record types.</summary>
    internal static void WriteKey(Span<uint> words, ShadowPageKey key)
    {
        if (words.Length < 17) throw new ArgumentException("A page key requires seventeen words.", nameof(words));
        var source = key.Source;
        Put(words, 0, source.World); words[2] = (uint)source.Dimension; words[3] = (uint)source.Kind;
        Put(words, 4, source.Owner); Put(words, 6, source.Lifetime);
        words[8] = (uint)source.Slot; words[9] = (uint)source.X; words[10] = (uint)source.Y; words[11] = (uint)source.Z;
        Put(words, 12, key.Projection); words[14] = (uint)key.FaceOrLevel; words[15] = (uint)key.X; words[16] = (uint)key.Y;
    }
    /// <summary>Writes low/high words independent of signed interpretation on GPU.</summary>
    internal static void Put(Span<uint> words, int offset, long value) { words[offset] = (uint)value; words[offset + 1] = (uint)(value >> 32); }
    #endregion
}
