using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares screen-resolved and voxel-resolved lighting for the same non-emissive room.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Screen Hit Lighting
    /// <summary>Screen and offscreen geometry agree on unavailable cache lighting while preserving opaque hits.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(1f)]
    public void ScreenHits_AndOffscreenHitsRequireCachePublication(float lighting)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        float depth = ScreenDepthAt(-7); // Interior face of the back wall occupies z=-8..-7.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var offScreen = Trace(fixture);
        var onScreen = Trace(fixture, screenDepth: depth);
        var suppressed = Trace(fixture, screenDepth: depth, suppress: true);
        ImageAssertions.Predicate(onScreen.Radiance, "return bvec2(same(a,b,15u),false);", suppressed.Radiance).AssertEqual();
        ImageAssertions.Predicate(onScreen.Meta, "return bvec2(same(a,b,3u),false);", suppressed.Meta).AssertEqual();
        var checkedDirections = ObserveScreenTrace(onScreen, screenOnly, """
            bool hit=(floatBitsToUint(c.g)&1u)!=0u;
            bool valid=b.r==0.0 && (flags&1u)==1u && (flags&32u)==0u
                && all(equal(d.rgb,vec3(0))) && all(lessThanEqual(abs(a.rgb-d.rgb),vec3(0.002)));
            return bvec2(!hit || valid,hit);
            """, offScreen);
        checkedDirections.AssertEqual(); uint checkedHits=checkedDirections.MatchingTexels;
        Assert.True(checkedHits > 0, "The visible plane must produce accepted screen hits.");
    }

    /// <summary>A geometric screen hit without a ready local lighting source must not publish confident darkness.</summary>
    [Fact]
    public void ScreenHits_UnavailableLighting_RemainsUnresolved()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var result = Trace(fixture, screenDepth: depth);
        var checkedDirections = ObserveScreenTrace(result, screenOnly, """
            bool hit=(floatBitsToUint(c.g)&1u)!=0u;
            return bvec2(!hit || (b.r==0.0 && all(equal(a.rgb,vec3(0)))),hit);
            """);
        checkedDirections.AssertEqual(); uint checkedHits=checkedDirections.MatchingTexels;
        Assert.True(checkedHits > 0);
    }

    /// <summary>Screen emission survives absent geometry, but a known opaque hit requires its own published cache lighting.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScreenHits_EmissionIsNotDoubled_AndSurvivesUnavailableLocalData(bool publish)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        if (publish) fixture.Publish(world, new Vector4(1, 1, 1, 0.25f));
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 0.25f, emissionBoost: 2);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2);
        var suppressed = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2, suppress: true);
        ImageAssertions.Predicate(result.Radiance, "return bvec2(same(a,b,15u),false);", suppressed.Radiance).AssertEqual();
        ImageAssertions.Predicate(result.Meta, "return bvec2(same(a,b,3u),false);", suppressed.Meta).AssertEqual();
        var checkedDirections = ObserveScreenTrace(result, screenOnly, """
            bool hit=(floatBitsToUint(c.g)&1u)!=0u;
            bool valid=b.r==multipliers.x && all(greaterThanEqual(a.rgb,vec3(multipliers.y)))
                && all(lessThanEqual(a.rgb,vec3(multipliers.z)));
            return bvec2(!hit || valid,hit);
            """, parameters:new(publish?0:1,publish?0:.495f,publish?0:.505f,0));
        checkedDirections.AssertEqual(); uint checkedHits=checkedDirections.MatchingTexels;
        Assert.True(checkedHits > 0);
    }

    /// <summary>A nearer known opaque voxel cannot borrow emission from a screen hit when its own material is unavailable.</summary>
    [Fact]
    public void ScreenHits_MissingLocalMaterial_DoesNotBorrowScreenEmission()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 1);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 1);
        var checkedDirections = ObserveScreenTrace(result, screenOnly, """
            bool hit=(floatBitsToUint(c.g)&1u)!=0u;
            return bvec2(!hit || (b.r==0.0 && all(equal(a.rgb,vec3(0)))),hit);
            """);
        checkedDirections.AssertEqual(); uint checkedHits=checkedDirections.MatchingTexels;
        Assert.True(checkedHits > 0);
    }

    /// <summary>Projects a constant view-space plane using the same projection as the trace harness.</summary>
    private static float ScreenDepthAt(float z)
    {
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        return (projection[10] * z + projection[14]) / (projection[11] * z + projection[15]) * 0.5f + 0.5f;
    }
    #endregion
}
