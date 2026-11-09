using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.LumOn;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises production near-field geometry, lighting and cache handoff through actual screen-probe rays.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed partial class LumOnNearFieldFunctionalTests : NearFieldShaderTestBase, IClassFixture<NearFieldNumericalProgramFixture>
{
    #region Public API
    /// <summary>Shares the headless GPU context.</summary>
    public LumOnNearFieldFunctionalTests(HeadlessGLFixture fixture, NearFieldNumericalProgramFixture numericalPrograms)
        : base(fixture, numericalPrograms) { }

    #region Scenarios
    /// <summary>A sealed room retains opaque hits and rejects raw voxel light when surface pages are unavailable.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    public void SealedRoom_RequiresPublishedSurfaceLighting(float lighting)
    {
        EnsureShaderTestAvailable();
        // Arrange: Build a sealed voxel room with no published surface-lighting page at the selected raw-light level.
        var world = new ControlledVoxelWorld { DefaultLight = Vector4.One };
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);

        // Act: Trace with and without distant-light suppression through the production shader.
        var result = Trace(fixture);
        var suppressed = Trace(fixture, suppress: true);

        // Assert: The paired GPU observations retain opaque hits and reject raw voxel light.
        ImageAssertions.Predicates.Predicate(result.Meta, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4, suppressed.Meta).AssertEqual();
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTests_SealedRoom_RequiresPublishedSurfaceLighting, suppressed).AssertEqual();
    }

    /// <summary>Unpublished data, unsupported cells and exhausted traversal never authorize cache or sky.</summary>
    [Theory]
    [InlineData(false, 256, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 256, true)]
    public void UnresolvedScene_RemainsDark(bool publish, int budget, bool unsupported)
    {
        EnsureShaderTestAvailable();
        // Arrange: Select unpublished, unsupported, or budget-exhausted local traversal for this case.
        using var fixture = new NearFieldVoxelFixture();
        if (publish) fixture.Publish(new ControlledVoxelWorld { IsLoaded = _ => !unsupported });

        // Act and assert: Trace the configured scene and budget; no unresolved case may claim cache or sky light.
        var result = Trace(fixture, budget: budget);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTests_UnresolvedScene_RemainsDark).AssertEqual();
    }

    /// <summary>A fully known clear segment admits distant cache lighting and suppression affects only that lighting.</summary>
    [Fact]
    public void ClearScene_UsesDistantCache()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a fully known clear local segment with a valid distant cache sample.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act: Compare normal and suppressed production traces.
        var result = Trace(fixture);
        var suppressed = Trace(fixture, suppress: true);

        // Assert: Only the admitted distant-cache contribution changes under suppression.
        ImageAssertions.Predicates.Predicate(result.Meta, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4, suppressed.Meta).AssertEqual();
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTests_ClearScene_UsesDistantCache, suppressed).AssertEqual();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 2, 0);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-cloud", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
