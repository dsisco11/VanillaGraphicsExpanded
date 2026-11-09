using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Compares screen-resolved and voxel-resolved lighting for the same non-emissive room.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Screen Hit Lighting
    /// <summary>Screen and offscreen geometry agree on unavailable cache lighting while preserving opaque hits.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void ScreenHits_AndOffscreenHitsRequireCachePublication(float lighting)
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish the same sealed room for voxel traversal and a projected screen hit.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        float depth = ScreenDepthAt(-7); // Interior face of the back wall occupies z=-8..-7.

        // Act: Dispatch screen-only, offscreen, screen-enabled, and suppression traces over shared inputs.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var offScreen = Trace(fixture);
        var onScreen = Trace(fixture, screenDepth: depth);
        var suppressed = Trace(fixture, screenDepth: depth, suppress: true);

        // Assert: Their independent observations agree on unavailable lighting and preserve accepted opaque hits.
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
        // Arrange: Leave local regions unpublished while providing a screen depth for the visible wall.
        using var fixture = new NearFieldVoxelFixture();
        float depth = ScreenDepthAt(-7);

        // Act: Compare the screen-only geometry hit with near-field screen-hit tracing.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var result = Trace(fixture, screenDepth: depth);

        // Assert: A geometric hit remains observable without publishing confident darkness.
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
        // Arrange: Select whether local material data is published, retaining identical screen emission inputs.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        if (publish) fixture.Publish(world, new Vector4(1, 1, 1, 0.25f));
        float depth = ScreenDepthAt(-7);

        // Act: Compare screen-only, combined, and suppressed traces for this publication state.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 0.25f, emissionBoost: 2);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2);
        var suppressed = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2, suppress: true);

        // Assert: Screen emission is not doubled and an opaque hit does not borrow unavailable local lighting.
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
        // Arrange: Publish a known opaque room with unresolved local material while the screen supplies emission.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);
        float depth = ScreenDepthAt(-7);

        // Act: Run screen-only geometry and the full near-field trace against the same wall.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 1);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 1);

        // Assert: The nearer unresolved wall cannot inherit screen emission as local cache lighting.
        var checkedDirections = ObserveScreenTrace(result, screenOnly, SurfacePredicates.LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_UnavailableLighting_RemainsUnresolved);
        checkedDirections.AssertEqual(); uint checkedHits=checkedDirections.MatchingTexels;
        Assert.True(checkedHits > 0);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Projects a constant view-space plane using the same projection as the trace harness.</summary>
    private static float ScreenDepthAt(float z)
    {
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        return (projection[10] * z + projection[14]) / (projection[11] * z + projection[15]) * 0.5f + 0.5f;
    }
    #endregion
}
