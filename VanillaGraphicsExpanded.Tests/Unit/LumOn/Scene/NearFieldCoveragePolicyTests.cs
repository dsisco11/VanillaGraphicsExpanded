using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Fixed-window coverage remains bounded across movement and world boundaries.</summary>
public sealed class NearFieldCoveragePolicyTests
{
    #region Bounded coverage
    /// <summary>Every fractional offset retains the camera cell and its 26 immediate neighbors.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-16777216)]
    [InlineData(16777216)]
    public void FullIntervalPreservesFixedWindow(double anchor)
    {
        var policy = new NearFieldCoveragePolicy();
        var layout = new PartitionLayout(new(16, 16, 16));
        for (int i = 0; i <= 64; i++)
        {
            var position = new PartitionPoint(anchor + i * .25, anchor - i * .25, anchor + i * .125);
            Assert.True(policy.TryPlan(position, 1000, out var plan));
            Assert.Equal(48, plan.Resolution);
            Assert.Equal((Math.Floor(position.X / 16) - 1) * 16, plan.WindowOrigin.X);
            Assert.Equal((Math.Floor(position.Y / 16) - 1) * 16, plan.WindowOrigin.Y);
            Assert.Equal((Math.Floor(position.Z / 16) - 1) * 16, plan.WindowOrigin.Z);
            Assert.Equal(27, layout.Intersecting(plan.Required).Count());
            Assert.Equal(plan.Origins, plan.Required);
            Assert.InRange(position.X - plan.WindowOrigin.X, 16, 32);
        }
    }

    /// <summary>Invalid positions and distances are rejected without imposing a ray-dependent allocation size.</summary>
    [Fact]
    public void UnsupportedEnvelopeIsRejected()
    {
        var policy = new NearFieldCoveragePolicy();
        Assert.True(policy.TryPlan(new(), 1000, out _));
        Assert.False(policy.TryPlan(new(int.MaxValue, 0, 0), 20, out _));
        Assert.False(policy.TryPlan(new(int.MinValue, 0, 0), 20, out _));
        Assert.False(policy.TryPlan(new(40_000_000, 0, 0), 20, out _));
        Assert.False(policy.TryPlan(new(-40_000_000, 0, 0), 20, out _));
        Assert.False(policy.TryPlan(new(double.NaN, 0, 0), 20, out _));
        Assert.False(policy.TryPlan(new(), double.NaN, out _));
        Assert.False(policy.TryPlan(new(), 0, out _));
    }

    /// <summary>Out-of-world vertical neighbors never become retryable loading demand.</summary>
    [Theory]
    [InlineData(3, 0, 32)]
    [InlineData(255, 224, 256)]
    public void VerticalWorldBoundsClipDemand(double y, double bottom, double top)
    {
        Assert.True(new NearFieldCoveragePolicy().TryPlan(new(100, y, 100), 20, out var plan, 256));
        Assert.Equal(48, plan.Resolution);
        Assert.Equal(bottom, plan.Required.Min.Y);
        Assert.Equal(top, plan.Required.Max.Y);
        Assert.Equal(18, new PartitionLayout(new(16, 16, 16)).Intersecting(plan.Required).Count());
    }

    /// <summary>Largest cache spacing contributes to traversal distance without changing the window.</summary>
    [Fact]
    public void MaximumReachIncludesEveryProbeLevel()
    {
        Assert.Equal(2 * Math.Sqrt(3) * 8 * 4, NearFieldCoveragePolicy.MaximumTraceReach(20, 8, 3));
        Assert.Equal(200, NearFieldCoveragePolicy.MaximumTraceReach(200, 8, 3));
    }
    #endregion
}
