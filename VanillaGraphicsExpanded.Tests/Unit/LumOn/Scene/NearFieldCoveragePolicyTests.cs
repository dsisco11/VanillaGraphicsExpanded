using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Coverage requirements include supported origins, complete trace reach, and adjacent lighting.</summary>
public sealed class NearFieldCoveragePolicyTests
{
    #region Bounded coverage
    /// <summary>Every fractional offset across a publication cell retains the complete required and prefetched domain.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-16777216)]
    [InlineData(16777216)]
    public void FullIntervalPreservesTraceEnvelope(double anchor)
    {
        var policy=new NearFieldCoveragePolicy();
        const double reach=20;
        for(int i=0;i<=64;i++)
        {
            var position=new PartitionPoint(anchor+i*.25,anchor-i*.25,anchor+i*.125);
            Assert.True(policy.TryPlan(position,reach,out var plan));
            Assert.Equal(0,plan.WindowOrigin.X%16); Assert.Equal(0,plan.WindowOrigin.Y%16);Assert.Equal(0,plan.WindowOrigin.Z%16);
            double margin=reach+16+1;
            Assert.Equal(position.X-16-margin,plan.Required.Min.X);
            Assert.Equal(position.Y+16+margin,plan.Required.Max.Y);
            Assert.True(plan.WindowOrigin.X<=plan.Required.Min.X-16);
            Assert.True(plan.WindowOrigin.Y<=plan.Required.Min.Y-16);
            Assert.True(plan.WindowOrigin.Z<=plan.Required.Min.Z-16);
            Assert.True(plan.WindowOrigin.X+plan.Resolution>=plan.Required.Max.X+16);
            Assert.True(plan.WindowOrigin.Y+plan.Resolution>=plan.Required.Max.Y+16);
            Assert.True(plan.WindowOrigin.Z+plan.Resolution>=plan.Required.Max.Z+16);
        }
    }
    /// <summary>Unsupported reach and GPU coordinate envelopes fail explicitly instead of cropping or wrapping.</summary>
    [Fact]
    public void UnsupportedEnvelopeIsRejected()
    {
        var policy=new NearFieldCoveragePolicy();
        Assert.False(policy.TryPlan(new(),1000,out _));
        Assert.False(policy.TryPlan(new(int.MaxValue,0,0),20,out _));
        Assert.False(policy.TryPlan(new(int.MinValue,0,0),20,out _));
        Assert.False(policy.TryPlan(new(40_000_000,0,0),20,out _));
        Assert.False(policy.TryPlan(new(-40_000_000,0,0),20,out _));
        Assert.False(policy.TryPlan(new(),double.NaN,out _));
        Assert.False(policy.TryPlan(new(),0,out _));
        Assert.False(new NearFieldCoveragePolicy(MaximumResolution:32).TryPlan(new(),20,out _));
    }
    /// <summary>Largest cache spacing contributes to conservative reach independently from camera radius.</summary>
    [Fact]
    public void MaximumReachIncludesEveryProbeLevel()
    {
        Assert.Equal(2*Math.Sqrt(3)*8*4,NearFieldCoveragePolicy.MaximumTraceReach(20,8,3));
        Assert.Equal(200,NearFieldCoveragePolicy.MaximumTraceReach(200,8,3));
    }
    #endregion
}
