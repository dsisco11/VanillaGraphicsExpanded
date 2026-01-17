using System;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Noise;

public static class BlueNoiseConversions
{
    public static uint[] ToRankU32(in BlueNoiseRankMap rankMap)
    {
        if (rankMap is null) throw new ArgumentNullException(nameof(rankMap));

        uint[] result = new uint[rankMap.Length];
        TensorPrimitives.ConvertTruncating<ushort, uint>(rankMap.RanksSpan, result);
        return result;
    }

    public static float[] ToNormalizedF32(in BlueNoiseRankMap rankMap)
    {
        if (rankMap is null) throw new ArgumentNullException(nameof(rankMap));

        int n = rankMap.Length;
        float[] result = new float[n];

        // Convert ranks -> float.
        TensorPrimitives.ConvertTruncating<ushort, float>(rankMap.RanksSpan, result);

        // Map integer ranks 0..(N-1) to evenly-spaced values in [0,1).
        // Using the bin-center (rank + 0.5) / N gives nicer symmetry than rank / (N-1).
        float invN = 1f / n;
        TensorPrimitives.Multiply(result, invN, result);
        TensorPrimitives.Add(result, 0.5f * invN, result);

        // Defensive clamp; should already be in [0,1).
        TensorPrimitives.Clamp(result, 0f, 1f, result);

        return result;
    }

    public static byte[] ToL8(in BlueNoiseRankMap rankMap)
    {
        if (rankMap is null) throw new ArgumentNullException(nameof(rankMap));

        int n = rankMap.Length;

        // Match the common "LDR" mapping used in practice: floor(rank * 256 / N) in [0,255].
        float[] tmp = new float[n];
        TensorPrimitives.ConvertTruncating<ushort, float>(rankMap.RanksSpan, tmp);

        float scale = 256f / n;
        TensorPrimitives.Multiply(tmp, scale, tmp);
        TensorPrimitives.Clamp(tmp, 0f, 255f, tmp);

        byte[] result = new byte[n];
        TensorPrimitives.ConvertTruncating<float, byte>(tmp, result);
        return result;
    }

    public static byte[] ToBinaryMaskByFillRatio(in BlueNoiseRankMap rankMap, float fillRatio)
    {
        if (rankMap is null) throw new ArgumentNullException(nameof(rankMap));
        if (!float.IsFinite(fillRatio) || fillRatio < 0f || fillRatio > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(fillRatio), fillRatio, "Fill ratio must be finite and in [0, 1].");
        }

        int n = rankMap.Length;
        int thresholdExclusive = (int)MathF.Round(fillRatio * n);
        thresholdExclusive = Math.Clamp(thresholdExclusive, 0, n);

        return ToBinaryMaskByRankThreshold(rankMap, thresholdExclusive);
    }

    public static byte[] ToBinaryMaskByRankThreshold(in BlueNoiseRankMap rankMap, int thresholdExclusive)
    {
        if (rankMap is null) throw new ArgumentNullException(nameof(rankMap));

        int n = rankMap.Length;
        if ((uint)thresholdExclusive > (uint)n)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdExclusive), thresholdExclusive, $"Threshold must be in [0, {n}].");
        }

        // 1 when rank < thresholdExclusive, else 0.
        byte[] mask = new byte[n];
        ReadOnlySpan<ushort> ranks = rankMap.RanksSpan;

        for (int i = 0; i < n; i++)
        {
            mask[i] = ranks[i] < thresholdExclusive ? (byte)1 : (byte)0;
        }

        return mask;
    }
}
