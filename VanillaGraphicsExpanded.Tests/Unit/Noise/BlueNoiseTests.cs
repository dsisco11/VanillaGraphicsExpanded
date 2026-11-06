using System;
using VanillaGraphicsExpanded.Noise;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Noise;

/// <summary>Verifies blue-noise generation, rank conversion, mask thresholds, and cache identity independently.</summary>
public sealed class BlueNoiseTests
{
    #region Public API
    #region Generation and conversion
    /// <summary>Schedule relabeling is deterministic, preserves complete ranks and leaves baseline slice zero intact.</summary>
    [Fact]
    public void CloudRefreshSchedulePreservesRanksAndBaseline()
    {
        // Arrange
        const int side=8,pixels=side*side;
        var input=Enumerable.Range(0,pixels*8).Select(i=>((i*17)%pixels+.5f)/pixels).ToArray();
        // Act
        var result=VanillaGraphicsExpanded.PBR.Clouds.CloudRefreshSchedule.Create(input,side,8);
        // Assert
        Assert.Equal(result,VanillaGraphicsExpanded.PBR.Clouds.CloudRefreshSchedule.Create(input,side,8));
        Assert.Equal(input[..pixels],result[..pixels]);
        for(int slice=0;slice<8;slice++)Assert.Equal(input.AsSpan(slice*pixels,pixels).ToArray().Order(),result.AsSpan(slice*pixels,pixels).ToArray().Order());
    }

    /// <summary>Generation produces the same complete rank permutation for identical settings.</summary>
    [Fact]
    public void VoidAndCluster_IsDeterministic_ForSameConfig()
    {
        // Arrange
        var config = CreateFastConfig(seed: 123u, tileable: true);
        // Act
        BlueNoiseRankMap a = VoidAndClusterGenerator.GenerateRankMap(config);
        BlueNoiseRankMap b = VoidAndClusterGenerator.GenerateRankMap(config);
        // Assert
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Length, b.Length);
        Assert.True(a.RanksSpan.SequenceEqual(b.RanksSpan));
        Assert.True(a.RanksSpan.ToArray().Order().SequenceEqual(Enumerable.Range(0, a.Length).Select(rank => (ushort)rank)));
    }

    /// <summary>Changing the generator seed changes the generated rank ordering.</summary>
    [Fact]
    public void VoidAndCluster_DifferentSeed_ChangesOutput()
    {
        // Arrange
        var aCfg = CreateFastConfig(seed: 123u, tileable: true);
        var bCfg = CreateFastConfig(seed: 124u, tileable: true);
        // Act
        BlueNoiseRankMap a = VoidAndClusterGenerator.GenerateRankMap(aCfg);
        BlueNoiseRankMap b = VoidAndClusterGenerator.GenerateRankMap(bCfg);
        // Assert
        Assert.False(a.RanksSpan.SequenceEqual(b.RanksSpan));
    }

    /// <summary>Tileable generation keeps wrap-neighbor differences in a plausible range.</summary>
    [Fact]
    public void Tileable_SeamNeighborDiff_IsNotPathological()
    {
        // Arrange
        // Sanity check: when tileable, the wrap-neighbor diffs along the seams
        // should be of similar scale as interior neighbor diffs.
        // This doesn’t prove spectral properties, but catches obvious non-wrapping bugs.
        var config = CreateFastConfig(seed: 999u, tileable: true);
        // Act
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

        // Assert: a wide tolerance only rejects seams that are far worse than interior neighbors.
        Assert.InRange(seamMean, interiorMean * 0.25, interiorMean * 4.0);
    }

    /// <summary>Rank conversions preserve lengths and produce finite in-range normalized and byte values.</summary>
    [Fact]
    public void Conversions_AreInRange_AndLengthsMatch()
    {
        // Arrange: a complete authored permutation isolates conversion rules from generation cost.
        BlueNoiseRankMap map = CreateAuthoredRankMap();
        // Act
        float[] normalized = BlueNoiseConversions.ToNormalizedF32(map);
        byte[] l8 = BlueNoiseConversions.ToL8(map);
        uint[] u32 = BlueNoiseConversions.ToRankU32(map);
        byte[] mask = BlueNoiseConversions.ToBinaryMaskByFillRatio(map, 0.25f);
        // Assert
        Assert.Equal(map.Length, normalized.Length);
        foreach (float v in normalized)
        {
            Assert.False(float.IsNaN(v));
            Assert.InRange(v, 0f, 1f);
        }

        Assert.Equal(map.Length, l8.Length);
        Assert.Equal(map.Length, u32.Length);
        Assert.Equal(map.Length, mask.Length);
        foreach (byte b in mask)
        {
            Assert.True(b is 0 or 1);
        }
    }

    /// <summary>Fill-ratio conversion sets exactly the requested number of mask entries for a complete permutation.</summary>
    [Fact]
    public void BinaryMask_FillRatio_ProducesExpectedOnCount()
    {
        // Arrange
        BlueNoiseRankMap map = CreateAuthoredRankMap();
        const float fill = 0.25f;
        // Act
        byte[] mask = BlueNoiseConversions.ToBinaryMaskByFillRatio(map, fill);

        int expected = (int)MathF.Round(fill * map.Length);
        expected = Math.Clamp(expected, 0, map.Length);

        // Assert
        int actual = 0;
        foreach (byte b in mask)
        {
            actual += b;
        }

        Assert.Equal(expected, actual);
    }
    #endregion

    #region Cache
    /// <summary>The cache evaluates its factory once and returns the same authored map for repeated lookup.</summary>
    [Fact]
    public void Cache_CreatesOnce_PerKey()
    {
        // Arrange
        var cache = new BlueNoiseCache();
        var config = CreateFastConfig(seed: 123u, tileable: true);

        int factoryCalls = 0;
        BlueNoiseRankMap Factory(BlueNoiseConfig c)
        {
            factoryCalls++;
            return CreateAuthoredRankMap(descending: true);
        }

        // Act
        BlueNoiseRankMap a = cache.GetOrCreateRankMap(config, Factory);
        BlueNoiseRankMap b = cache.GetOrCreateRankMap(config, Factory);

        // Assert
        Assert.Equal(1, factoryCalls);
        Assert.Same(a, b);
        Assert.Equal((ushort)1023, a.RanksSpan[0]);

        Assert.True(cache.TryGetRankMap(config, out BlueNoiseRankMap? cached));
        Assert.Same(a, cached);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates the bounded settings shared by the generator contract tests.</summary>
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

    /// <summary>Authors a complete 32 by 32 rank permutation for consumer and cache contract tests.</summary>
    private static BlueNoiseRankMap CreateAuthoredRankMap(bool descending = false)
    {
        const int width = 32;
        const int count = width * width;
        ushort[] ranks = Enumerable.Range(0, count)
            .Select(index => (ushort)(descending ? count - 1 - index : index))
            .ToArray();
        return new(width, width, ranks);
    }
    #endregion
}
