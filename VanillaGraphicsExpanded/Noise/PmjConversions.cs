using System;
using System.Buffers;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Noise;

public static class PmjConversions
{
    public static Vector2[] ToVector2F32(in PmjSequence sequence)
    {
        if (sequence is null) throw new ArgumentNullException(nameof(sequence));

        // Canonical format is already Vector2 in [0,1).
        return sequence.Points.ToArray();
    }

    public static Vector2[] ToVector2F32Centered(in PmjSequence sequence)
    {
        if (sequence is null) throw new ArgumentNullException(nameof(sequence));

        int n = sequence.Count;
        Vector2[] result = sequence.Points.ToArray();

        // Vector2 is two contiguous floats; cast and do a bulk subtract.
        // This hits SIMD via TensorPrimitives and avoids per-element overhead.
        Span<float> interleaved = MemoryMarshal.Cast<Vector2, float>(result.AsSpan());
        TensorPrimitives.Add(interleaved, -0.5f, interleaved);

        return result;
    }

    public static ushort[] ToRg16UNormInterleaved(in PmjSequence sequence)
    {
        if (sequence is null) throw new ArgumentNullException(nameof(sequence));

        int n = sequence.Count;

        float[] x = ArrayPool<float>.Shared.Rent(n);
        float[] y = ArrayPool<float>.Shared.Rent(n);

        try
        {
            ExtractXY(sequence.PointsSpan, x.AsSpan(0, n), y.AsSpan(0, n));

            // u16 = floor(clamp(x * 65536, 0, 65535))
            const float scale = 65536f;
            TensorPrimitives.Multiply(x.AsSpan(0, n), scale, x.AsSpan(0, n));
            TensorPrimitives.Multiply(y.AsSpan(0, n), scale, y.AsSpan(0, n));
            TensorPrimitives.Clamp(x.AsSpan(0, n), 0f, 65535f, x.AsSpan(0, n));
            TensorPrimitives.Clamp(y.AsSpan(0, n), 0f, 65535f, y.AsSpan(0, n));

            ushort[] packed = new ushort[checked(n * 2)];
            Span<ushort> r = packed.AsSpan(0, n);
            Span<ushort> g = packed.AsSpan(n, n);

            TensorPrimitives.ConvertTruncating<float, ushort>(x.AsSpan(0, n), r);
            TensorPrimitives.ConvertTruncating<float, ushort>(y.AsSpan(0, n), g);

            // Interleave into RG RG RG... (many APIs prefer interleaved rather than planar).
            // We store interleaved in-place by writing to a new array.
            ushort[] interleaved = new ushort[checked(n * 2)];
            for (int i = 0; i < n; i++)
            {
                interleaved[(i * 2) + 0] = r[i];
                interleaved[(i * 2) + 1] = g[i];
            }

            return interleaved;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(x);
            ArrayPool<float>.Shared.Return(y);
        }
    }

    public static byte[] ToRg8UNormInterleaved(in PmjSequence sequence)
    {
        if (sequence is null) throw new ArgumentNullException(nameof(sequence));

        int n = sequence.Count;

        float[] x = ArrayPool<float>.Shared.Rent(n);
        float[] y = ArrayPool<float>.Shared.Rent(n);

        try
        {
            ExtractXY(sequence.PointsSpan, x.AsSpan(0, n), y.AsSpan(0, n));

            // u8 = floor(clamp(x * 256, 0, 255))
            const float scale = 256f;
            TensorPrimitives.Multiply(x.AsSpan(0, n), scale, x.AsSpan(0, n));
            TensorPrimitives.Multiply(y.AsSpan(0, n), scale, y.AsSpan(0, n));
            TensorPrimitives.Clamp(x.AsSpan(0, n), 0f, 255f, x.AsSpan(0, n));
            TensorPrimitives.Clamp(y.AsSpan(0, n), 0f, 255f, y.AsSpan(0, n));

            byte[] packed = new byte[checked(n * 2)];
            Span<byte> r = packed.AsSpan(0, n);
            Span<byte> g = packed.AsSpan(n, n);

            TensorPrimitives.ConvertTruncating<float, byte>(x.AsSpan(0, n), r);
            TensorPrimitives.ConvertTruncating<float, byte>(y.AsSpan(0, n), g);

            byte[] interleaved = new byte[checked(n * 2)];
            for (int i = 0; i < n; i++)
            {
                interleaved[(i * 2) + 0] = r[i];
                interleaved[(i * 2) + 1] = g[i];
            }

            return interleaved;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(x);
            ArrayPool<float>.Shared.Return(y);
        }
    }

    private static void ExtractXY(ReadOnlySpan<Vector2> points, Span<float> x, Span<float> y)
    {
        int n = points.Length;
        if (x.Length < n || y.Length < n) throw new ArgumentException("Output spans too small.");

        for (int i = 0; i < n; i++)
        {
            Vector2 p = points[i];
            x[i] = p.X;
            y[i] = p.Y;
        }
    }
}
