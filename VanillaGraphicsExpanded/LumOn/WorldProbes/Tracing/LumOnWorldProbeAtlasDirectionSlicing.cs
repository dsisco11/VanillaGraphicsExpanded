using System;
using System.Numerics;

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

    /// <summary>
    /// Importance-biased selector (optional): chooses exactly K unique texel indices per update, with a
    /// deterministic exploration subset to preserve coverage.
    ///
    /// Importance proxy (basis-driven): w(dir) = max(dot(basisDir, dir), 0).
    /// This avoids GPU readbacks and keeps selection CPU-cheap; it is intentionally conservative.
    ///
    /// Determinism: depends only on (frameIndex, probeStorageLinearIndex, S, K, explore knobs, basisDir).
    /// </summary>
    public static int FillTexelIndicesForUpdateImportance(
        int frameIndex,
        int probeStorageLinearIndex,
        int octahedralSize,
        int texelsPerUpdate,
        Vector3 basisDir,
        float exploreFraction,
        int exploreCount,
        float weightEpsilon,
        ReadOnlySpan<Vector3> directions,
        Span<int> destination)
    {
        int s = Math.Max(1, octahedralSize);
        int dirCount = checked(s * s);
        int k = Math.Clamp(texelsPerUpdate, 1, dirCount);
        int writeMax = Math.Min(destination.Length, k);

        if (writeMax <= 0)
        {
            return 0;
        }

        // Normalize basis direction (defensive).
        if (basisDir.LengthSquared() < 1e-12f)
        {
            basisDir = Vector3.UnitY;
        }
        else
        {
            basisDir = Vector3.Normalize(basisDir);
        }

        exploreFraction = Math.Clamp(exploreFraction, 0.0f, 1.0f);
        weightEpsilon = Math.Clamp(weightEpsilon, 1e-12f, 1.0f);

        int kExplore;
        if (exploreCount >= 0)
        {
            kExplore = exploreCount;
        }
        else
        {
            kExplore = (int)MathF.Round(k * exploreFraction);
        }
        kExplore = Math.Clamp(kExplore, 0, writeMax);
        int kImportance = writeMax - kExplore;

        // Selection bitmap (byte for compactness; stackalloc for typical S<=64).
        Span<byte> selected = dirCount <= 4096 ? stackalloc byte[4096] : new byte[dirCount];
        selected = selected.Slice(0, dirCount);

        int stride = SelectCoprimeStride(dirCount, Hash32(probeStorageLinearIndex) | 1);
        int baseOffset = PositiveMod(Hash32(probeStorageLinearIndex ^ unchecked((int)0x9E3779B9)), dirCount);

        int written = 0;

        // Exploration: deterministic batch slicing with an intra-batch cycle so we eventually cover all texels
        // even when kExplore < k.
        if (kExplore > 0)
        {
            int batchCount = (dirCount + k - 1) / k;
            int batchIndex = batchCount <= 1 ? 0 : PositiveMod(Hash32(frameIndex ^ probeStorageLinearIndex), batchCount);
            int batchStart = batchIndex * k;

            int cycle = batchCount <= 0 ? 0 : (frameIndex + probeStorageLinearIndex) / Math.Max(1, batchCount);
            int withinBatchOffset = PositiveMod(Hash32(cycle ^ probeStorageLinearIndex), Math.Max(1, k));

            for (int j = 0; j < k && written < kExplore; j++)
            {
                int linear = batchStart + ((withinBatchOffset + j) % Math.Max(1, k));
                if (linear >= dirCount)
                {
                    continue;
                }

                int idx = PositiveMod(baseOffset + linear * stride, dirCount);
                if (selected[idx] != 0)
                {
                    continue;
                }

                selected[idx] = 1;
                destination[written++] = idx;
            }
        }

        if (kImportance > 0)
        {
            Span<float> keys = dirCount <= 4096 ? stackalloc float[4096] : new float[dirCount];
            keys = keys.Slice(0, dirCount);

            int salt = Hash32(frameIndex ^ unchecked((int)0xC001D00D));

            for (int i = 0; i < dirCount; i++)
            {
                if (selected[i] != 0)
                {
                    keys[i] = float.NegativeInfinity;
                    continue;
                }

                // Basis-driven importance proxy.
                Vector3 dir = (i < directions.Length) ? directions[i] : Vector3.Zero;
                float w = MathF.Max(0.0f, Vector3.Dot(basisDir, dir));
                if (w <= weightEpsilon)
                {
                    keys[i] = float.NegativeInfinity;
                    continue;
                }

                // Deterministic key method (log form): key = log(u)/w, select K largest keys.
                float u = HashToUnitFloat(Hash32(i ^ probeStorageLinearIndex ^ salt));
                keys[i] = MathF.Log(u) / w;
            }

            for (int pick = 0; pick < kImportance; pick++)
            {
                int bestIdx = -1;
                float bestKey = float.NegativeInfinity;

                for (int i = 0; i < dirCount; i++)
                {
                    if (selected[i] != 0)
                    {
                        continue;
                    }

                    float key = keys[i];
                    if (key > bestKey)
                    {
                        bestKey = key;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0 || float.IsNegativeInfinity(bestKey))
                {
                    break;
                }

                selected[bestIdx] = 1;
                destination[written++] = bestIdx;

                if (written >= writeMax)
                {
                    break;
                }

                keys[bestIdx] = float.NegativeInfinity;
            }
        }

        // Fallback fill: if we didn't reach K (all weights ~0), fill remaining slots deterministically from the
        // legacy batch order.
        if (written < writeMax)
        {
            int batchCount = (dirCount + k - 1) / k;
            int batchIndex = batchCount <= 1 ? 0 : PositiveMod(Hash32(frameIndex ^ probeStorageLinearIndex), batchCount);
            int batchStart = batchIndex * k;

            for (int i = 0; i < dirCount && written < writeMax; i++)
            {
                int linear = (batchStart + i) % dirCount;
                int idx = PositiveMod(baseOffset + linear * stride, dirCount);
                if (selected[idx] != 0)
                {
                    continue;
                }

                selected[idx] = 1;
                destination[written++] = idx;
            }
        }

        return written;
    }

    private static float HashToUnitFloat(int h)
    {
        // Map uint32 -> (0,1], clamping away from 0 to keep log stable.
        unchecked
        {
            uint u = (uint)h;
            // 24 bits of mantissa; ensure non-zero.
            uint m = (u >> 8) | 1u;
            return m / (float)0x01000000; // 2^24
        }
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
