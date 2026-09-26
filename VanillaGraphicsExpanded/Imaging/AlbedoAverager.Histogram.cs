using System;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace VanillaGraphicsExpanded.Imaging;

/// <summary>Histogram accumulation kernels with identical sampling and ordered, collision-safe bin updates.</summary>
internal static partial class AlbedoAverager
{
    #region Histogram kernels
    /// <summary>Accumulates selected pixels without requiring vector hardware.</summary>
    internal static int AccumulateScalar(ReadOnlySpan<int> pixels, int pixelCount, int samples,
        byte threshold, Span<int> counts, Span<Vector3> sums)
    {
        int accepted = 0;
        for (int i = 0; i < samples; i++)
        {
            int pixel = pixels[SampleIndex(i, pixelCount, samples)];
            if ((byte)((uint)pixel >> 24) < threshold) continue;
            var rgb = new Vector3(SrgbToLinearLut[(pixel >> 16) & 255],
                SrgbToLinearLut[(pixel >> 8) & 255], SrgbToLinearLut[pixel & 255]);
            float luminance = Vector3.Dot(rgb, new Vector3(0.2126f, 0.7152f, 0.0722f));
            int bin = Math.Min((int)(luminance * BinCount), BinCount - 1);
            counts[bin]++;
            sums[bin] += rgb;
            accepted++;
        }
        return accepted;
    }

    /// <summary>Decodes and evaluates luminance for 4 samples together, then updates shared bins in sample order.</summary>
    internal static int AccumulateVector128(ReadOnlySpan<int> pixels, int pixelCount, int samples,
        byte threshold, Span<int> counts, Span<Vector3> sums)
    {
        int accepted = 0;
        int i = 0;
        Span<int> selected = stackalloc int[4];
        for (; i <= samples - 4; i += 4)
        {
            // Gather only when sampling is reduced. Contiguous textures need one vector load.
            Vector128<int> packed;
            if (samples == pixelCount) packed = Vector128.LoadUnsafe(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(pixels), (nuint)i);
            else
            {
                for (int lane = 0; lane < 4; lane++) selected[lane] = pixels[SampleIndex(i + lane, pixelCount, samples)];
                packed = Vector128.LoadUnsafe(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(selected));
            }
            var mask = Vector128.Create(255);
            var ri = (packed >>> 16) & mask;
            var gi = (packed >>> 8) & mask;
            var bi = packed & mask;
            // Preserve exact LUT decoding; do not substitute a polynomial sRGB approximation.
            var r = Vector128.Create(SrgbToLinearLut[ri.GetElement(0)], SrgbToLinearLut[ri.GetElement(1)], SrgbToLinearLut[ri.GetElement(2)], SrgbToLinearLut[ri.GetElement(3)]);
            var g = Vector128.Create(SrgbToLinearLut[gi.GetElement(0)], SrgbToLinearLut[gi.GetElement(1)], SrgbToLinearLut[gi.GetElement(2)], SrgbToLinearLut[gi.GetElement(3)]);
            var b = Vector128.Create(SrgbToLinearLut[bi.GetElement(0)], SrgbToLinearLut[bi.GetElement(1)], SrgbToLinearLut[bi.GetElement(2)], SrgbToLinearLut[bi.GetElement(3)]);
            var luminance = (r * Vector128.Create(0.2126f) + g * Vector128.Create(0.7152f)) + b * Vector128.Create(0.0722f);
            var bins = Vector128.ConvertToInt32(luminance * Vector128.Create((float)BinCount));
            // Different lanes may target the same bin. Ordered scalar writes avoid scatter conflicts
            // and retain the scalar accumulation order and rounding.
            for (int lane = 0; lane < 4; lane++)
            {
                if ((byte)((uint)packed.GetElement(lane) >> 24) < threshold) continue;
                int bin = Math.Min(bins.GetElement(lane), BinCount - 1);
                counts[bin]++;
                sums[bin] += new Vector3(r.GetElement(lane), g.GetElement(lane), b.GetElement(lane));
                accepted++;
            }
        }
        // Preserve original sampling ordinals in the tail, including reduced scans.
        for (; i < samples; i++)
        {
            int pixel = pixels[SampleIndex(i, pixelCount, samples)];
            if ((byte)((uint)pixel >> 24) < threshold) continue;
            var rgb = new Vector3(SrgbToLinearLut[(pixel >> 16) & 255], SrgbToLinearLut[(pixel >> 8) & 255], SrgbToLinearLut[pixel & 255]);
            int bin = Math.Min((int)(Vector3.Dot(rgb, new Vector3(0.2126f, 0.7152f, 0.0722f)) * BinCount), BinCount - 1);
            counts[bin]++;
            sums[bin] += rgb;
            accepted++;
        }
        return accepted;
    }

    /// <summary>Decodes and evaluates luminance for 8 samples together, then updates shared bins in sample order.</summary>
    internal static int AccumulateVector256(ReadOnlySpan<int> pixels, int pixelCount, int samples,
        byte threshold, Span<int> counts, Span<Vector3> sums)
    {
        int accepted = 0;
        int i = 0;
        Span<int> selected = stackalloc int[8];
        for (; i <= samples - 8; i += 8)
        {
            // Gather only when sampling is reduced. Contiguous textures need one vector load.
            Vector256<int> packed;
            if (samples == pixelCount) packed = Vector256.LoadUnsafe(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(pixels), (nuint)i);
            else
            {
                for (int lane = 0; lane < 8; lane++) selected[lane] = pixels[SampleIndex(i + lane, pixelCount, samples)];
                packed = Vector256.LoadUnsafe(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(selected));
            }
            var mask = Vector256.Create(255);
            var ri = (packed >>> 16) & mask;
            var gi = (packed >>> 8) & mask;
            var bi = packed & mask;
            // Preserve exact LUT decoding; do not substitute a polynomial sRGB approximation.
            var r = Vector256.Create(SrgbToLinearLut[ri.GetElement(0)], SrgbToLinearLut[ri.GetElement(1)], SrgbToLinearLut[ri.GetElement(2)], SrgbToLinearLut[ri.GetElement(3)], SrgbToLinearLut[ri.GetElement(4)], SrgbToLinearLut[ri.GetElement(5)], SrgbToLinearLut[ri.GetElement(6)], SrgbToLinearLut[ri.GetElement(7)]);
            var g = Vector256.Create(SrgbToLinearLut[gi.GetElement(0)], SrgbToLinearLut[gi.GetElement(1)], SrgbToLinearLut[gi.GetElement(2)], SrgbToLinearLut[gi.GetElement(3)], SrgbToLinearLut[gi.GetElement(4)], SrgbToLinearLut[gi.GetElement(5)], SrgbToLinearLut[gi.GetElement(6)], SrgbToLinearLut[gi.GetElement(7)]);
            var b = Vector256.Create(SrgbToLinearLut[bi.GetElement(0)], SrgbToLinearLut[bi.GetElement(1)], SrgbToLinearLut[bi.GetElement(2)], SrgbToLinearLut[bi.GetElement(3)], SrgbToLinearLut[bi.GetElement(4)], SrgbToLinearLut[bi.GetElement(5)], SrgbToLinearLut[bi.GetElement(6)], SrgbToLinearLut[bi.GetElement(7)]);
            var luminance = (r * Vector256.Create(0.2126f) + g * Vector256.Create(0.7152f)) + b * Vector256.Create(0.0722f);
            var bins = Vector256.ConvertToInt32(luminance * Vector256.Create((float)BinCount));
            // Different lanes may target the same bin. Ordered scalar writes avoid scatter conflicts
            // and retain the scalar accumulation order and rounding.
            for (int lane = 0; lane < 8; lane++)
            {
                if ((byte)((uint)packed.GetElement(lane) >> 24) < threshold) continue;
                int bin = Math.Min(bins.GetElement(lane), BinCount - 1);
                counts[bin]++;
                sums[bin] += new Vector3(r.GetElement(lane), g.GetElement(lane), b.GetElement(lane));
                accepted++;
            }
        }
        // Preserve original sampling ordinals in the tail, including reduced scans.
        for (; i < samples; i++)
        {
            int pixel = pixels[SampleIndex(i, pixelCount, samples)];
            if ((byte)((uint)pixel >> 24) < threshold) continue;
            var rgb = new Vector3(SrgbToLinearLut[(pixel >> 16) & 255], SrgbToLinearLut[(pixel >> 8) & 255], SrgbToLinearLut[pixel & 255]);
            int bin = Math.Min((int)(Vector3.Dot(rgb, new Vector3(0.2126f, 0.7152f, 0.0722f)) * BinCount), BinCount - 1);
            counts[bin]++;
            sums[bin] += rgb;
            accepted++;
        }
        return accepted;
    }
    #endregion

    #region Sample selection
    /// <summary>Returns the unchanged deterministic sample position for every kernel.</summary>
    private static int SampleIndex(int ordinal, int pixelCount, int samples)
    {
        if (samples == pixelCount) return ordinal;
        // Continuous equal-area strata avoid overweighting rounded, shorter intervals.
        double offset = MixSampleIndex((uint)ordinal) / 4294967296.0;
        return (int)((ordinal + offset) * pixelCount / samples);
    }
    #endregion
}
