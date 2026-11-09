using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.PBR.Materials;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Tests shared publication through production surface capture and relight bindings.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SharedTraceSceneSurfaceTests : RenderTestBase
{


    #region Public API
    /// <summary>Uses the exclusive material fixture's GPU context.</summary>
    public SharedTraceSceneSurfaceTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Surface consumers
    /// <summary>Logical surface movement preserves history when physical residency stays unchanged.</summary>
    [Fact]
    public void LogicalDomainMovementPreservesHistoryWithoutSlotEviction()
    {
        // Arrange: Choose adjacent logical windows that map to the same physical ring residency.
        EnsureContextValid();
        using var scene = new TraceGeometryGpuScene(48);
        var first = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        var next = TraceGeometryCoverage.Plan(new(1, 32, 0), true, 32, 256);
        Assert.Equal(first.Window, next.Window);
        // Act: Move between the overlapping windows and repeat the same publication.
        scene.SetWindow(first);
        long revision = scene.Revision, history = scene.InvalidationRevision;
        scene.SetWindow(next);
        // Assert: Logical revision advances once, while physical invalidation remains unchanged.
        Assert.True(scene.Revision > revision);
        Assert.Equal(history, scene.InvalidationRevision);
        // Act: Apply the identical window again to check that an unchanged publication is idempotent.
        revision = scene.Revision; history = scene.InvalidationRevision;
        scene.SetWindow(next);
        // Assert: Repeating the current window changes neither revision.
        Assert.Equal(revision, scene.Revision);
        Assert.Equal(history, scene.InvalidationRevision);
    }

    /// <summary>Preserves material capture, integer coordinates and cosine-weighted irradiance normalization.</summary>
    [Theory]
    [InlineData(0)] [InlineData(-16777216)] [InlineData(16777216)]
    public void ValidSharedSurfaceCapturesAndAccumulates(int anchor)
    {
        // Arrange: Publish a resolved material and uniform packed lighting at a signed anchor.
        EnsureContextValid();
        using var material = new ScopedPbrMaterialFixture(); material.SetReadiness(true, true);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, 0), true, 32, 256);
        uint lighting = LumonSceneOccupancyPacking.PackClamped(32, 0, 0, (int)id);
        using var scene = new SharedTraceGeometryFixture(plan, materials, (_, _, _) => new(2 | id << 2, lighting, 0));
        scene.Publish();
        using var page = new SharedSurfacePageFixture(anchor);
        // Act: Capture the surface against the published material and geometry.
        Assert.True(page.Capture(scene.Scene));
        // Assert: Capture resolves the expected material identity.
        ImageAssertions.Predicates.Predicate(page.MaterialAtlas, LightingPredicates.SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates).AssertEqual();
        // Act: Accumulate two independent relight batches against the same captured surface.
        Assert.True(page.Relight(scene.Scene)); Assert.True(page.Relight(scene.Scene));
        // Assert: The two samples produce the cosine-weighted reference irradiance.
        float expected=32*GgxHemisphereReference.CapturedDiffuse(1);
        ImageAssertions.Predicates.Predicate(page.IrradianceAtlas, LightingPredicates.SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates_2,
            parameters:new(expected-.1f,expected+.1f,0,0)).AssertEqual();
        // Act: Reset the accumulated lighting history.
        page.ResetLighting();
        // Assert: Reset clears the accumulated sample as well as its value.
        ImageAssertions.Comparisons.CompareConstant(page.IrradianceAtlas,0).AssertEqual();
    }

    /// <summary>Dirty, missing, unsupported, empty and exhausted traces cannot create sky or sample weight.</summary>
    [Theory]
    [InlineData("missing")] [InlineData("dirty")] [InlineData("unsupported")]
    [InlineData("exit")] [InlineData("budget")] [InlineData("material")]
    public void UnavailableSharedSurfaceRemainsRetryable(string scenario)
    {
        // Arrange: Select one unavailable source, revision, geometry, budget, or material condition.
        EnsureContextValid();
        using var material = new ScopedPbrMaterialFixture(); material.SetReadiness(scenario != "material", true);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        uint kind = scenario == "unsupported" ? 3u : scenario is "exit" or "budget" ? 1u : 2u;
        using var scene = new SharedTraceGeometryFixture(plan, materials, (_, _, _) => new(kind | id << 2, 0, 0));
        if (scenario != "missing") scene.Publish();
        if (scenario == "dirty") scene.Dirty();
        using var page = new SharedSurfacePageFixture();
        // Act: Attempt capture and relighting against the selected shared-publication state.
        bool captured = page.Capture(scene.Scene);
        // Assert: Only confirmed hit-like geometry may capture, and unresolved lighting adds no sample weight.
        Assert.Equal(scenario is "exit" or "budget" or "unsupported", captured);
        Assert.False(page.Relight(scene.Scene, scenario == "budget" ? 1u : 256u));
        ImageAssertions.Comparisons.CompareConstant(page.IrradianceAtlas,0).AssertEqual();
    }
    #endregion

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-lighting", 0, 0);
    }
    #endregion
}
