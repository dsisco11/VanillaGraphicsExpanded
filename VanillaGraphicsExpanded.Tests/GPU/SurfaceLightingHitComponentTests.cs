using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Proves controlled cache lighting reaches directional hits independently of runtime orchestration.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingHitComponentTests : SurfaceLightingHitTestBase
{
    /// <summary>Uses the shared material-isolated GPU context.</summary>
    public SurfaceLightingHitComponentTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Source lighting transitions
    /// <summary>Changing only source lighting removes and restores energy at the isolated hit boundary.</summary>
    [Fact]
    public void OffscreenLightRemovalAndRestorationReachHitRadiance()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        var bright=RenderHits(room);AssertHits(bright,true);
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var dark=RenderHits(room);AssertHits(dark,false);
        var hits=ObserveTrace(dark,"bool hit=(flags&1u)!=0u; return bvec2(!hit || b.r==1.0,hit);");
        hits.AssertEqual(); Assert.True(hits.MatchingTexels>32);
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var restored=RenderHits(room);AssertHits(restored,true);
        ImageAssertions.Predicate(bright.Radiance,"return bvec2(same(a,b,15u),false);",restored.Radiance).AssertEqual();
    }

    /// <summary>Bright exterior fields cannot illuminate a sealed dark enclosure at the hit boundary.</summary>
    [Fact]
    public void SealedRoomRejectsBrightExterior()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(blockLight:0){ExteriorBlockLight=32};
        room.Geometry.Dirty();room.Geometry.Publish();room.Seed();room.Bounce();
        var dark=RenderHits(room);AssertHits(dark,false);
        var hits=ObserveTrace(dark,"bool hit=(flags&1u)!=0u; return bvec2(!hit || b.r==1.0,hit);");
        hits.AssertEqual(); Assert.True(hits.MatchingTexels>32);
    }

    /// <summary>Emission survives zero diffuse reflectance and remains the only source of hit radiance.</summary>
    [Fact]
    public void EmissiveOnlyCacheLightsHitRadiance()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(0,0,4){EmissionPolicy=true};room.Seed();
        var pixels=RenderHits(room);AssertHits(pixels,true);
        Assert.InRange(AssertEnergy(pixels,true,"emission"),3.99f,4.01f);
        room.EmissionPolicy=false;room.Seed();AssertHits(RenderHits(room),false);
    }

    /// <summary>A previous-generation bounce increases hit radiance instead of remaining isolated in the cache.</summary>
    [Fact]
    public void MultipleBouncesReachHitRadiance()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        var seed=RenderHits(room);room.Bounce();
        var bounce=RenderHits(room);room.Bounce();
        var second=RenderHits(room);
        AssertHits(seed,true);AssertHits(bounce,true);AssertHits(second,true);
        Assert.True(TraceRed(bounce)>TraceRed(seed)*1.1f);
        Assert.True(TraceRed(second)>TraceRed(bounce)*1.01f);
    }
    #endregion

    #region Publication lifetime
    /// <summary>Unpublished and stale pages never leak retained bright texels through hit evaluation.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidPagesDoNotReachHitRadiance(bool stale)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();AssertHits(RenderHits(room),true);
        if(stale) room.StaleSlots();else room.WithholdPages();
        var missing=RenderHits(room);AssertHits(missing,false);
        ImageAssertions.CompareConstant(missing.Meta,0,channelMask:1).AssertEqual();
    }

    /// <summary>Replacing all geometry and cache resources cannot retain illumination from the retired enclosure.</summary>
    [Fact]
    public void RecreatedDarkCacheDoesNotReuseBrightLighting()
    {
        EnsureShaderTestAvailable();
        using(var bright=new SurfaceLightingEnclosureFixture()) { bright.Seed();AssertHits(RenderHits(bright),true); }
        using(var dark=new SurfaceLightingEnclosureFixture(blockLight:0)) { dark.Seed();AssertHits(RenderHits(dark),false); }
        using(var restored=new SurfaceLightingEnclosureFixture()) { restored.Seed();AssertHits(RenderHits(restored),true); }
    }
    #endregion
}
