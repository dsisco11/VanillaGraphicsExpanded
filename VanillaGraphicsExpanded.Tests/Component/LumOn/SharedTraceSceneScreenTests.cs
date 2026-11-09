using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.PBR.Materials;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises the actual screen-trace shader against production shared geometry publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SharedTraceSceneScreenTests : NearFieldShaderTestBase
{
    #region Public API
    /// <summary>Uses the exclusive material fixture's shared GPU context.</summary>
    public SharedTraceSceneScreenTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shared screen tracing
    /// <summary>Captured cache lighting resolves shared geometry hits consistently at signed large coordinates, including valid darkness.</summary>
    [Theory]
    [InlineData(0, 0f, 32)] [InlineData(-16777216, .25f, 32)] [InlineData(16777216, .25f, 32)]
    public void SealedRoomUsesSharedHitLighting(int anchor, float light, int surfaceSize)
    {
        // Arrange: Publish a sealed shared-geometry room with cache emission independent of legacy voxel light.
        EnsureShaderTestAvailable();
        using var material = new ScopedPbrMaterialFixture();
        // Cache emission is authored independently of the obsolete voxel RGB field below.
        material.SetReadiness(true, true, emissive: light / 32f);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, -5), true, surfaceSize, 256);
        using var shared = new SharedTraceGeometryFixture(plan, materials, (x, y, z) =>
        {
            bool wall = x == anchor - 3 || x == anchor + 3 || y == 29 || y == 35 || z == -8 || z == -2;
            return new(wall ? 2 | id << 2 : 1, 0, TraceGeometryVoxel.PackLight(new(light, light, light, 0)));
        });
        // Act: Publish the room and establish an unavailable compatibility result before cache admission.
        shared.Publish();
        using var cache = new SurfaceLightingScreenTraceFixture(shared.Scene,
            new(anchor - 3, 29, -8), new(anchor + 3, 35, -2));
        using var compatibility = new NearFieldVoxelFixture();
        var unavailable = Trace(compatibility, worldOffset: new(anchor, 32, 0), shared: shared.Scene);
        // Assert: A valid cache seeds successfully at each signed anchor, including legitimate darkness.
        Assert.True(cache.CaptureAndSeed());
        // Act: Trace again using the published surface-lighting snapshot.
        var result = Trace(compatibility, worldOffset: new(anchor, 32, 0), shared: shared.Scene, surfaceLighting: cache.Snapshot);
        // Assert: The shared cache result matches the expected lighting range and traversal count.
        ObserveTrace(result, LightingPredicates.SharedTraceSceneScreenTests_SealedRoomUsesSharedHitLighting, unavailable,new(light-.002f,light+.002f,light==0?3:2,0)).AssertEqual();
    }

    /// <summary>Unsupported, unpublished, exhausted and out-of-domain traces cannot become cache handoff.</summary>
    [Theory]
    [InlineData("unsupported")] [InlineData("missing")] [InlineData("budget")] [InlineData("outside")]
    public void IncompleteSharedTracesRemainUnavailable(string scenario)
    {
        // Arrange: Select an unsupported, unpublished, budget-limited, or out-of-domain trace input.
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(0, 32, -5), true, null, 256);
        using var shared = new SharedTraceGeometryFixture(plan, new(), (_, _, _) => new(scenario == "unsupported" ? 3u : 1u, 0, 0));
        // Act: Run the real shared tracer with only the selected failure precondition changed.
        if (scenario != "missing") shared.Publish();
        using var compatibility = new NearFieldVoxelFixture();
        var result = Trace(compatibility, budget: scenario == "budget" ? 1 : 256,
            worldOffset: new(0, scenario == "outside" ? 96 : 32, 0), shared: shared.Scene);
        // Assert: Incomplete traces remain unavailable to the cache handoff.
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTests_UnresolvedScene_RemainsDark).AssertEqual();
    }
    #endregion
    #endregion
    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-lighting", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
