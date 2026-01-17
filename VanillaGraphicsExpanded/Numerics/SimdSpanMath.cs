using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

internal static class SimdSpanMath
{
    private const float Inv255 = 1f / 255f;

    public static void FillInterleaved3(Span<float> destination, float a0, float a1, float a2)
    {
        if (destination.Length == 0)
        {
            return;
        }

        if ((destination.Length % 3) != 0)
        {
            throw new ArgumentException($"Length must be a multiple of 3 (destination={destination.Length}).", nameof(destination));
        }

        if (Avx.IsSupported)
        {
            FillInterleaved3Vector256Avx(destination, a0, a1, a2);
            return;
        }

        if (Sse.IsSupported)
        {
            FillInterleaved3Vector128Sse(destination, a0, a1, a2);
            return;
        }

        FillInterleaved3Scalar(destination, a0, a1, a2);
    }

    public static void CopyInterleaved4ToInterleaved3(ReadOnlySpan<float> source4, Span<float> destination3)
    {
        if (source4.Length == 0)
        {
            if (destination3.Length != 0)
            {
                throw new ArgumentException($"Length mismatch (source={source4.Length}, destination={destination3.Length}).", nameof(destination3));
            }

            return;
        }

        if ((source4.Length % 4) != 0)
        {
            throw new ArgumentException($"Length must be a multiple of 4 (source={source4.Length}).", nameof(source4));
        }

        int pixelCount = source4.Length / 4;
        int expectedDestinationLength = pixelCount * 3;
        if (destination3.Length != expectedDestinationLength)
        {
            throw new ArgumentException($"Length mismatch (expected destination={expectedDestinationLength}, destination={destination3.Length}).", nameof(destination3));
        }

        // Note: This is a "drop every 4th element" packing transform (4→3 stride).
        // Full SIMD is possible but fairly shuffle-heavy; start with an unrolled scalar loop.
        int si = 0;
        int di = 0;

        int p = 0;
        for (; p <= pixelCount - 4; p += 4)
        {
            destination3[di + 0] = source4[si + 0];
            destination3[di + 1] = source4[si + 1];
            destination3[di + 2] = source4[si + 2];

            destination3[di + 3] = source4[si + 4];
            destination3[di + 4] = source4[si + 5];
            destination3[di + 5] = source4[si + 6];

            destination3[di + 6] = source4[si + 8];
            destination3[di + 7] = source4[si + 9];
            destination3[di + 8] = source4[si + 10];

            destination3[di + 9] = source4[si + 12];
            destination3[di + 10] = source4[si + 13];
            destination3[di + 11] = source4[si + 14];

            si += 16;
            di += 12;
        }

        for (; p < pixelCount; p++)
        {
            destination3[di + 0] = source4[si + 0];
            destination3[di + 1] = source4[si + 1];
            destination3[di + 2] = source4[si + 2];
            si += 4;
            di += 3;
        }

        Debug.Assert(si == source4.Length);
        Debug.Assert(di == destination3.Length);
    }

    public static void MultiplyClamp01Interleaved3InPlace(Span<float> destination3, float mul0, float mul1, float mul2)
    {
        if (destination3.Length == 0)
        {
            return;
        }

        if ((destination3.Length % 3) != 0)
        {
            throw new ArgumentException($"Length must be a multiple of 3 (destination={destination3.Length}).", nameof(destination3));
        }

        // This is an interleaved layout; keep the math centralized here.
        // NaN policy: comparisons against NaN are false, so NaN passes through unchanged.
        for (int i = 0; i < destination3.Length; i += 3)
        {
            destination3[i + 0] = Clamp01(destination3[i + 0] * mul0);
            destination3[i + 1] = Clamp01(destination3[i + 1] * mul1);
            destination3[i + 2] = Clamp01(destination3[i + 2] * mul2);
        }
    }

    public static void MultiplyClamp01Interleaved3InPlace2D(
        Span<float> destination3,
        int rectWidthPixels,
        int rectHeightPixels,
        int rowStridePixels,
        float mul0,
        float mul1,
        float mul2)
    {
        if (rectWidthPixels <= 0) throw new ArgumentOutOfRangeException(nameof(rectWidthPixels));
        if (rectHeightPixels <= 0) throw new ArgumentOutOfRangeException(nameof(rectHeightPixels));
        if (rowStridePixels < rectWidthPixels) throw new ArgumentOutOfRangeException(nameof(rowStridePixels));

        int rectRowFloats = checked(rectWidthPixels * 3);
        int requiredFloats = checked(((rectHeightPixels - 1) * rowStridePixels + rectWidthPixels) * 3);
        if (destination3.Length < requiredFloats)
        {
            throw new ArgumentException(
                $"destination3 is too small for rect (required={requiredFloats}, actual={destination3.Length}, rect={rectWidthPixels}x{rectHeightPixels}, stridePixels={rowStridePixels}).",
                nameof(destination3));
        }

        // Precompute a row-sized multiplier tensor so each row can use TensorPrimitives ops.
        const int StackallocFloatLimit = 1024;
        float[]? rented = null;
        Span<float> mulRow = rectRowFloats <= StackallocFloatLimit
            ? stackalloc float[rectRowFloats]
            : (rented = ArrayPool<float>.Shared.Rent(rectRowFloats));

        mulRow = mulRow.Slice(0, rectRowFloats);
        FillInterleaved3(mulRow, mul0, mul1, mul2);

        try
        {
            int rowStrideFloats = checked(rowStridePixels * 3);
            for (int y = 0; y < rectHeightPixels; y++)
            {
                Span<float> row = destination3.Slice(y * rowStrideFloats, rectRowFloats);
                TensorPrimitives.Multiply(row, mulRow, row);
                TensorPrimitives.Clamp(row, 0f, 1f, row);
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<float>.Shared.Return(rented);
            }
        }
    }

    public static void Fill(Span<float> destination, float value)
    {
        destination.Fill(value);
    }

    public static void ScaleInPlace(Span<float> destination, float scale)
    {
        TensorPrimitives.Multiply(destination, scale, destination);
    }

    public static void Scale(ReadOnlySpan<float> source, float scale, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.Multiply(source, scale, destination);
    }

    public static void AddInPlace(Span<float> destination, float add)
    {
        TensorPrimitives.Add(destination, add, destination);
    }

    public static void Add(ReadOnlySpan<float> source, float add, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.Add(source, add, destination);
    }

    public static void MultiplyAddClamp01(ReadOnlySpan<float> x, float mul, float add, Span<float> destination)
    {
        EnsureSameLength(x.Length, destination.Length);

        TensorPrimitives.Multiply(x, mul, destination);
        TensorPrimitives.Add(destination, add, destination);
        TensorPrimitives.Clamp(destination, 0f, 1f, destination);
    }

    public static void BytesToSingles(ReadOnlySpan<byte> source, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.ConvertTruncating<byte, float>(source, destination);
    }

    public static void BytesToSingles01(ReadOnlySpan<byte> source, Span<float> destination)
    {
        BytesToSingles(source, destination);
        TensorPrimitives.Multiply(destination, Inv255, destination);

        Debug.Assert(destination.Length == source.Length);
    }

    private static void EnsureSameLength(int sourceLength, int destinationLength)
    {
        if (sourceLength != destinationLength)
        {
            throw new ArgumentException($"Length mismatch (source={sourceLength}, destination={destinationLength}).");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    #region Layout SIMD Helpers

    internal static void FillInterleaved3Scalar(Span<float> destination, float a0, float a1, float a2)
    {
        Debug.Assert((destination.Length % 3) == 0);

        for (int i = 0; i < destination.Length; i += 3)
        {
            destination[i + 0] = a0;
            destination[i + 1] = a1;
            destination[i + 2] = a2;
        }
    }

    internal static void FillInterleaved3Vector128Sse(Span<float> destination, float a0, float a1, float a2)
    {
        if (!Sse.IsSupported)
        {
            throw new PlatformNotSupportedException("SSE is not supported on this platform.");
        }

        Debug.Assert((destination.Length % 3) == 0);

        ref float dstRef = ref MemoryMarshal.GetReference(destination);
        int length = destination.Length;

        // 4 floats per vector. Write 3 vectors (12 floats) per iteration to preserve the 3-element phase.
        Vector128<float> v0 = Vector128.Create(a0, a1, a2, a0);
        Vector128<float> v1 = Vector128.Create(a1, a2, a0, a1);
        Vector128<float> v2 = Vector128.Create(a2, a0, a1, a2);

        int i = 0;
        for (; i <= length - 12; i += 12)
        {
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 0)), v0);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 4)), v1);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 8)), v2);
        }

        for (; i < length; i += 3)
        {
            Unsafe.Add(ref dstRef, i + 0) = a0;
            Unsafe.Add(ref dstRef, i + 1) = a1;
            Unsafe.Add(ref dstRef, i + 2) = a2;
        }
    }

    internal static void FillInterleaved3Vector256Avx(Span<float> destination, float a0, float a1, float a2)
    {
        if (!Avx.IsSupported)
        {
            throw new PlatformNotSupportedException("AVX is not supported on this platform.");
        }

        Debug.Assert((destination.Length % 3) == 0);

        ref float dstRef = ref MemoryMarshal.GetReference(destination);
        int length = destination.Length;

        // 8 floats per vector. Write 3 vectors (24 floats) per iteration to preserve the 3-element phase.
        Vector256<float> v0 = Vector256.Create(a0, a1, a2, a0, a1, a2, a0, a1);
        Vector256<float> v1 = Vector256.Create(a2, a0, a1, a2, a0, a1, a2, a0);
        Vector256<float> v2 = Vector256.Create(a1, a2, a0, a1, a2, a0, a1, a2);

        int i = 0;
        for (; i <= length - 24; i += 24)
        {
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 0)), v0);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 8)), v1);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 16)), v2);
        }

        for (; i < length; i += 3)
        {
            Unsafe.Add(ref dstRef, i + 0) = a0;
            Unsafe.Add(ref dstRef, i + 1) = a1;
            Unsafe.Add(ref dstRef, i + 2) = a2;
        }
    }

    #endregion
}
