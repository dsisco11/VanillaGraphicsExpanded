using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal static class LumOnWorldProbeAtlasDirectionSlicing
{
    public static int GetDirectionCount(int octahedralSize)
    {
        if (octahedralSize <= 0) return 0;
        return checked(octahedralSize * octahedralSize);
    }

    /// <summary>
    /// Computes the texel indices (0..S*S-1) to trace for this probe update.
    /// Mapping is deterministic and depends only on (frameIndex, probeStorageLinearIndex, S, K).
    /// </summary>
    public static int FillTexelIndicesForUpdate(
        int frameIndex,
        int probeStorageLinearIndex,
        int octahedralSize,
        int texelsPerUpdate,
        Span<int> destination)
    {
        int s = Math.Max(1, octahedralSize);
        int dirCount = checked(s * s);
        int k = Math.Clamp(texelsPerUpdate, 1, dirCount);

        int writeMax = Math.Min(destination.Length, k);

        int batchCount = (dirCount + k - 1) / k;
        int batchIndex = batchCount <= 1 ? 0 : PositiveMod(Hash32(frameIndex ^ probeStorageLinearIndex), batchCount);

        int stride = SelectCoprimeStride(dirCount, Hash32(probeStorageLinearIndex) | 1);
        int baseOffset = PositiveMod(Hash32(probeStorageLinearIndex ^ unchecked((int)0x9E3779B9)), dirCount);

        int written = 0;
        for (int i = 0; i < writeMax; i++)
        {
            int linear = batchIndex * k + i;
            if (linear >= dirCount)
            {
                break;
            }

            destination[written++] = PositiveMod(baseOffset + linear * stride, dirCount);
        }

        return written;
    }

    private static int Hash32(int x)
    {
        unchecked
        {
            uint u = (uint)x;
            u ^= u >> 16;
            u *= 0x7FEB352D;
            u ^= u >> 15;
            u *= 0x846CA68B;
            u ^= u >> 16;
            return (int)u;
        }
    }

    private static int PositiveMod(int x, int m)
    {
        if (m <= 0) return 0;
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }
        return a;
    }

    private static int SelectCoprimeStride(int dirCount, int seed)
    {
        if (dirCount <= 1)
        {
            return 1;
        }

        int candidate = PositiveMod(seed, dirCount);
        if (candidate == 0) candidate = 1;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            int s = candidate + attempt;
            if (s >= dirCount) s -= dirCount;
            if (s == 0) s = 1;

            if (Gcd(s, dirCount) == 1)
            {
                return s;
            }
        }

        return 1;
    }
}
