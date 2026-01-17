using System;
using VanillaGraphicsExpanded.Noise;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Noise;

public sealed class BlueNoiseTests
{
    private static BlueNoiseConfig CreateFastConfig(uint seed, bool tileable)
    {
        return new BlueNoiseConfig(
            Width: 32,
            Height: 32,
            Slices: 1,
            Tileable: tileable,
            Seed: seed,
            Algorithm: BlueNoiseAlgorithm.VoidAndCluster,
            OutputKind: BlueNoiseOutputKind.RankU16,
            Sigma: 1.5f,
            InitialFillRatio: 0.10f,
            MaxIterations: 256,
            StagnationLimit: 0);
    }

    [Fact]
    public void VoidAndCluster_IsDeterministic_ForSameConfig()
    {
        var config = CreateFastConfig(seed: 123u, tileable: true);

        BlueNoiseRankMap a = VoidAndClusterGenerator.GenerateRankMap(config);
        BlueNoiseRankMap b = VoidAndClusterGenerator.GenerateRankMap(config);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Length, b.Length);
        Assert.True(a.RanksSpan.SequenceEqual(b.RanksSpan));
    }

    [Fact]
    public void VoidAndCluster_DifferentSeed_ChangesOutput()
    {
        var aCfg = CreateFastConfig(seed: 123u, tileable: true);
        var bCfg = CreateFastConfig(seed: 124u, tileable: true);

        BlueNoiseRankMap a = VoidAndClusterGenerator.GenerateRankMap(aCfg);
        BlueNoiseRankMap b = VoidAndClusterGenerator.GenerateRankMap(bCfg);

        Assert.False(a.RanksSpan.SequenceEqual(b.RanksSpan));
    }

    [Fact]
    public void Tileable_SeamNeighborDiff_IsNotPathological()
    {
        // Sanity check: when tileable, the wrap-neighbor diffs along the seams
        // should be of similar scale as interior neighbor diffs.
        // This doesn’t prove spectral properties, but catches obvious non-wrapping bugs.
        var config = CreateFastConfig(seed: 999u, tileable: true);
        BlueNoiseRankMap map = VoidAndClusterGenerator.GenerateRankMap(config);

        int w = map.Width;
        int h = map.Height;
        ReadOnlySpan<ushort> r = map.RanksSpan;

        double seamHoriz = 0;
        double interiorHoriz = 0;
        int seamCount = 0;
        int interiorCount = 0;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;

            seamHoriz += Math.Abs((int)r[row + 0] - (int)r[row + (w - 1)]);
            seamCount++;

            for (int x = 1; x < w; x++)
            {
                interiorHoriz += Math.Abs((int)r[row + x] - (int)r[row + (x - 1)]);
                interiorCount++;
            }
        }

        double seamMean = seamHoriz / seamCount;
        double interiorMean = interiorHoriz / interiorCount;

        // Wide tolerance: just ensure the seam is not wildly different.
        Assert.InRange(seamMean, interiorMean * 0.25, interiorMean * 4.0);
    }

    [Fact]
    public void Conversions_AreInRange_AndLengthsMatch()
    {
        var config = CreateFastConfig(seed: 42u, tileable: true);
        BlueNoiseRankMap map = VoidAndClusterGenerator.GenerateRankMap(config);

        float[] normalized = BlueNoiseConversions.ToNormalizedF32(map);
        Assert.Equal(map.Length, normalized.Length);
        foreach (float v in normalized)
        {
            Assert.False(float.IsNaN(v));
            Assert.InRange(v, 0f, 1f);
        }

        byte[] l8 = BlueNoiseConversions.ToL8(map);
        Assert.Equal(map.Length, l8.Length);

        uint[] u32 = BlueNoiseConversions.ToRankU32(map);
        Assert.Equal(map.Length, u32.Length);

        byte[] mask = BlueNoiseConversions.ToBinaryMaskByFillRatio(map, 0.25f);
        Assert.Equal(map.Length, mask.Length);
        foreach (byte b in mask)
        {
            Assert.True(b is 0 or 1);
        }
    }

    [Fact]
    public void BinaryMask_FillRatio_ProducesExpectedOnCount()
    {
        var config = CreateFastConfig(seed: 7u, tileable: true);
        BlueNoiseRankMap map = VoidAndClusterGenerator.GenerateRankMap(config);

        const float fill = 0.25f;
        byte[] mask = BlueNoiseConversions.ToBinaryMaskByFillRatio(map, fill);

        int expected = (int)MathF.Round(fill * map.Length);
        expected = Math.Clamp(expected, 0, map.Length);

        int actual = 0;
        foreach (byte b in mask)
        {
            actual += b;
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Cache_CreatesOnce_PerKey()
    {
        var cache = new BlueNoiseCache();
        var config = CreateFastConfig(seed: 123u, tileable: true);

        int factoryCalls = 0;
        BlueNoiseRankMap Factory(BlueNoiseConfig c)
        {
            factoryCalls++;
            return VoidAndClusterGenerator.GenerateRankMap(c);
        }

        BlueNoiseRankMap a = cache.GetOrCreateRankMap(config, Factory);
        BlueNoiseRankMap b = cache.GetOrCreateRankMap(config, Factory);

        Assert.Equal(1, factoryCalls);
        Assert.Same(a, b);

        Assert.True(cache.TryGetRankMap(config, out BlueNoiseRankMap? cached));
        Assert.Same(a, cached);
    }
}
