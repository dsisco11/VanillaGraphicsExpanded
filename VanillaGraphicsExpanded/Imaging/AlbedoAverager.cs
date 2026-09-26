using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.Imaging;

/// <summary>Estimates representative texture color using a bounded luminance-trimmed linear RGB mean.</summary>
internal static partial class AlbedoAverager
{
    private const int MaxSamplesDefault = 4096;
    private const int BinCount = 256;
    private const double TrimFraction = 0.05;
    private static readonly float[] SrgbToLinearLut = BuildSrgbToLinearLut();

    #region Public API
    /// <summary>Removes five percent from each luminance tail while preserving whole RGB samples and cutout semantics.</summary>
    public static bool TryComputeRepresentativeLinearRgb(
        ReadOnlySpan<int> argbPixels, int width, int height,
        out Vector3 averageLinearRgb, out string? reason,
        int maxSamples = MaxSamplesDefault, byte alphaCutoutThreshold = 64)
    {
        averageLinearRgb = default;
        reason = null;
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
        {
            reason = "invalid dimensions";
            return false;
        }
        int pixelCount = width * height;
        if (argbPixels.Length < pixelCount)
        {
            reason = "insufficient pixel data";
            return false;
        }
        int samples = maxSamples <= 0 ? pixelCount : Math.Min(pixelCount, maxSamples);
        if (TryComputeCore(argbPixels, pixelCount, samples, alphaCutoutThreshold, out averageLinearRgb))
            return true;

        // Preserve the existing fallback for textures with no accepted cutout samples.
        if (TryComputeCore(argbPixels, pixelCount, samples, 0, out averageLinearRgb)) return true;
        reason = "no pixels sampled";
        return false;
    }
    #endregion

    #region Histogram reduction
    /// <summary>Accumulates fixed-size bins and fractionally clips boundary bins without sorting or heap scratch space.</summary>
    private static bool TryComputeCore(ReadOnlySpan<int> pixels, int pixelCount, int samples,
        byte threshold, out Vector3 result)
    {
        Span<int> counts = stackalloc int[BinCount];
        Span<Vector3> sums = stackalloc Vector3[BinCount];
        counts.Clear();
        sums.Clear();
        // SIMD kernels are available, but measured gains depend on alpha coverage and sampling.
        // Keep the scalar kernel as the default until a consistently faster dispatch policy is established.
        int accepted = AccumulateScalar(pixels, pixelCount, samples, threshold, counts, sums);
        result = default;
        if (accepted == 0) return false;

        double lower = accepted * TrimFraction;
        double upper = accepted - lower;
        int cumulative = 0;
        double red = 0, green = 0, blue = 0;
        for (int bin = 0; bin < BinCount; bin++)
        {
            int count = counts[bin];
            if (count == 0) continue;
            // Retain the overlap with the central 90%. Tied luminances receive identical RGB weights,
            // so input order cannot choose which hue survives at a trimming boundary.
            double retained = Math.Max(0, Math.Min(cumulative + count, upper) - Math.Max(cumulative, lower));
            double weight = retained / count;
            red += sums[bin].X * weight;
            green += sums[bin].Y * weight;
            blue += sums[bin].Z * weight;
            cumulative += count;
        }
        double retainedCount = upper - lower;
        result = new Vector3((float)(red / retainedCount), (float)(green / retainedCount), (float)(blue / retainedCount));
        return true;
    }

    /// <summary>Decorrelates sample offsets without mutable random state or per-texture allocations.</summary>
    private static uint MixSampleIndex(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
    }

    /// <summary>Decodes byte-valued sRGB exactly once for reuse by every sampled texture.</summary>
    private static float[] BuildSrgbToLinearLut()
    {
        var lut = new float[256];
        for (int i = 0; i < lut.Length; i++)
        {
            float c = i / 255f;
            lut[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return lut;
    }
    #endregion
}
