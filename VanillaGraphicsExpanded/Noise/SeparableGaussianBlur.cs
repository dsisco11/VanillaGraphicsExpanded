using System;
using System.Buffers;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Noise;

internal static class SeparableGaussianBlur
{
    public static void Blur(
        in GaussianKernel1D kernel,
        ReadOnlySpan<float> source,
        Span<float> scratch,
        Span<float> destination,
        int width,
        int height,
        bool tileable)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be > 0.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be > 0.");

        int n = checked(width * height);
        if (source.Length < n) throw new ArgumentException($"Source must be at least {n} elements long.", nameof(source));
        if (scratch.Length < n) throw new ArgumentException($"Scratch must be at least {n} elements long.", nameof(scratch));
        if (destination.Length < n) throw new ArgumentException($"Destination must be at least {n} elements long.", nameof(destination));

        ReadOnlySpan<float> k = kernel.Weights.Span;
        int radius = kernel.Radius;
        int kernelLength = k.Length;

        int extendedRowLength = checked(width + (2 * radius));
        int extendedColLength = checked(height + (2 * radius));

        float[] extendedBuffer = ArrayPool<float>.Shared.Rent(Math.Max(extendedRowLength, extendedColLength));
        int[] yIndex = ArrayPool<int>.Shared.Rent(extendedColLength);

        try
        {
            FillExtendedAxisIndices(yIndex.AsSpan(0, extendedColLength), height, radius, tileable);

            // Horizontal pass: source -> scratch
            for (int y = 0; y < height; y++)
            {
                int rowBase = y * width;
                Span<float> extendedRow = extendedBuffer.AsSpan(0, extendedRowLength);
                ReadOnlySpan<float> row = source.Slice(rowBase, width);

                FillExtendedRow(extendedRow, row, width, radius, tileable);

                for (int x = 0; x < width; x++)
                {
                    scratch[rowBase + x] = TensorPrimitives.Dot(k, extendedRow.Slice(x, kernelLength));
                }
            }

            // Vertical pass: scratch -> destination
            for (int x = 0; x < width; x++)
            {
                Span<float> extendedCol = extendedBuffer.AsSpan(0, extendedColLength);
                for (int j = 0; j < extendedColLength; j++)
                {
                    int y = yIndex[j];
                    extendedCol[j] = scratch[(y * width) + x];
                }

                for (int y = 0; y < height; y++)
                {
                    destination[(y * width) + x] = TensorPrimitives.Dot(k, extendedCol.Slice(y, kernelLength));
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(extendedBuffer);
            ArrayPool<int>.Shared.Return(yIndex);
        }
    }

    private static void FillExtendedAxisIndices(Span<int> destination, int size, int radius, bool tileable)
    {
        for (int j = 0; j < destination.Length; j++)
        {
            int orig = j - radius;
            destination[j] = tileable ? WrapIndex(orig, size) : ClampIndex(orig, size);
        }
    }

    private static void FillExtendedRow(Span<float> destination, ReadOnlySpan<float> row, int width, int radius, bool tileable)
    {
        if (radius <= width)
        {
            if (tileable)
            {
                row.Slice(width - radius, radius).CopyTo(destination.Slice(0, radius));
                row.CopyTo(destination.Slice(radius, width));
                row.Slice(0, radius).CopyTo(destination.Slice(radius + width, radius));
                return;
            }

            destination.Slice(0, radius).Fill(row[0]);
            row.CopyTo(destination.Slice(radius, width));
            destination.Slice(radius + width, radius).Fill(row[width - 1]);
            return;
        }

        for (int j = 0; j < destination.Length; j++)
        {
            int origX = j - radius;
            int x = tileable ? WrapIndex(origX, width) : ClampIndex(origX, width);
            destination[j] = row[x];
        }
    }

    private static int WrapIndex(int i, int size)
    {
        int m = i % size;
        return m < 0 ? m + size : m;
    }

    private static int ClampIndex(int i, int size)
    {
        if (i < 0) return 0;
        if (i >= size) return size - 1;
        return i;
    }
}
