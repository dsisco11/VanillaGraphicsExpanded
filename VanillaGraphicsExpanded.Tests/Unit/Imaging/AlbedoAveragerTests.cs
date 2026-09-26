using System;
using System.Numerics;

using VanillaGraphicsExpanded.Imaging;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Imaging;

/// <summary>Checks bounded representative color selection and alpha compatibility.</summary>
public sealed class AlbedoAveragerTests
{
    #region Robust representative color
    /// <summary>Sparse bright and dark accents do not shift a predominantly uniform material.</summary>
    [Fact]
    public void SparseExtremesAreTrimmedWithoutChangingMainColor()
    {
        int[] pixels = Enumerable.Repeat(Argb(255, 90, 130, 170), 100).ToArray();
        for (int i = 0; i < 4; i++) { pixels[i] = Argb(255, 255, 255, 255); pixels[99 - i] = Argb(255, 0, 0, 0); }
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 100, 1, out var actual, out _));
        Assert.Equal(Linear(90), actual.X, 5);
        Assert.Equal(Linear(130), actual.Y, 5);
        Assert.Equal(Linear(170), actual.Z, 5);
    }

    /// <summary>Balanced colored populations keep their RGB contributions instead of choosing one median color.</summary>
    [Fact]
    public void BalancedRedAndBlueRetainBothColors()
    {
        int[] pixels = Enumerable.Repeat(Argb(255, 255, 0, 0), 50)
            .Concat(Enumerable.Repeat(Argb(255, 0, 0, 255), 50)).ToArray();
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 100, 1, out var actual, out _));
        Assert.Equal(0.5f, actual.X, 6);
        Assert.Equal(0, actual.Y);
        Assert.Equal(0.5f, actual.Z, 6);
    }

    /// <summary>Elongated images sample each stratum and produce deterministic results regardless of orientation.</summary>
    [Theory]
    [InlineData(100, 1)]
    [InlineData(1, 100)]
    public void ElongatedImagesSampleOnlyBoundedPositions(int width, int height)
    {
        int[] pixels = Enumerable.Repeat(Argb(255, 0, 0, 0), 100).ToArray();
        for (int i = 0; i < 100; i++) if ((i / 25) % 2 == 0) pixels[i] = Argb(255, 255, 255, 255);
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, width, height, out var actual, out _, maxSamples: 4));
        Assert.Equal(new Vector3(0.5f), actual);
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, width, height, out var full, out _, maxSamples: 0));
        Assert.Equal(new Vector3(0.5f), full);
    }

    /// <summary>Jittered strata avoid locking every sample to one color or one alpha value of periodic stripes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PeriodicStripesDoNotAliasToOnePopulation(bool alphaMask)
    {
        var pixels = new int[1024 * 1024];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = alphaMask
                ? Argb((byte)((i & 1) == 0 ? 0 : 255), 0, 0, 0)
                : ((i & 1) == 0 ? Argb(255, 255, 255, 255) : Argb(255, 0, 0, 0));
        if (alphaMask)
            for (int i = 0; i < pixels.Length; i += 2) pixels[i] = Argb(0, 255, 255, 255);
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 1024, 1024, out var first, out _));
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 1024, 1024, out var second, out _));
        Assert.Equal(first, second);
        if (alphaMask) Assert.Equal(Vector3.Zero, first);
        else Assert.InRange(first.X, 0.46f, 0.54f);
    }

    /// <summary>Nonintegral strata preserve area proportions before symmetric tail trimming.</summary>
    [Fact]
    public void FractionalStrataPreserveTwoThirdsWhitePopulation()
    {
        int[] pixels = Enumerable.Range(0, 6144).Select(i => i % 3 == 0 ? Argb(255, 0, 0, 0) : Argb(255, 255, 255, 255)).ToArray();
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 6144, 1, out var actual, out _));
        float expected = (2f / 3f - 0.05f) / 0.9f;
        Assert.InRange(actual.X, expected - 0.04f, expected + 0.04f);
    }

    /// <summary>The alpha threshold includes equality and counts accepted pixels equally regardless of opacity.</summary>
    [Fact]
    public void AlphaThresholdIncludesBoundaryWithoutOpacityWeighting()
    {
        int[] pixels = [Argb(63, 0, 255, 0), Argb(64, 255, 0, 0), Argb(255, 0, 0, 255)];
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb(pixels, 3, 1, out var actual, out _));
        Assert.Equal(new Vector3(0.5f, 0, 0.5f), actual);
    }

    /// <summary>A one-pixel material survives fractional trimming and keeps its linear color.</summary>
    [Fact]
    public void SinglePixelRetainsColor()
    {
        Assert.True(AlbedoAverager.TryComputeRepresentativeLinearRgb([Argb(255, 100, 150, 200)], 1, 1, out var actual, out _));
        Assert.Equal(Linear(100), actual.X, 6);
        Assert.Equal(Linear(150), actual.Y, 6);
        Assert.Equal(Linear(200), actual.Z, 6);
    }
    #endregion

    #region Existing input compatibility
    /// <summary>Preserves input compatibility: AllOpaqueWhite ReturnsNearOne.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_AllOpaqueWhite_ReturnsNearOne()
    {
        int[] px =
        [
            Argb(255, 255, 255, 255), Argb(255, 255, 255, 255),
            Argb(255, 255, 255, 255), Argb(255, 255, 255, 255),
        ];

        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: px,
            width: 2,
            height: 2,
            averageLinearRgb: out Vector3 avg,
            reason: out _);

        Assert.True(ok);
        Assert.InRange(avg.X, 0.999f, 1.001f);
        Assert.InRange(avg.Y, 0.999f, 1.001f);
        Assert.InRange(avg.Z, 0.999f, 1.001f);
    }

    /// <summary>Preserves input compatibility: HalfWhiteHalfBlack ReturnsNearPointFive.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_HalfWhiteHalfBlack_ReturnsNearPointFive()
    {
        int[] px =
        [
            Argb(255, 255, 255, 255), Argb(255, 0, 0, 0),
            Argb(255, 255, 255, 255), Argb(255, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: px,
            width: 2,
            height: 2,
            averageLinearRgb: out Vector3 avg,
            reason: out _);

        Assert.True(ok);
        Assert.InRange(avg.X, 0.499f, 0.501f);
        Assert.InRange(avg.Y, 0.499f, 0.501f);
        Assert.InRange(avg.Z, 0.499f, 0.501f);
    }

    /// <summary>Preserves input compatibility: AlphaCutout IgnoresBelowThreshold.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_AlphaCutout_IgnoresBelowThreshold()
    {
        // One fully transparent white pixel should be ignored.
        int[] px =
        [
            Argb(0, 255, 255, 255), Argb(255, 0, 0, 0),
            Argb(255, 0, 0, 0),     Argb(255, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: px,
            width: 2,
            height: 2,
            averageLinearRgb: out Vector3 avg,
            reason: out _,
            alphaCutoutThreshold: 64);

        Assert.True(ok);
        Assert.InRange(avg.X, -1e-6f, 1e-6f);
        Assert.InRange(avg.Y, -1e-6f, 1e-6f);
        Assert.InRange(avg.Z, -1e-6f, 1e-6f);
    }

    /// <summary>Preserves input compatibility: AllTransparent FallsBackToNoAlphaReject.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_AllTransparent_FallsBackToNoAlphaReject()
    {
        // All pixels are below the threshold; the implementation should fall back to counting all pixels.
        int[] px =
        [
            Argb(0, 255, 255, 255), Argb(0, 0, 0, 0),
            Argb(0, 255, 255, 255), Argb(0, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: px,
            width: 2,
            height: 2,
            averageLinearRgb: out Vector3 avg,
            reason: out _,
            alphaCutoutThreshold: 64);

        Assert.True(ok);
        Assert.InRange(avg.X, 0.499f, 0.501f);
        Assert.InRange(avg.Y, 0.499f, 0.501f);
        Assert.InRange(avg.Z, 0.499f, 0.501f);
    }

    /// <summary>Preserves input compatibility: MaxSamplesBoundsWork SamplesSubsetDeterministically.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_MaxSamplesBoundsWork_SamplesSubsetDeterministically()
    {
        // 4x4 image where only (0,0) is white, rest black.
        // With maxSamples=1, only the first flattened pixel is sampled.
        var px = new int[16];
        Array.Fill(px, Argb(255, 0, 0, 0));
        px[0] = Argb(255, 255, 255, 255);

        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: px,
            width: 4,
            height: 4,
            averageLinearRgb: out Vector3 avg,
            reason: out _,
            maxSamples: 1);

        Assert.True(ok);
        Assert.InRange(avg.X, 0.999f, 1.001f);
        Assert.InRange(avg.Y, 0.999f, 1.001f);
        Assert.InRange(avg.Z, 0.999f, 1.001f);
    }

    /// <summary>Preserves input compatibility: InvalidDimensions ReturnsFalse.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_InvalidDimensions_ReturnsFalse()
    {
        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: [Argb(255, 0, 0, 0)],
            width: 0,
            height: 1,
            averageLinearRgb: out _,
            reason: out string? reason);

        Assert.False(ok);
        Assert.Contains("invalid", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Preserves input compatibility: InsufficientPixels ReturnsFalse.</summary>
    [Fact]
    public void TryComputeRepresentativeLinearRgb_InsufficientPixels_ReturnsFalse()
    {
        bool ok = AlbedoAverager.TryComputeRepresentativeLinearRgb(
            argbPixels: Array.Empty<int>(),
            width: 2,
            height: 2,
            averageLinearRgb: out _,
            reason: out string? reason);

        Assert.False(ok);
        Assert.Contains("insufficient", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Reference helpers
    /// <summary>Converts a channel independently using the standard sRGB transfer function.</summary>
    private static float Linear(byte channel)
    {
        float value = channel / 255f;
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>Packs test pixels in the engine bitmap convention.</summary>
    private static int Argb(byte a, byte r, byte g, byte b)
        => (a << 24) | (r << 16) | (g << 8) | b;
    #endregion
}
