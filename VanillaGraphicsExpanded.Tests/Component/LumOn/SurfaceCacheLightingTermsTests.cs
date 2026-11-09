using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks irradiance normalization independently of ray travel distance.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheLightingTermsTests : RenderTestBase
{


    #region Public API
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceCacheLightingTermsTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Irradiance estimation
    /// <summary>A uniform enclosure yields irradiance scaled by hit reflectance, independent of wall distance.</summary>
    [Theory]
    [InlineData(5, 1f)]
    [InlineData(10, 1f)]
    [InlineData(5, .5f)]
    public void UniformEnclosurePreservesIrradianceAcrossTravelDistances(int extent, float reflectance)
    {
        // Arrange: Keep the receiving patch and radiance fixed while varying enclosure distance and reflectance.
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture((id, x, y, z) =>
        {
            // The +X receiving patch is fixed; moving the other walls changes ray lengths only.
            bool wall = x <= 0 || x >= extent || y <= 32 - extent || y >= 36 + extent || z <= -extent || z >= 4 + extent;
            uint geometry = wall ? 2u | id << 2 : 1u;
            // Deliberately omit outside-cell material IDs: reflectance must come from the hit.
            uint light = LumonSceneOccupancyPacking.PackClamped(32, 0, 0, 0);
            return new(geometry, light, 0);
        }, new System.Numerics.Vector3(reflectance));
        // Act: Capture and relight the same receiving patch inside the selected enclosure.
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        // Assert: Irradiance depends on hit reflectance, not ray travel distance.
        float expected=32f*GgxHemisphereReference.CapturedDiffuse(reflectance);
        ImageAssertions.Predicates.Predicate(fixture.Page.IrradianceAtlas, SurfacePredicates.SurfaceCacheLightingTermsTests_UniformEnclosurePreservesIrradianceAcrossTravelDistances,
            parameters:new(expected-.1f,expected+.1f,0,0)).AssertEqual();
    }

    /// <summary>Resolved directions cannot contribute a biased average when another direction exhausts traversal.</summary>
    [Fact]
    public void IncompleteHemisphereBatchPreservesHistory()
    {
        // Arrange: Capture the enclosure, save a single resolved direction, then reset lighting.
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture((id, x, y, z) =>
            new(x <= 0 || x >= 2 ? 2u | id << 2 : 1u,
                LumonSceneOccupancyPacking.PackClamped(32, 0, 0, 0), 0));
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 1);
        using var firstDirections = new GpuImageSnapshot(fixture.Page.IrradianceAtlas);
        fixture.Page.ResetLighting();
        // Act: Request a full direction batch whose short traversal limit leaves one direction unresolved.
        Assert.False(fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 16));
        // The first ray is identical; require at least one texel whose complete batch is rejected.
        // Assert: The mixed batch preserves zero history wherever it did not complete.
        var mixed=ImageAssertions.Predicates.Predicate(fixture.Page.IrradianceAtlas, SurfacePredicates.SurfaceCacheLightingTermsTests_IncompleteHemisphereBatchPreservesHistory,second:firstDirections.Texture);
        mixed.AssertEqual();Assert.True(mixed.MatchingTexels>0,"Fixture must exercise a mixed resolved/unresolved hemisphere batch.");
    }

    /// <summary>A saturated history must not add energy when another equal-light batch arrives.</summary>
    [Fact]
    public void CappedHistoryPreservesConstantIrradiance()
    {
        // Arrange: Build one valid sample, then load it with the maximum representable history weight.
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture(32);
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        using var expected = new GpuImageSnapshot(fixture.Page.IrradianceAtlas);
        ImageAssertions.Comparisons.CompareConstant(expected.Texture,1,channelMask:8).AssertEqual();
        // Preserve the completed representable sample; only the authored temporal weight changes.
        var transform = ImageAssertions.Projections;
        using var history=transform.Apply(expected.Texture, SurfaceProjections.SurfaceCacheLightingTermsTests_CappedHistoryPreservesConstantIrradiance);
        var area=new VanillaGraphicsExpanded.Rendering.RHI.RenderArea(0,0,8,8);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BlitImages(new(
            VanillaGraphicsExpanded.Rendering.RHI.RHIAttachment.FromTexture(history),area,
            VanillaGraphicsExpanded.Rendering.RHI.RHIAttachment.FromTexture(fixture.Page.IrradianceAtlas),area));
        // Act: Submit three identical relight batches against the saturated history.
        // Assert: RGB remains constant and the encoded history weight stays capped on every repetition.
        for(int repeat=0;repeat<3;repeat++)
        {
            Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
            ImageAssertions.Comparisons.CompareHistory(fixture.Page.IrradianceAtlas,expected,channelMask:7).AssertEqual();
            ImageAssertions.Comparisons.CompareConstant(fixture.Page.IrradianceAtlas,1024,channelMask:8).AssertEqual();
        }
    }
    #endregion

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
        programs.Prepare("projection-surface", 0, 0);
    }
    #endregion
}
