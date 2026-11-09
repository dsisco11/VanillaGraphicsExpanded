using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Proves controlled cache lighting reaches directional hits independently of runtime orchestration.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingHitComponentTests : SurfaceLightingHitTestBase
{
    #region Public API


    /// <summary>Uses the shared material-isolated GPU context.</summary>
    public SurfaceLightingHitComponentTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Source lighting transitions
    /// <summary>Changing only source lighting removes and restores energy at the isolated hit boundary.</summary>
    [Fact]
    public void OffscreenLightRemovalAndRestorationReachHitRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        // Act
        var bright=RenderHits(room);
        // Assert
        AssertHits(bright,true);
        // Act: remove the source, publish its geometry, and reseed before observing the hit.
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var dark=RenderHits(room);
        // Assert
        AssertHits(dark,false);
        var hits=ObserveTrace(dark, SurfacePredicates.SurfaceLightingHitComponentTests_OffscreenLightRemovalAndRestorationReachHitRadiance);
        hits.AssertEqual(); Assert.True(hits.MatchingTexels>32);
        // Act: restore the source and publish the refreshed lighting before rendering again.
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var restored=RenderHits(room);
        // Assert
        AssertHits(restored,true);
        ImageAssertions.Predicates.Predicate(bright.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5,restored.Radiance).AssertEqual();
    }

    /// <summary>Bright exterior fields cannot illuminate a sealed dark enclosure at the hit boundary.</summary>
    [Fact]
    public void SealedRoomRejectsBrightExterior()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight:0){ExteriorBlockLight=32};
        room.Geometry.Dirty();room.Geometry.Publish();
        // Act
        room.Seed();room.Bounce();
        var dark=RenderHits(room);
        // Assert
        AssertHits(dark,false);
        var hits=ObserveTrace(dark, SurfacePredicates.SurfaceLightingHitComponentTests_OffscreenLightRemovalAndRestorationReachHitRadiance);
        hits.AssertEqual(); Assert.True(hits.MatchingTexels>32);
    }

    /// <summary>Emission survives zero diffuse reflectance and remains the only source of hit radiance.</summary>
    [Fact]
    public void EmissiveOnlyCacheLightsHitRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, 0,0,4){EmissionPolicy=true};
        // Act
        room.Seed();
        var pixels=RenderHits(room);
        // Assert
        AssertHits(pixels,true);
        Assert.InRange(AssertEnergy(pixels,true,"emission"),3.99f,4.01f);
        // Act
        room.EmissionPolicy=false;room.Seed();
        // Assert
        AssertHits(RenderHits(room),false);
    }

    /// <summary>A previous-generation bounce increases hit radiance instead of remaining isolated in the cache.</summary>
    [Fact]
    public void MultipleBouncesReachHitRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        // Act
        var seed=RenderHits(room);room.Bounce();
        var bounce=RenderHits(room);room.Bounce();
        var second=RenderHits(room);
        // Assert
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
        // Arrange
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        // Act
        // Assert the initial valid-page render in the same expression that performs it.
        AssertHits(RenderHits(room),true);
        // Act: invalidate the page or its identity, then render against that state.
        if(stale) room.StaleSlots();else room.WithholdPages();
        var missing=RenderHits(room);
        // Assert
        AssertHits(missing,false);
        ImageAssertions.Comparisons.CompareConstant(missing.Meta,0,channelMask:1).AssertEqual();
    }

    /// <summary>Replacing all geometry and cache resources cannot retain illumination from the retired enclosure.</summary>
    [Fact]
    public void RecreatedDarkCacheDoesNotReuseBrightLighting()
    {
        // Arrange
        EnsureShaderTestAvailable();
        // Act
        using(var bright=new SurfaceLightingEnclosureFixture(ImageAssertions)) { bright.Seed();
            // Assert
            AssertHits(RenderHits(bright),true); }
        // Act
        using(var dark=new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight:0)) { dark.Seed();
            // Assert
            AssertHits(RenderHits(dark),false); }
        // Act
        using(var restored=new SurfaceLightingEnclosureFixture(ImageAssertions)) { restored.Seed();
            // Assert
            AssertHits(RenderHits(restored),true); }
    }
    #endregion
    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-cloud", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
