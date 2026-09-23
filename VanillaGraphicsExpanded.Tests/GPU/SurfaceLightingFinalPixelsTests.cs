using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Proves controlled cache lighting reaches final pixels through both gather implementations.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingFinalPixelsTests : SurfaceLightingPipelineTestBase
{
    /// <summary>Uses the shared material-isolated GPU context.</summary>
    public SurfaceLightingFinalPixelsTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Source lighting transitions
    /// <summary>Changing only source lighting removes and restores energy at every downstream boundary.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OffscreenLightRemovalAndRestorationReachFinalPixels(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        var bright=RenderLighting(room,sh9);AssertPipeline(bright,true);
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var dark=RenderLighting(room,sh9);AssertPipeline(dark,false);
        int[] hits=Enumerable.Range(0,dark.Meta.Length/2).Where(i=>(Flags(dark.Meta[i*2+1])&1u)!=0).ToArray();
        Assert.True(hits.Length>32);
        Assert.All(hits,i=>Assert.Equal(1,dark.Meta[i*2]));
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var restored=RenderLighting(room,sh9);AssertPipeline(restored,true);
        Assert.Equal(bright.Final,restored.Final);
    }

    /// <summary>Bright exterior fields cannot illuminate a sealed dark enclosure at any downstream boundary.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SealedRoomRejectsBrightExterior(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(blockLight:0){ExteriorBlockLight=32};
        room.Geometry.Dirty();room.Geometry.Publish();room.Seed();room.Bounce();
        var dark=RenderLighting(room,sh9);AssertPipeline(dark,false);
        int[] hits=Enumerable.Range(0,dark.Meta.Length/2).Where(i=>(Flags(dark.Meta[i*2+1])&1u)!=0).ToArray();
        Assert.True(hits.Length>32);
        Assert.All(hits,i=>Assert.Equal(1,dark.Meta[i*2]));
    }

    /// <summary>Emission survives zero diffuse reflectance and remains the only source of final illumination.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmissiveOnlyCacheLightsFinalPixels(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(0,0,4){EmissionPolicy=true};room.Seed();
        var pixels=RenderLighting(room,sh9);AssertPipeline(pixels,true);
        Assert.InRange(AssertEnergy(pixels.Trace,true,"emission"),3.99f,4.01f);
        room.EmissionPolicy=false;room.Seed();AssertPipeline(RenderLighting(room,sh9),false);
    }

    /// <summary>A previous-generation bounce increases final illumination instead of remaining isolated in the cache.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultipleBouncesReachFinalPixels(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        var seed=RenderLighting(room,sh9);room.Bounce();
        var bounce=RenderLighting(room,sh9);room.Bounce();
        var second=RenderLighting(room,sh9);
        AssertPipeline(seed,true);AssertPipeline(bounce,true);AssertPipeline(second,true);
        Assert.True(bounce.Final[0]>seed.Final[0]*1.1f);
        Assert.True(second.Final[0]>bounce.Final[0]*1.01f);
    }
    #endregion

    #region Publication lifetime
    /// <summary>Unpublished and stale pages never leak retained bright texels through filtering or gather.</summary>
    [Theory]
    [InlineData(false,false)] [InlineData(true,false)]
    [InlineData(false,true)] [InlineData(true,true)]
    public void InvalidPagesDoNotReachFinalPixels(bool sh9,bool stale)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();AssertPipeline(RenderLighting(room,sh9),true);
        if(stale) room.StaleSlots();else room.WithholdPages();
        var missing=RenderLighting(room,sh9);AssertPipeline(missing,false);
        Assert.All(Enumerable.Range(0,missing.Meta.Length/2),i=>Assert.Equal(0,missing.Meta[i*2]));
    }

    /// <summary>Replacing all geometry and cache resources cannot retain illumination from the retired enclosure.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RecreatedDarkCacheDoesNotReuseBrightLighting(bool sh9)
    {
        EnsureShaderTestAvailable();
        using(var bright=new SurfaceLightingEnclosureFixture()) { bright.Seed();AssertPipeline(RenderLighting(bright,sh9),true); }
        using(var dark=new SurfaceLightingEnclosureFixture(blockLight:0)) { dark.Seed();AssertPipeline(RenderLighting(dark,sh9),false); }
        using(var restored=new SurfaceLightingEnclosureFixture()) { restored.Seed();AssertPipeline(RenderLighting(restored,sh9),true); }
    }
    #endregion
}
