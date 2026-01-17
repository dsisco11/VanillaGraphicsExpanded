using System;
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
