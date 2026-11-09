using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks associative publication separately from ordered numerical accumulation.</summary>
public sealed partial class GpuImageAssertionTests
{
    #region Public API
    /// <summary>Excluded channels cannot masquerade as computed zero moments, including rounded-product statistics.</summary>
    [Fact]
    public void MomentResultsExposeOnlySelectedChannels()
    {
        // Arrange: author distinct channels so inclusion and unavailable-channel behavior cannot coincide.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(image, [1f, 2f, 3f, 4f], PixelComponentType.Float);
        foreach (bool squareInFloat in new[] { false, true })
        {
            // Act: compute moments with only green selected under both product precision modes.
            var result = ImageAssertions.Statistics.Statistics(image, channelMask: 2, squareInFloat: squareInFloat);
            // Assert: selected moments are exact and omitted channels remain unavailable.
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
        // Arrange: place failures in separate workgroups when the selected extent crosses a group boundary.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, count > 4096 ? 257 : count, count > 4096 ? 257 : 1);
        int total = image.Width * image.Height;
        var values = new float[total];
        values[^1] = 2;
        if (total > 64) values[63] = 1;
        Upload(image, values, PixelComponentType.Float);
        // Act: reduce the complete image, then run an independent statistics observation.
        var result = ImageAssertions.Comparisons.CompareConstant(image, 0);
        // Assert: every tail lane is counted once and the earliest failure retains its source coordinate.
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
        // A second reduction provides an independent witness for the comparison's complete result domain.
    }

    /// <summary>A suppressed scalar dispatch cannot report a valid ordered sum from reset output storage.</summary>
    [Fact]
    public void OrderedScalarRequiresItsOwnCompletion()
    {
        // Arrange: suppress the ordered-sum dispatch after preparing a valid image and native-call witness.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 1);
        Upload(image, new float[65], PixelComponentType.Float);
        using var native = new NativeDriverCapture();
        int calls = 0;
        native.Add("glDispatchCompute", new Dispatch((x, y, z) => calls++));
        native.Install();
        // Act: request a scalar result whose output storage was reset but never produced.
        Assert.Throws<InvalidOperationException>(() => ImageAssertions.Statistics.OrderedFloatSum(image,
            ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
        // Assert: suppressed native work cannot be mistaken for a completed scalar sum.
        Assert.Equal(1, calls);
        native.RequireSuccessfulObservation();
    }

    /// <summary>Bounded scalar transport retains the original rejection of nonfinite ordered sums.</summary>
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void OrderedScalarRejectsNonfiniteResults(float value)
    {
        EnsureContextValid();
        // Arrange a one-pixel scalar texture containing the nonfinite value selected by this theory row.
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Upload(image, [value], PixelComponentType.Float);
        // Act: request a bounded scalar reduction from the authored nonfinite value.
        // Assert: the result cannot be represented by the finite scalar transport contract.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ImageAssertions.Statistics.OrderedFloatSum(image,
            ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
    }

    #endregion
}
