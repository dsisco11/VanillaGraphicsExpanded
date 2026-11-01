using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks irradiance normalization independently of ray travel distance.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheLightingTermsTests : RenderTestBase
{
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
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        float expected=32f*GgxHemisphereReference.CapturedDiffuse(reflectance);
        ImageAssertions.Predicate(fixture.Page.IrradianceAtlas,
            "return bvec2(all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && a.a==1,false);",
            parameters:new(expected-.1f,expected+.1f,0,0)).AssertEqual();
    }

    /// <summary>Resolved directions cannot contribute a biased average when another direction exhausts traversal.</summary>
    [Fact]
    public void IncompleteHemisphereBatchPreservesHistory()
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture((id, x, y, z) =>
            new(x <= 0 || x >= 2 ? 2u | id << 2 : 1u,
                LumonSceneOccupancyPacking.PackClamped(32, 0, 0, 0), 0));
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 1);
        using var firstDirections = new GpuImageSnapshot(fixture.Page.IrradianceAtlas);
        fixture.Page.ResetLighting();
        Assert.False(fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 16));
        // The first ray is identical; require at least one texel whose complete batch is rejected.
        var mixed=ImageAssertions.Predicate(fixture.Page.IrradianceAtlas,
            "bool selected=b.a==1 && a.a==0;return bvec2(!selected || all(equal(a.rgb,vec3(0))),selected);",second:firstDirections.Texture);
        mixed.AssertEqual();Assert.True(mixed.MatchingTexels>0,"Fixture must exercise a mixed resolved/unresolved hemisphere batch.");
    }

    /// <summary>A saturated history must not add energy when another equal-light batch arrives.</summary>
    [Fact]
    public void CappedHistoryPreservesConstantIrradiance()
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture(32);
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        using var expected = new GpuImageSnapshot(fixture.Page.IrradianceAtlas);
        ImageAssertions.CompareConstant(expected.Texture,1,channelMask:8).AssertEqual();
        // Preserve the completed representable sample; only the authored temporal weight changes.
        using var transform=new GpuImageTransform();
        using var history=transform.Apply(expected.Texture,"return vec4(a.rgb,1024);");
        var area=new VanillaGraphicsExpanded.Rendering.RHI.RenderArea(0,0,8,8);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BlitImages(new(
            VanillaGraphicsExpanded.Rendering.RHI.RHIAttachment.FromTexture(history),area,
            VanillaGraphicsExpanded.Rendering.RHI.RHIAttachment.FromTexture(fixture.Page.IrradianceAtlas),area));
        for(int repeat=0;repeat<3;repeat++)
        {
            Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
            ImageAssertions.CompareHistory(fixture.Page.IrradianceAtlas,expected,channelMask:7).AssertEqual();
            ImageAssertions.CompareConstant(fixture.Page.IrradianceAtlas,1024,channelMask:8).AssertEqual();
        }
    }
    #endregion
}
