using System;
using System.Buffers;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Noise;

public static class VoidAndClusterGenerator
{
    public static BlueNoiseRankMap GenerateRankMap(in BlueNoiseConfig config)
    {
        config.Validate();

        if (config.Algorithm != BlueNoiseAlgorithm.VoidAndCluster)
        {
            throw new NotSupportedException($"Unsupported algorithm: {config.Algorithm}.");
        }

        if (config.Slices != 1)
        {
            throw new NotSupportedException($"Slices != 1 is not supported yet (Slices={config.Slices}).");
        }

        int width = config.Width;
        int height = config.Height;
        int n = checked(width * height);

        if (n > (ushort.MaxValue + 1))
        {
            throw new NotSupportedException($"RankU16 requires Width*Height <= {ushort.MaxValue + 1} (was {n}).");
        }

        // Initial seed count: must be > 0 and < 0.5.
        int maxInitial = Math.Max(1, (n - 1) / 2);
        int nInitialOne = Math.Clamp((int)(n * config.InitialFillRatio), 1, maxInitial);

        float[] kernel = BuildGaussianKernel(config.Sigma);

        bool[] initial = new bool[n];
        FillDeterministicInitialPattern(initial, config, nInitialOne);

        // Swap tightest clusters into largest voids until convergence (or until capped).
        int iterations = 0;
        while (true)
        {
            int iTightestCluster = FindTightestClusterIndex(initial, width, height, config.Tileable, kernel);
            initial[iTightestCluster] = false;

            int iLargestVoid = FindLargestVoidIndex(initial, width, height, config.Tileable, kernel);

            if (iLargestVoid == iTightestCluster)
            {
                // No effective change, revert and stop.
                initial[iTightestCluster] = true;
                break;
            }

            initial[iLargestVoid] = true;

            iterations++;
            if (config.MaxIterations > 0 && iterations >= config.MaxIterations)
            {
                break;
            }
        }

        var ranks = new ushort[n];

        // Phase 1: rank minority pixels in the initial binary pattern.
        bool[] pattern = (bool[])initial.Clone();
        for (int rank = nInitialOne - 1; rank >= 0; rank--)
        {
            int idx = FindTightestClusterIndex(pattern, width, height, config.Tileable, kernel);
            pattern[idx] = false;
            ranks[idx] = (ushort)rank;
        }

        // Phase 2: rank the remainder of the first half.
        pattern = (bool[])initial.Clone();
        int half = (n + 1) / 2;
        for (int rank = nInitialOne; rank < half; rank++)
        {
            int idx = FindLargestVoidIndex(pattern, width, height, config.Tileable, kernel);
            pattern[idx] = true;
            ranks[idx] = (ushort)rank;
        }

        // Phase 3: rank the last half.
        for (int rank = half; rank < n; rank++)
        {
            int idx = FindTightestClusterIndex(pattern, width, height, config.Tileable, kernel);
            pattern[idx] = true;
            ranks[idx] = (ushort)rank;
        }

        return new BlueNoiseRankMap(width, height, ranks);
    }

    private static void FillDeterministicInitialPattern(Span<bool> destination, in BlueNoiseConfig config, int countTrue)
    {
        destination.Clear();

        uint seed = Squirrel3Noise.HashU(config.Seed, unchecked((uint)config.Width), unchecked((uint)config.Height));

        int n = destination.Length;

        var keys = new ulong[n];
        var indices = new int[n];

        for (int i = 0; i < n; i++)
        {
            uint h = Squirrel3Noise.HashU(seed, unchecked((uint)i));
            keys[i] = (((ulong)h) << 32) | (uint)i;
            indices[i] = i;
        }

        Array.Sort(keys, indices);

        for (int i = 0; i < countTrue; i++)
        {
            destination[indices[i]] = true;
        }
    }

    private static float[] BuildGaussianKernel(float sigma)
    {
        // Radius rule: 3*sigma (common practical cutoff).
        // Note: Phase 4 will revisit this; keep it consistent and deterministic for now.
        int radius = Math.Max(1, (int)MathF.Ceiling(3f * sigma));
        int size = checked((radius * 2) + 1);

        var kernel = new float[size];

        float invTwoSigma2 = 1f / (2f * sigma * sigma);

        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            float w = MathF.Exp(-(i * i) * invTwoSigma2);
            kernel[i + radius] = w;
            sum += w;
        }

        float invSum = 1f / sum;
        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] *= invSum;
        }

        return kernel;
    }

    private static int FindLargestVoidIndex(ReadOnlySpan<bool> pattern, int width, int height, bool tileable, ReadOnlySpan<float> kernel)
    {
        int n = pattern.Length;

        int ones = 0;
        for (int i = 0; i < n; i++)
        {
            if (pattern[i]) ones++;
        }

        bool invert = (ones * 2) >= n;

        float[] src = ArrayPool<float>.Shared.Rent(n);
        float[] tmp = ArrayPool<float>.Shared.Rent(n);
        float[] dst = ArrayPool<float>.Shared.Rent(n);

        try
        {
            for (int i = 0; i < n; i++)
            {
                bool v = invert ? !pattern[i] : pattern[i];
                src[i] = v ? 1f : 0f;
            }

            GaussianBlur2D(src.AsSpan(0, n), tmp.AsSpan(0, n), dst.AsSpan(0, n), width, height, tileable, kernel);

            // Match the reference behavior (Ulichney/Peters):
            //   iLargestVoid = argmin( where(minority, 2.0, filtered) )
            Span<float> metric = dst.AsSpan(0, n);
            for (int i = 0; i < n; i++)
            {
                bool v = invert ? !pattern[i] : pattern[i];
                if (v)
                {
                    metric[i] = 2f;
                }
            }

            int bestIndex = TensorPrimitives.IndexOfMin(metric);
            if ((uint)bestIndex >= (uint)n)
            {
                throw new InvalidOperationException("Largest void search failed.");
            }

            return bestIndex;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(src);
            ArrayPool<float>.Shared.Return(tmp);
            ArrayPool<float>.Shared.Return(dst);
        }
    }

    private static int FindTightestClusterIndex(ReadOnlySpan<bool> pattern, int width, int height, bool tileable, ReadOnlySpan<float> kernel)
    {
        int n = pattern.Length;

        int ones = 0;
        for (int i = 0; i < n; i++)
        {
            if (pattern[i]) ones++;
        }

        bool invert = (ones * 2) >= n;

        float[] src = ArrayPool<float>.Shared.Rent(n);
        float[] tmp = ArrayPool<float>.Shared.Rent(n);
        float[] dst = ArrayPool<float>.Shared.Rent(n);

        try
        {
            for (int i = 0; i < n; i++)
            {
                bool v = invert ? !pattern[i] : pattern[i];
                src[i] = v ? 1f : 0f;
            }

            GaussianBlur2D(src.AsSpan(0, n), tmp.AsSpan(0, n), dst.AsSpan(0, n), width, height, tileable, kernel);

            // Match the reference behavior (Ulichney/Peters):
            //   iTightestCluster = argmax( where(minority, filtered, -1.0) )
            Span<float> metric = dst.AsSpan(0, n);
            for (int i = 0; i < n; i++)
            {
                bool v = invert ? !pattern[i] : pattern[i];
                if (!v)
                {
                    metric[i] = -1f;
                }
            }

            int bestIndex = TensorPrimitives.IndexOfMax(metric);
            if ((uint)bestIndex >= (uint)n)
            {
                throw new InvalidOperationException("Tightest cluster search failed.");
            }

            return bestIndex;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(src);
            ArrayPool<float>.Shared.Return(tmp);
            ArrayPool<float>.Shared.Return(dst);
        }
    }

    private static void GaussianBlur2D(ReadOnlySpan<float> source, Span<float> scratch, Span<float> destination, int width, int height, bool tileable, ReadOnlySpan<float> kernel)
    {
        if (source.Length != scratch.Length || source.Length != destination.Length)
        {
            throw new ArgumentException("Buffer length mismatch.");
        }

        int radius = (kernel.Length - 1) / 2;
        int kernelLength = kernel.Length;

        // Separable convolution with TensorPrimitives.Dot.
        // We build an extended row/col buffer per scanline with wrap/clamp so each sliding window is contiguous.
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
                    scratch[rowBase + x] = TensorPrimitives.Dot(kernel, extendedRow.Slice(x, kernelLength));
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
                    destination[(y * width) + x] = TensorPrimitives.Dot(kernel, extendedCol.Slice(y, kernelLength));
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
        // Fast path: typical case where radius is small relative to width.
        // Use bulk operations (memcpy/fill) rather than per-element wrap/clamp math.
        if (radius <= width)
        {
            if (tileable)
            {
                // [tail | row | head]
                row.Slice(width - radius, radius).CopyTo(destination.Slice(0, radius));
                row.CopyTo(destination.Slice(radius, width));
                row.Slice(0, radius).CopyTo(destination.Slice(radius + width, radius));
                return;
            }

            // Clamp
            destination.Slice(0, radius).Fill(row[0]);
            row.CopyTo(destination.Slice(radius, width));
            destination.Slice(radius + width, radius).Fill(row[width - 1]);
            return;
        }

        // Fallback: very small textures / very large sigma where radius >= width.
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
