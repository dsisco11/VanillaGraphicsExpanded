using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks all-or-nothing cache lookup completion and the full original ray denominator.</summary>
public sealed class SurfaceFallbackEstimatesTests
{
    #region Complete estimators
    /// <summary>Confirmed sky contributes zero radiance but retains its share of the cosine estimator denominator.</summary>
    [Fact]
    public void SkyAndHitsUseOriginalRayCount()
    {
        var request=new SurfaceFallbackRequest{Page=2,Slot=3,Patch=4,Linear=5,Fraction=new(0,0,0,4)};
        var query=new SurfaceLightingQuery{Result=new(8,4,2,1)};
        var result=new SurfaceFallbackResult([new(request,0,1,true)],[query],[]);
        var commit=Assert.Single(SurfaceFallbackEstimates.Resolve(result,[query]));
        Assert.Equal(new Vector4(2*MathF.PI,MathF.PI,.5f*MathF.PI,1),commit.Estimate);
        Assert.Equal(2u,commit.Page);Assert.Equal(3u,commit.Slot);Assert.Equal(4u,commit.Patch);Assert.Equal(5u,commit.Linear);
    }

    /// <summary>A sky-only completed ray batch produces a valid zero indirect sample.</summary>
    [Fact]
    public void ConfirmedSkyProducesValidZero()
    {
        var result=new SurfaceFallbackResult([new(new(){Fraction=new(0,0,0,4)},0,0,true)],[],[]);
        Assert.Equal(new Vector4(0,0,0,1),Assert.Single(SurfaceFallbackEstimates.Resolve(result,[])).Estimate);
    }

    /// <summary>Unavailable or nonfinite hit answers reject the whole texel and cannot dilute its denominator.</summary>
    [Theory]
    [InlineData(false,1f)] [InlineData(true,float.NaN)] [InlineData(true,float.PositiveInfinity)]
    public void MissingOrInvalidHitNeverPublishesPartialEstimator(bool available,float value)
    {
        var good=new SurfaceLightingQuery{Result=new(8,4,2,1)};
        var bad=new SurfaceLightingQuery{Result=new(value,2,3,available?1:0)};
        var result=new SurfaceFallbackResult([new(new(){Fraction=new(0,0,0,4)},0,2,true)],[good,bad],[]);
        Assert.Empty(SurfaceFallbackEstimates.Resolve(result,[good,bad]));
    }

    /// <summary>A geometry-incomplete texel remains unresolved even when all earlier hit lookups succeeded.</summary>
    [Fact]
    public void IncompleteGeometryCannotPublishCachedPrefix()
    {
        var query=new SurfaceLightingQuery{Result=new(8,4,2,1)};
        var result=new SurfaceFallbackResult([new(new(){Fraction=new(0,0,0,4)},0,1,false)],[query],[]);
        Assert.Empty(SurfaceFallbackEstimates.Resolve(result,[query]));
    }
    #endregion
}
