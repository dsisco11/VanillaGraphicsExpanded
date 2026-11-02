using VanillaGraphicsExpanded.Tests.GPU.Assertions;
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
        ImageAssertions.Predicates.Predicate(onScreen.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, suppressed.Radiance).AssertEqual();
        ImageAssertions.Predicates.Predicate(onScreen.Meta, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4, suppressed.Meta).AssertEqual();
        var checkedDirections = ObserveScreenTrace(onScreen, screenOnly, SurfacePredicates.LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_AndOffscreenHitsRequireCachePublication, offScreen);
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
        var checkedDirections = ObserveScreenTrace(result, screenOnly, SurfacePredicates.LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_UnavailableLighting_RemainsUnresolved);
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
        ImageAssertions.Predicates.Predicate(result.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, suppressed.Radiance).AssertEqual();
        ImageAssertions.Predicates.Predicate(result.Meta, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4, suppressed.Meta).AssertEqual();
        var checkedDirections = ObserveScreenTrace(result, screenOnly, SurfacePredicates.LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_EmissionIsNotDoubled_AndSurvivesUnavailableLocalData, parameters:new(publish?0:1,publish?0:.495f,publish?0:.505f,0));
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
        var checkedDirections = ObserveScreenTrace(result, screenOnly, SurfacePredicates.LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_UnavailableLighting_RemainsUnresolved);
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
