using System;
using System.Numerics;

using VanillaGraphicsExpanded.Imaging;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Imaging;

public sealed class AlbedoAveragerTests
{
    [Fact]
    public void TryComputeAverageLinearRgb_AllOpaqueWhite_ReturnsNearOne()
    {
        int[] px =
        [
            Argb(255, 255, 255, 255), Argb(255, 255, 255, 255),
            Argb(255, 255, 255, 255), Argb(255, 255, 255, 255),
        ];

        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
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

    [Fact]
    public void TryComputeAverageLinearRgb_HalfWhiteHalfBlack_ReturnsNearPointFive()
    {
        int[] px =
        [
            Argb(255, 255, 255, 255), Argb(255, 0, 0, 0),
            Argb(255, 255, 255, 255), Argb(255, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
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

    [Fact]
    public void TryComputeAverageLinearRgb_AlphaCutout_IgnoresBelowThreshold()
    {
        // One fully transparent white pixel should be ignored.
        int[] px =
        [
            Argb(0, 255, 255, 255), Argb(255, 0, 0, 0),
            Argb(255, 0, 0, 0),     Argb(255, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
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

    [Fact]
    public void TryComputeAverageLinearRgb_AllTransparent_FallsBackToNoAlphaReject()
    {
        // All pixels are below the threshold; the implementation should fall back to counting all pixels.
        int[] px =
        [
            Argb(0, 255, 255, 255), Argb(0, 0, 0, 0),
            Argb(0, 255, 255, 255), Argb(0, 0, 0, 0),
        ];

        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
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

    [Fact]
    public void TryComputeAverageLinearRgb_MaxSamplesForcesStride_SamplesSubsetDeterministically()
    {
        // 4x4 image where only (0,0) is white, rest black.
        // With maxSamples=1, stride becomes 4 and we sample only the (0,0) pixel.
        var px = new int[16];
        Array.Fill(px, Argb(255, 0, 0, 0));
        px[0] = Argb(255, 255, 255, 255);

        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
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

    [Fact]
    public void TryComputeAverageLinearRgb_InvalidDimensions_ReturnsFalse()
    {
        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
            argbPixels: [Argb(255, 0, 0, 0)],
            width: 0,
            height: 1,
            averageLinearRgb: out _,
            reason: out string? reason);

        Assert.False(ok);
        Assert.Contains("invalid", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryComputeAverageLinearRgb_InsufficientPixels_ReturnsFalse()
    {
        bool ok = AlbedoAverager.TryComputeAverageLinearRgb(
            argbPixels: Array.Empty<int>(),
            width: 2,
            height: 2,
            averageLinearRgb: out _,
            reason: out string? reason);

        Assert.False(ok);
        Assert.Contains("insufficient", reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static int Argb(byte a, byte r, byte g, byte b)
        => (a << 24) | (r << 16) | (g << 8) | b;
}
