using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>One virtual projection region shared by geometry instances and multiple physical pages.</summary>
internal sealed class ShadowDrawView
{
    internal ShadowProjectionSnapshot Projection { get; }
    internal ShadowRasterRegion Region { get; }
    internal ShadowRasterRegion Uncached { get; }
    internal Vector3d Origin { get; }
    internal ImmutableArray<float> Matrix { get; }

    #region Public API
    /// <summary>Freezes directional raster and clipping rectangles in the same absolute virtual texel mapping.</summary>
    internal static ShadowDrawView Directional(DirectionalShadowProjection projection, int level,
        ShadowRasterRegion region, ShadowRasterRegion uncached, Vector3d origin) =>
        new(ShadowProjectionSnapshot.Directional(projection, level), region, uncached, origin,
            ShadowRasterProjection.Directional(projection, level, region, origin));

    /// <summary>Retains homogeneous source-plane behavior while permitting explicitly bounded face-region splits.</summary>
    internal static ShadowDrawView Point(PointShadowProjection projection, int face,
        ShadowRasterRegion region, ShadowRasterRegion uncached, Vector3d origin) =>
        new(ShadowProjectionSnapshot.Point(projection, face), region, uncached, origin,
            ShadowRasterProjection.Point(projection, face, region, origin));

    /// <summary>Packs sixteen RGBA32UI texels for texture-buffer access on every vertex-producing stage.</summary>
    internal void Pack(Span<uint> words, int capacity, int probes, int residentBanks, long batch)
    {
        if (words.Length != 64 || capacity < 1 || probes < 1 || residentBanks is < 1 or > 4 || batch < 1)
            throw new ArgumentException("Invalid view record or pool bounds.");
        words.Clear();
        for (int i = 0; i < 16; i++) words[i] = BitConverter.SingleToUInt32Bits(Matrix[i]);
        var x = SplitOrigin(Region.X, Region.Width, Projection.PageSize);
        var y = SplitOrigin(Region.Y, Region.Height, Projection.PageSize);
        words[16] = (uint)x.Texel; words[17] = (uint)y.Texel;
        words[18] = (uint)Region.Width; words[19] = (uint)Region.Height;
        words[20] = (uint)(Uncached.X - Region.X); words[21] = (uint)(Uncached.Y - Region.Y);
        words[22] = (uint)(Uncached.X - Region.X + Uncached.Width);
        words[23] = (uint)(Uncached.Y - Region.Y + Uncached.Height);
        var key = new ShadowPageKey(Projection.Source, Projection.Generation, Projection.FaceOrLevel, x.Page, y.Page);
        ShadowPageRequest.WriteKey(words[24..41], key);
        ShadowPageRequest.Put(words, 44, BitConverter.DoubleToInt64Bits(Origin.X));
        ShadowPageRequest.Put(words, 46, BitConverter.DoubleToInt64Bits(Origin.Y));
        ShadowPageRequest.Put(words, 48, BitConverter.DoubleToInt64Bits(Origin.Z));
        words[52] = (uint)capacity; words[53] = (uint)probes; words[54] = (uint)Projection.PageSize;
        words[55] = (uint)residentBanks; ShadowPageRequest.Put(words, 56, batch);
    }
    #endregion

    #region Private
    /// <summary>Splits absolute texels into an exact signed page origin and bounded local offset without narrowing world coordinates.</summary>
    private static (int Page, int Texel) SplitOrigin(long start, int length, int pageSize)
    {
        // C# division truncates toward zero; normalize the remainder to obtain floor
        // division at negative origins. The whole admitted region must fit page keys.
        long first = Math.DivRem(start, pageSize, out long offset);
        if (offset < 0) { first--; offset += pageSize; }
        long lastTexel = checked(start + length - 1);
        long last = Math.DivRem(lastTexel, pageSize, out long lastOffset);
        if (lastOffset < 0) last--;
        if (first < int.MinValue || last > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(start), "Virtual raster region exceeds signed page addressing.");
        return (checked((int)first), checked((int)offset));
    }

    /// <summary>Requires complete finite region and origin data before publishing immutable raster inputs.</summary>
    private ShadowDrawView(ShadowProjectionSnapshot projection, ShadowRasterRegion region,
        ShadowRasterRegion uncached, Vector3d origin, float[] matrix)
    {
        region.Validate(); uncached.Validate();
        if (!region.Contains(uncached) || !ShadowProjectionMath.Finite(origin))
            throw new ArgumentException("Uncached rectangle or world origin is outside the represented view.");
        Projection = projection; Region = region; Uncached = uncached; Origin = origin; Matrix = ImmutableArray.Create(matrix);
    }
    #endregion
}
