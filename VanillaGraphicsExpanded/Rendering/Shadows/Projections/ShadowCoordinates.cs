using System;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Prepares translated FP32 frames while retaining precise origins at frame boundaries.</summary>
internal static class ShadowCoordinates
{
    internal const int SamplingWords = 192;
    internal const int FrameOffset = 160;

    #region Public API
    #region Origins and precision
    /// <summary>Splits an absolute origin before any GPU subtraction can discard its bounded remainder.</summary>
    internal static void Split(Vector3d origin, out Vector3 high, out Vector3 low)
    {
        high = new((float)origin.X, (float)origin.Y, (float)origin.Z);
        low = new((float)(origin.X - high.X), (float)(origin.Y - high.Y), (float)(origin.Z - high.Z));
        if (!float.IsFinite(high.X) || !float.IsFinite(high.Y) || !float.IsFinite(high.Z) ||
            !float.IsFinite(low.X) || !float.IsFinite(low.Y) || !float.IsFinite(low.Z))
            throw new ArgumentOutOfRangeException(nameof(origin));
    }

    /// <summary>Bounds the rounding discarded by the high/low origin representation.</summary>
    internal static double OriginError(Vector3d origin)
    {
        Split(origin, out var high, out var low);
        return Math.Max(Math.Abs(origin.X - ((double)high.X + low.X)),
            Math.Max(Math.Abs(origin.Y - ((double)high.Y + low.Y)), Math.Abs(origin.Z - ((double)high.Z + low.Z))));
    }

    /// <summary>Reserves coordinate arithmetic and high/low representation error in conservative receiving demand.</summary>
    internal static double DemandPadding(ShadowBounds bounds)
    {
        double extent = Math.Max(1, Math.Max(bounds.MaxX - bounds.MinX,
            Math.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        double magnitude = Math.Max(Math.Max(Math.Abs(bounds.MinX), Math.Abs(bounds.MaxX)),
            Math.Max(Math.Max(Math.Abs(bounds.MinY), Math.Abs(bounds.MaxY)), Math.Max(Math.Abs(bounds.MinZ), Math.Abs(bounds.MaxZ))));
        // A generous float operation allowance covers translated view reconstruction and projection;
        // the second term bounds splitting finite native-world origins into two float components.
        return Math.ScaleB(extent, -18) + Math.ScaleB(Math.Max(1, magnitude), -47);
    }

    /// <summary>Bounds surface translation through both the rendered sampling frame and local coverage frame.</summary>
    internal static double SurfacePadding(ShadowBounds bounds, Vector3d cameraOrigin, Vector3d samplingOrigin)
    {
        double relative = Math.Max(Math.Max(Math.Abs(bounds.MinX - cameraOrigin.X), Math.Abs(bounds.MaxX - cameraOrigin.X)),
            Math.Max(Math.Max(Math.Abs(bounds.MinY - cameraOrigin.Y), Math.Abs(bounds.MaxY - cameraOrigin.Y)),
                Math.Max(Math.Abs(bounds.MinZ - cameraOrigin.Z), Math.Abs(bounds.MaxZ - cameraOrigin.Z))));
        var delta = cameraOrigin - samplingOrigin;
        double translation = Math.Max(Math.Abs(delta.X), Math.Max(Math.Abs(delta.Y), Math.Abs(delta.Z)));
        // A snapped coverage center lies within the receiving box plus half a chunk.
        // Allow twice the fragment guard's rounding coefficient, including bound growth
        // and outward float packing, before either projection or coverage translation.
        double arithmetic = Math.ScaleB(Math.Max(1, relative + Math.Max(translation, relative + 32)), -20);
        return DemandPadding(bounds) + arithmetic + OriginError(cameraOrigin) + OriginError(samplingOrigin);
    }

    /// <summary>Chooses a local frame while keeping virtual addressing independent of its translation.</summary>
    internal static Vector3d SamplingOrigin(ShadowProjectionSnapshot projection, ShadowBounds receivers) =>
        projection.IsPoint ? projection.PositionOrForward : Center(receivers);
    #endregion

    #region GPU records
    /// <summary>Writes an outward-rounded local coverage box and its origin alongside the chunk/local certificate.</summary>
    internal static void WriteCoverage(Span<uint> words, ShadowBounds bounds)
    {
        if (!bounds.IsValid) return;
        var origin = Center(bounds);
        Split(origin, out var high, out var low);
        // Test translated local differences in surface shaders; compute containment uses the
        // outward-rounded integer chunk and FP32 endpoint certificate packed alongside this box.
        for (int axis = 0; axis < 3; axis++)
        {
            double o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            double min = axis == 0 ? bounds.MinX : axis == 1 ? bounds.MinY : bounds.MinZ;
            double max = axis == 0 ? bounds.MaxX : axis == 1 ? bounds.MaxY : bounds.MaxZ;
            float lower = (float)(min - o), upper = (float)(max - o);
            // The independent coverage-frame conversion and receiver +/- radius
            // comparisons round separately. Eight outward ULPs cover their float
            // operations, including a merged box whose origin is far from a receiver.
            for (int step = 0; step < 8; step++)
            { lower = MathF.BitDecrement(lower); upper = MathF.BitIncrement(upper); }
            Float(words, 98 + axis, lower);
            Float(words, 101 + axis, upper);
        }
        Vector(words, 104, high); Vector(words, 107, low);
    }

    /// <summary>Builds one sampling frame from its rendered mapping, independently of current illumination or physical placement.</summary>
    internal static void WriteSamplingFrame(Span<uint> words, ShadowProjectionSnapshot projection, ShadowBounds receivers,
        Vector3d? directionalOrigin = null)
    {
        var frame = words.Slice(FrameOffset, 32); frame.Clear();
        var origin = projection.IsPoint ? projection.PositionOrForward : directionalOrigin ?? SamplingOrigin(projection, receivers);
        Split(origin, out var high, out var low);
        Vector(frame, 0, high); Vector(frame, 3, low);
        Vector(frame, 6, projection.Right); Vector(frame, 9, projection.Up); Vector(frame, 12, projection.Forward);
        double width = projection.TexelWidth;
        Float(frame, 15, (float)width);
        double depthOrigin = projection.IsPoint ? 0 : Vector3d.Dot(origin, projection.Forward);
        Float(frame, 16, (float)(projection.Near - depthOrigin));
        Float(frame, 17, (float)(projection.Far - depthOrigin));
        if (!projection.IsPoint)
        {
            // Keep the large page part integer. Only the within-page fraction enters FP32 projection.
            double x = Vector3d.Dot(origin, projection.Right) / width;
            double y = Vector3d.Dot(origin, projection.Up) / width;
            // The nearby snapped origin may fall just beyond an addressable page
            // even when the receiver and its interior taps remain legal. Clamp only
            // this auxiliary anchor; checked shader displacement still rejects each
            // actual overflowing tap without withdrawing its legal neighbours.
            int px = (int)Math.Clamp(Math.Floor(x / projection.PageSize), int.MinValue, int.MaxValue);
            int py = (int)Math.Clamp(Math.Floor(y / projection.PageSize), int.MinValue, int.MaxValue);
            frame[18] = (uint)px; frame[19] = (uint)py;
            Float(frame, 20, (float)(x - (double)px * projection.PageSize));
            Float(frame, 21, (float)(y - (double)py * projection.PageSize));
        }
        Float(frame, 22, (float)OriginError(origin));
        float near = (float)(projection.Near - depthOrigin), far = (float)(projection.Far - depthOrigin);
        frame[31] = receivers.IsValid && float.IsFinite(near) && float.IsFinite(far) && far > near &&
            (projection.IsPoint ? near > 0 : float.IsFinite((float)width) && (float)width > 0) ? 1u : 0u;
    }

    /// <summary>Stores one scalar without converting its bit representation through an integer value.</summary>
    internal static void Float(Span<uint> words, int offset, float value) => words[offset] = BitConverter.SingleToUInt32Bits(value);

    /// <summary>Packs an already bounded float vector.</summary>
    internal static void Vector(Span<uint> words, int offset, Vector3 value)
    { Float(words, offset, value.X); Float(words, offset + 1, value.Y); Float(words, offset + 2, value.Z); }
    #endregion
    #endregion

    #region Private
    /// <summary>Chooses a nearby chunk-grid origin without coupling page identity to camera translation.</summary>
    private static Vector3d Center(ShadowBounds bounds) => new(
        Math.Round((bounds.MinX + (bounds.MaxX - bounds.MinX) * .5) / 32) * 32,
        Math.Round((bounds.MinY + (bounds.MaxY - bounds.MinY) * .5) / 32) * 32,
        Math.Round((bounds.MinZ + (bounds.MaxZ - bounds.MinZ) * .5) / 32) * 32);

    /// <summary>Narrows a basis only after its high-precision construction is complete.</summary>
    private static void Vector(Span<uint> words, int offset, Vector3d value) =>
        Vector(words, offset, new Vector3((float)value.X, (float)value.Y, (float)value.Z));
    #endregion
}
