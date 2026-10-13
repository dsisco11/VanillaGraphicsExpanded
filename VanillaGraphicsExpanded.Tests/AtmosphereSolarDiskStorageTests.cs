using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks visible-disk metadata against independent double arithmetic without changing physical illumination.</summary>
public sealed class AtmosphereSolarDiskStorageTests
{
    #region Public API
    /// <summary>The published multiplier conservatively respects glare headroom even for the largest finite source.</summary>
    [Theory]
    [InlineData(70000000f, 1f, 1f)]
    [InlineData(70000000f, 2f, 1f)]
    [InlineData(70000000f, 1.3f, 1f)]
    [InlineData(float.MaxValue, 1f, 1f)]
    public void ScaleDoesNotExceedIndependentHeadroom(float peak, float bloom, float shafts)
    {
        var physical = new Vector3(peak, peak * .5f, 0);
        float limit = AtmosphereSolarDiskStorage.MaximumScale(physical, bloom, shafts);
        double cap = Math.Min(48000d, 64000d / Math.Max(1d, Math.Max(bloom, shafts)) - 16000d);
        Assert.True(float.IsFinite(limit) && limit > 0);
        Assert.InRange((double)peak * limit, cap * .999999, cap);
        Assert.InRange(peak * limit, 0, cap);
        Assert.Equal(peak * limit * .5f, physical.Y * limit);
        Assert.Equal(new Vector3(peak, peak * .5f, 0), physical);
    }

    /// <summary>A retained-light frame scale below the disk cap passes through without changing the physical source.</summary>
    [Fact]
    public void MinimumFrameScaleRemainsAvailable()
    {
        var physical = new Vector3(70000000f, 35000000f, 0);
        float limit = AtmosphereSolarDiskStorage.MaximumScale(physical, 2, 2);
        Assert.True(limit > LightingStorage.MinimumFrameScale);
        Assert.Equal(LightingStorage.MinimumFrameScale, Math.Min(limit, LightingStorage.MinimumFrameScale));
        Assert.InRange(physical.X * LightingStorage.MinimumFrameScale, 0, 16000);
        Assert.Equal(float.MaxValue, AtmosphereSolarDiskStorage.MaximumScale(Vector3.Zero, 2, 2));
    }

    /// <summary>Unsupported physical or glare metadata returns an unavailable publication rather than an unsafe multiplier.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void InvalidInputsAreUnavailable(int kind)
    {
        Vector3 source = kind switch { 0 => new(-1), 1 => new(float.NaN), 2 => new(float.PositiveInfinity), _ => Vector3.One };
        float bloom = kind switch { 3 => 3, 4 => -1, 5 => float.NaN, _ => 1 };
        float shafts = kind switch { 6 => float.PositiveInfinity, 7 => 3, _ => 1 };
        Assert.Equal(0, AtmosphereSolarDiskStorage.MaximumScale(source, bloom, shafts));
    }
    #endregion
}
