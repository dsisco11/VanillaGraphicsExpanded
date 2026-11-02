using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks associative publication separately from ordered numerical accumulation.</summary>
public sealed partial class GpuImageAssertionTests
{
    #region Public API
    /// <summary>Excluded channels cannot masquerade as computed zero moments, including rounded-product statistics.</summary>
    [Fact]
    public void MomentResultsExposeOnlySelectedChannels()
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(image, [1f, 2f, 3f, 4f], PixelComponentType.Float);
        foreach (bool squareInFloat in new[] { false, true })
        {
            var result = ImageAssertions.Statistics.Statistics(image, channelMask: 2, squareInFloat: squareInFloat);
            Assert.Equal(2d, result.Sum(1));
            Assert.Equal(4d, result.SumSquares(1));
            Assert.Throws<InvalidOperationException>(() => result.Sum(0));
            Assert.Throws<InvalidOperationException>(() => result.SumSquares(0));
        }
    }

    /// <summary>Every tail lane joins collectives without inventing visits, failures or selected components.</summary>
    [Theory]
    [InlineData(1)] [InlineData(63)] [InlineData(64)] [InlineData(65)]
    [InlineData(129)] [InlineData(195)] [InlineData(66049)]
    public void WorkgroupCountersRetainExactDomainAndEarliestFailure(int count)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, count > 4096 ? 257 : count, count > 4096 ? 257 : 1);
        int total = image.Width * image.Height;
        var values = new float[total];
        values[^1] = 2;
        if (total > 64) values[63] = 1;
        Upload(image, values, PixelComponentType.Float);
        var result = ImageAssertions.Comparisons.CompareConstant(image, 0);
        Assert.Equal((uint)total, result.VisitedTexels);
        Assert.Equal((uint)total, result.ComparedComponents);
        Assert.Equal(total > 64 ? 2u : 1u, result.Mismatches);
        Assert.Equal((uint)total - result.Mismatches, result.ConstantMatches);
        int first = total > 64 ? 63 : total - 1;
        Assert.Contains($"({first % image.Width},{first / image.Width},0), channel=0", result.ToString());
        var inspection = ImageAssertions.Statistics.Inspect(image);
        Assert.Equal((uint)total, inspection.Components);
        Assert.Equal(result.Mismatches, inspection.Positive);
        Assert.Equal(2d, inspection.Maximum);
    }

    /// <summary>A suppressed scalar dispatch cannot report a valid ordered sum from reset output storage.</summary>
    [Fact]
    public void OrderedScalarRequiresItsOwnCompletion()
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 1);
        Upload(image, new float[65], PixelComponentType.Float);
        using var native = new NativeDriverCapture();
        int calls = 0;
        native.Add("glDispatchCompute", new Dispatch((x, y, z) => calls++));
        native.Install();
        Assert.Throws<InvalidOperationException>(() => ImageAssertions.Statistics.OrderedFloatSum(image,
            ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
        Assert.Equal(1, calls);
        native.RequireSuccessfulObservation();
    }

    /// <summary>Bounded scalar transport retains the original rejection of nonfinite ordered sums.</summary>
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void OrderedScalarRejectsNonfiniteResults(float value)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Upload(image, [value], PixelComponentType.Float);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ImageAssertions.Statistics.OrderedFloatSum(image,
            ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
    }

    #endregion
}
