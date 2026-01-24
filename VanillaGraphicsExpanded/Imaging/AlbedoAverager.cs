using System;
using System.Buffers;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Imaging;

internal static class AlbedoAverager
{
    private const int MaxSamplesDefault = 4096;

    private static readonly float[] SrgbToLinearLut = BuildSrgbToLinearLut();

    public static bool TryComputeAverageLinearRgb(
        ReadOnlySpan<int> argbPixels,
        int width,
        int height,
        out Vector3 averageLinearRgb,
        out string? reason,
        int maxSamples = MaxSamplesDefault,
        byte alphaCutoutThreshold = 64)
    {
        averageLinearRgb = default;
        reason = null;

        if (width <= 0 || height <= 0)
        {
            reason = "invalid dimensions";
            return false;
        }

        int pixelCount = checked(width * height);
        if (argbPixels.Length < pixelCount)
        {
            reason = "insufficient pixel data";
            return false;
        }

        int step = ComputeStride(width, height, maxSamples);

        if (TryComputeAverageCore(argbPixels, width, height, step, alphaCutoutThreshold, out averageLinearRgb))
        {
            return true;
        }

        // Fully transparent texture (or threshold too aggressive). Fall back to sampling without alpha reject.
        if (TryComputeAverageCore(argbPixels, width, height, step, alphaCutoutThreshold: 0, out averageLinearRgb))
        {
            return true;
        }

        reason = "no pixels sampled";
        return false;
    }

    private static bool TryComputeAverageCore(
        ReadOnlySpan<int> argbPixels,
        int width,
        int height,
        int step,
        byte alphaCutoutThreshold,
        out Vector3 averageLinearRgb)
    {
        averageLinearRgb = default;

        // Fast path: contiguous scan (step==1) with AVX2 + polynomial sRGB->linear approximation.
        // This avoids LUT gathers (which can be expensive) and avoids allocating temporary channel buffers.
        if (step == 1 && Avx2.IsSupported)
        {
            return TryComputeAverageCoreAvx2(argbPixels, width, height, alphaCutoutThreshold, out averageLinearRgb);
        }

        int sampleCapacity = EstimateSampleCount(width, height, step);
        if (sampleCapacity <= 0)
        {
            return false;
        }

        float[] rentedR = ArrayPool<float>.Shared.Rent(sampleCapacity);
        float[] rentedG = ArrayPool<float>.Shared.Rent(sampleCapacity);
        float[] rentedB = ArrayPool<float>.Shared.Rent(sampleCapacity);

        int count = 0;

        try
        {
            for (int y = 0; y < height; y += step)
            {
                int row = y * width;
                for (int x = 0; x < width; x += step)
                {
                    int argb = argbPixels[row + x];

                    byte a = (byte)((argb >> 24) & 0xFF);
                    if (a < alphaCutoutThreshold)
                    {
                        continue;
                    }

                    byte r8 = (byte)((argb >> 16) & 0xFF);
                    byte g8 = (byte)((argb >> 8) & 0xFF);
                    byte b8 = (byte)(argb & 0xFF);

                    rentedR[count] = SrgbToLinearLut[r8];
                    rentedG[count] = SrgbToLinearLut[g8];
                    rentedB[count] = SrgbToLinearLut[b8];
                    count++;
                }
            }

            if (count <= 0)
            {
                return false;
            }

            // Tensor-accelerated reduction (SIMD under the hood).
            float sumR = TensorPrimitives.Sum(rentedR.AsSpan(0, count));
            float sumG = TensorPrimitives.Sum(rentedG.AsSpan(0, count));
            float sumB = TensorPrimitives.Sum(rentedB.AsSpan(0, count));

            float inv = 1f / count;
            averageLinearRgb = new Vector3(sumR * inv, sumG * inv, sumB * inv);
            return true;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rentedR, clearArray: true);
            ArrayPool<float>.Shared.Return(rentedG, clearArray: true);
            ArrayPool<float>.Shared.Return(rentedB, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe bool TryComputeAverageCoreAvx2(
        ReadOnlySpan<int> argbPixels,
        int width,
        int height,
        byte alphaCutoutThreshold,
        out Vector3 averageLinearRgb)
    {
        averageLinearRgb = default;

        // Preconditions: caller ensures step==1 and Avx2.IsSupported.
        int pixelCount = checked(width * height);
        if (argbPixels.Length < pixelCount)
        {
            return false;
        }

        Vector256<float> sumR = Vector256<float>.Zero;
        Vector256<float> sumG = Vector256<float>.Zero;
        Vector256<float> sumB = Vector256<float>.Zero;

        int count = 0;

        float tailR = 0f;
        float tailG = 0f;
        float tailB = 0f;

        Vector256<int> byteMask = Vector256.Create(0xFF);
        Vector256<int> thrMinusOne = Vector256.Create(alphaCutoutThreshold == 0 ? -1 : alphaCutoutThreshold - 1);
        Vector256<float> inv255 = Vector256.Create(1f / 255f);

        fixed (int* pPixels = argbPixels)
        {
            for (int y = 0; y < height; y++)
            {
                int rowBase = y * width;

                int x = 0;
                int rowEndSimd = width & ~7;

                if (alphaCutoutThreshold == 0)
                {
                    // No alpha masking: all pixels contribute.
                    for (; x < rowEndSimd; x += 8)
                    {
                        var px = Avx.LoadVector256(pPixels + rowBase + x);

                        var rIdx = Avx2.And(Avx2.ShiftRightLogical(px, 16), byteMask);
                        var gIdx = Avx2.And(Avx2.ShiftRightLogical(px, 8), byteMask);
                        var bIdx = Avx2.And(px, byteMask);

                        var rSrgb = Avx.Multiply(Avx.ConvertToVector256Single(rIdx), inv255);
                        var gSrgb = Avx.Multiply(Avx.ConvertToVector256Single(gIdx), inv255);
                        var bSrgb = Avx.Multiply(Avx.ConvertToVector256Single(bIdx), inv255);

                        var rLin = SrgbToLinearApprox(rSrgb);
                        var gLin = SrgbToLinearApprox(gSrgb);
                        var bLin = SrgbToLinearApprox(bSrgb);

                        sumR = Avx.Add(sumR, rLin);
                        sumG = Avx.Add(sumG, gLin);
                        sumB = Avx.Add(sumB, bLin);
                        count += 8;
                    }
                }
                else
                {
                    for (; x < rowEndSimd; x += 8)
                    {
                        var px = Avx.LoadVector256(pPixels + rowBase + x);

                        var a = Avx2.And(Avx2.ShiftRightLogical(px, 24), byteMask);
                        var maskInt = Avx2.CompareGreaterThan(a, thrMinusOne);
                        var maskF = maskInt.AsSingle();

                        var rIdx = Avx2.And(Avx2.ShiftRightLogical(px, 16), byteMask);
                        var gIdx = Avx2.And(Avx2.ShiftRightLogical(px, 8), byteMask);
                        var bIdx = Avx2.And(px, byteMask);

                        var rSrgb = Avx.Multiply(Avx.ConvertToVector256Single(rIdx), inv255);
                        var gSrgb = Avx.Multiply(Avx.ConvertToVector256Single(gIdx), inv255);
                        var bSrgb = Avx.Multiply(Avx.ConvertToVector256Single(bIdx), inv255);

                        var rLin = SrgbToLinearApprox(rSrgb);
                        var gLin = SrgbToLinearApprox(gSrgb);
                        var bLin = SrgbToLinearApprox(bSrgb);

                        // Zero-out rejected lanes.
                        rLin = Avx.And(rLin, maskF);
                        gLin = Avx.And(gLin, maskF);
                        bLin = Avx.And(bLin, maskF);

                        sumR = Avx.Add(sumR, rLin);
                        sumG = Avx.Add(sumG, gLin);
                        sumB = Avx.Add(sumB, bLin);

                        // Count accepted lanes (mask lanes are 0 or -1).
                        int laneMask = Avx.MoveMask(maskF);
                        count += BitOperations.PopCount((uint)laneMask);
                    }
                }

                // Scalar tail.
                for (; x < width; x++)
                {
                    int argb = pPixels[rowBase + x];
                    byte a = (byte)((argb >> 24) & 0xFF);
                    if (a < alphaCutoutThreshold)
                    {
                        continue;
                    }

                    byte r8 = (byte)((argb >> 16) & 0xFF);
                    byte g8 = (byte)((argb >> 8) & 0xFF);
                    byte b8 = (byte)(argb & 0xFF);

                    tailR += SrgbToLinearApproxScalar(r8 * (1f / 255f));
                    tailG += SrgbToLinearApproxScalar(g8 * (1f / 255f));
                    tailB += SrgbToLinearApproxScalar(b8 * (1f / 255f));
                    count++;
                }
            }
        }

        if (count <= 0)
        {
            return false;
        }

        float totalR = HorizontalSumAvx(sumR) + tailR;
        float totalG = HorizontalSumAvx(sumG) + tailG;
        float totalB = HorizontalSumAvx(sumB) + tailB;

        float inv = 1f / count;
        averageLinearRgb = new Vector3(totalR * inv, totalG * inv, totalB * inv);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> SrgbToLinearApprox(Vector256<float> srgb01)
    {
        // Polynomial approximation for sRGB->linear on [0,1].
        // Source form commonly used in real-time graphics:
        // linear ≈ x * (x * (x * 0.305306011 + 0.682171111) + 0.012522878)
        // This is fast, vector-friendly, and sufficiently accurate for average-albedo estimation.

        var a = Vector256.Create(0.305306011f);
        var b = Vector256.Create(0.682171111f);
        var c = Vector256.Create(0.012522878f);

        Vector256<float> x = srgb01;
        Vector256<float> t = Avx.Add(Avx.Multiply(x, a), b);
        t = Avx.Add(Avx.Multiply(x, t), c);
        return Avx.Multiply(x, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SrgbToLinearApproxScalar(float srgb01)
    {
        float x = Math.Clamp(srgb01, 0f, 1f);
        return x * (x * (x * 0.305306011f + 0.682171111f) + 0.012522878f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float HorizontalSumAvx(Vector256<float> v)
    {
        float* tmp = stackalloc float[8];
        Avx.Store(tmp, v);

        float sum = 0f;
        sum += tmp[0];
        sum += tmp[1];
        sum += tmp[2];
        sum += tmp[3];
        sum += tmp[4];
        sum += tmp[5];
        sum += tmp[6];
        sum += tmp[7];
        return sum;
    }

    private static int ComputeStride(int width, int height, int maxSamples)
    {
        if (maxSamples <= 0)
        {
            return 1;
        }

        int pixelCount = width * height;
        if (pixelCount <= maxSamples)
        {
            return 1;
        }

        // Approximate stride so (width/step)*(height/step) <= maxSamples.
        float ratio = pixelCount / (float)maxSamples;
        int step = (int)MathF.Ceiling(MathF.Sqrt(ratio));
        return Math.Max(1, step);
    }

    private static int EstimateSampleCount(int width, int height, int step)
    {
        if (step <= 0)
        {
            return 0;
        }

        int sx = (width + step - 1) / step;
        int sy = (height + step - 1) / step;
        return Math.Max(1, sx * sy);
    }

    private static float[] BuildSrgbToLinearLut()
    {
        var lut = new float[256];

        for (int i = 0; i < 256; i++)
        {
            float c = i / 255f;
            lut[i] = c <= 0.04045f
                ? c / 12.92f
                : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return lut;
    }
}
