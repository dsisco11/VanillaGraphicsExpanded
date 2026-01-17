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

        GaussianKernel1D kernel = GaussianKernel1D.Create(config.Sigma);

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

    private static int FindLargestVoidIndex(ReadOnlySpan<bool> pattern, int width, int height, bool tileable, in GaussianKernel1D kernel)
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

            SeparableGaussianBlur.Blur(kernel, src.AsSpan(0, n), tmp.AsSpan(0, n), dst.AsSpan(0, n), width, height, tileable);

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

    private static int FindTightestClusterIndex(ReadOnlySpan<bool> pattern, int width, int height, bool tileable, in GaussianKernel1D kernel)
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

            SeparableGaussianBlur.Blur(kernel, src.AsSpan(0, n), tmp.AsSpan(0, n), dst.AsSpan(0, n), width, height, tileable);

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
}
