using System.Numerics;
using System.Runtime.Intrinsics;
using VanillaGraphicsExpanded.Imaging;

namespace VanillaGraphicsExpanded.Tests.Unit.Imaging;

/// <summary>Compares complete histogram outputs for scalar and SIMD accumulation kernels.</summary>
public sealed class AlbedoHistogramSimdTests
{
    #region Kernel parity
    /// <summary>Random colors, alpha cutoffs and incomplete vector tails retain identical accepted samples and bins.</summary>
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 3, 64)]
    [InlineData(7, 7, 64)]
    [InlineData(9, 9, 255)]
    [InlineData(257, 257, 64)]
    [InlineData(6144, 4096, 64)]
    [InlineData(65537, 4093, 0)]
    public void RandomAndStratifiedSamplesMatchScalar(int pixelCount, int samples, int threshold)
    {
        var random = new Random(1597);
        var pixels = new int[pixelCount];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = unchecked((int)random.NextInt64(0, 1L << 32));
        AssertKernels(pixels, samples, (byte)threshold);
    }

    /// <summary>Byte extremes, the sRGB transfer boundary and alpha equality exercise lane conversion boundaries.</summary>
    [Fact]
    public void ChannelAndAlphaBoundariesMatchScalar()
    {
        byte[] channels = [0, 1, 10, 11, 127, 128, 254, 255];
        byte[] alphas = [0, 63, 64, 65, 254, 255];
        var pixels = new List<int>();
        foreach (byte alpha in alphas)
            foreach (byte red in channels)
                foreach (byte green in channels)
                    foreach (byte blue in channels)
                        pixels.Add((alpha << 24) | (red << 16) | (green << 8) | blue);
        AssertKernels(pixels.ToArray(), pixels.Count, 64);
    }

    /// <summary>Repeated destinations must accumulate every lane even when all lanes select one histogram bin.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void RepeatedBinsAndTailsMatchScalar(int channel)
    {
        int color = unchecked((int)0xff000000) | (channel << 16) | (channel << 8) | channel;
        int[] pixels = Enumerable.Repeat(color, 4111).ToArray();
        AssertKernels(pixels, pixels.Length, 64);
    }
    #endregion

    #region Histogram assertions
    /// <summary>Checks all bins and RGB sums, including bins that should remain untouched.</summary>
    private static void AssertKernels(int[] pixels, int samples, byte threshold)
    {
        var expectedCounts = new int[256];
        var expectedSums = new Vector3[256];
        int expectedAccepted = AlbedoAverager.AccumulateScalar(pixels, pixels.Length, samples, threshold, expectedCounts, expectedSums);
        foreach (int width in new[] { 128, 256 })
        {
            if (width == 128 && !Vector128.IsHardwareAccelerated || width == 256 && !Vector256.IsHardwareAccelerated) continue;
            var actualCounts = new int[256];
            var actualSums = new Vector3[256];
            int actualAccepted = width == 128
                ? AlbedoAverager.AccumulateVector128(pixels, pixels.Length, samples, threshold, actualCounts, actualSums)
                : AlbedoAverager.AccumulateVector256(pixels, pixels.Length, samples, threshold, actualCounts, actualSums);
            Assert.Equal(expectedAccepted, actualAccepted);
            Assert.Equal(expectedCounts, actualCounts);
            for (int bin = 0; bin < 256; bin++)
            {
                Assert.Equal(expectedSums[bin].X, actualSums[bin].X);
                Assert.Equal(expectedSums[bin].Y, actualSums[bin].Y);
                Assert.Equal(expectedSums[bin].Z, actualSums[bin].Z);
            }
        }
    }
    #endregion
}
