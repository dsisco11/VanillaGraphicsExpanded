using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.Noise;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Noise;

public sealed class PmjTests
{
    private static PmjConfig CreateConfig(int sampleCount, uint seed, bool owenScramble = false, uint salt = 0)
    {
        return new PmjConfig(
            SampleCount: sampleCount,
            Seed: seed,
            Variant: PmjVariant.Pmj02,
            OutputKind: PmjOutputKind.Vector2F32,
            OwenScramble: owenScramble,
            Salt: salt,
            Centered: false);
    }

    [Fact]
    public void Pmj02_IsDeterministic_ForSameConfig()
    {
        var config = CreateConfig(sampleCount: 256, seed: 123u, owenScramble: true, salt: 77u);

        PmjSequence a = PmjGenerator.Generate(config);
        PmjSequence b = PmjGenerator.Generate(config);

        Assert.Equal(a.Count, b.Count);
        Assert.True(a.PointsSpan.SequenceEqual(b.PointsSpan));
    }

    [Fact]
    public void Pmj02_DifferentSeed_ChangesOutput()
    {
        var aCfg = CreateConfig(sampleCount: 256, seed: 123u, owenScramble: true, salt: 77u);
        var bCfg = CreateConfig(sampleCount: 256, seed: 124u, owenScramble: true, salt: 77u);

        PmjSequence a = PmjGenerator.Generate(aCfg);
        PmjSequence b = PmjGenerator.Generate(bCfg);

        Assert.False(a.PointsSpan.SequenceEqual(b.PointsSpan));
    }

    [Fact]
    public void Pmj02_RangeChecks_AllPointsFinite_AndInBounds()
    {
        var config = CreateConfig(sampleCount: 1024, seed: 42u, owenScramble: false);
        PmjSequence seq = PmjGenerator.Generate(config);

        foreach (Vector2 p in seq.PointsSpan)
        {
            Assert.False(float.IsNaN(p.X));
            Assert.False(float.IsNaN(p.Y));
            Assert.False(float.IsInfinity(p.X));
            Assert.False(float.IsInfinity(p.Y));

            Assert.True(p.X >= 0f && p.X < 1f, $"X out of range: {p.X}");
            Assert.True(p.Y >= 0f && p.Y < 1f, $"Y out of range: {p.Y}");
        }
    }

    [Fact]
    public void Pmj02_StratifcationSanity_Prefix64_HasPerfect1DBinCounts()
    {
        // For a power-of-two prefix, Sobol-based sequences should be exactly stratified in 1D base-2 bins.
        var config = CreateConfig(sampleCount: 64, seed: 0u, owenScramble: false);
        PmjSequence seq = PmjGenerator.Generate(config);

        const int bins = 8;
        int[] xCounts = new int[bins];
        int[] yCounts = new int[bins];

        foreach (Vector2 p in seq.PointsSpan)
        {
            int bx = (int)(p.X * bins);
            int by = (int)(p.Y * bins);

            bx = Math.Clamp(bx, 0, bins - 1);
            by = Math.Clamp(by, 0, bins - 1);

            xCounts[bx]++;
            yCounts[by]++;
        }

        for (int i = 0; i < bins; i++)
        {
            Assert.Equal(64 / bins, xCounts[i]);
            Assert.Equal(64 / bins, yCounts[i]);
        }
    }

    [Fact]
    public void Pmj02_StratifcationSanity_MultiplePrefixes_HavePerfect1DBinCounts()
    {
        // Stronger regression guard: for power-of-two prefixes and power-of-two bin counts that evenly divide,
        // we expect perfect 1D stratification in both dimensions.
        static void AssertPerfect1DBins(ReadOnlySpan<Vector2> points, int bins)
        {
            int[] xCounts = new int[bins];
            int[] yCounts = new int[bins];

            foreach (Vector2 p in points)
            {
                int bx = Math.Clamp((int)(p.X * bins), 0, bins - 1);
                int by = Math.Clamp((int)(p.Y * bins), 0, bins - 1);
                xCounts[bx]++;
                yCounts[by]++;
            }

            int expected = points.Length / bins;
            for (int i = 0; i < bins; i++)
            {
                Assert.Equal(expected, xCounts[i]);
                Assert.Equal(expected, yCounts[i]);
            }
        }

        (int prefix, int bins)[] cases =
        [
            (prefix: 16, bins: 4),
            (prefix: 64, bins: 8),
            (prefix: 256, bins: 16),
        ];

        foreach ((int prefix, int bins) in cases)
        {
            var config = CreateConfig(sampleCount: prefix, seed: 0u, owenScramble: false);
            PmjSequence seq = PmjGenerator.Generate(config);

            Assert.Equal(prefix, seq.Count);
            AssertPerfect1DBins(seq.PointsSpan, bins);
        }
    }

    [Fact]
    public void Pmj02_OwenScrambleSalt_AffectsOutput_WhenEnabled()
    {
        var aCfg = CreateConfig(sampleCount: 256, seed: 123u, owenScramble: true, salt: 1u);
        var bCfg = CreateConfig(sampleCount: 256, seed: 123u, owenScramble: true, salt: 2u);

        PmjSequence a = PmjGenerator.Generate(aCfg);
        PmjSequence b = PmjGenerator.Generate(bCfg);

        Assert.False(a.PointsSpan.SequenceEqual(b.PointsSpan));
    }

    [Fact]
    public void Pmj02_PackedRg16UNorm_HasNoExactDuplicates_ForTypicalConfigs()
    {
        // Practical guard against catastrophic bugs (e.g., generator stuck, constant output,
        // or incorrect quantization producing repeated jitter offsets).
        var config = CreateConfig(sampleCount: 1024, seed: 42u, owenScramble: true, salt: 0u);
        PmjSequence seq = PmjGenerator.Generate(config);

        ushort[] rg = PmjConversions.ToRg16UNormInterleaved(seq);
        Assert.Equal(seq.Count * 2, rg.Length);

        var seen = new HashSet<uint>(capacity: seq.Count);
        for (int i = 0; i < seq.Count; i++)
        {
            ushort u = rg[(i * 2) + 0];
            ushort v = rg[(i * 2) + 1];
            uint packed = ((uint)u << 16) | v;

            bool added = seen.Add(packed);
            Assert.True(added, $"Duplicate packed RG16 value at index {i} (u={u}, v={v})");
        }
    }

    [Fact]
    public void Cache_CreatesOnce_PerKey()
    {
        var cache = new PmjCache();
        var config = CreateConfig(sampleCount: 256, seed: 123u, owenScramble: true, salt: 77u);

        int factoryCalls = 0;
        PmjSequence Factory(PmjConfig c)
        {
            factoryCalls++;
            return PmjGenerator.Generate(c);
        }

        PmjSequence a = cache.GetOrCreateSequence(config, Factory);
        PmjSequence b = cache.GetOrCreateSequence(config, Factory);

        Assert.Equal(1, factoryCalls);
        Assert.Same(a, b);

        Assert.True(cache.TryGetSequence(config, out PmjSequence? cached));
        Assert.Same(a, cached);
    }
}
