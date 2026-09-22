using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests shared publication through production surface capture and relight bindings.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SharedTraceSceneSurfaceTests : RenderTestBase
{
    /// <summary>Uses the exclusive material fixture's GPU context.</summary>
    public SharedTraceSceneSurfaceTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Surface consumers
    /// <summary>Logical surface movement invalidates history even when physical residency stays unchanged.</summary>
    [Fact]
    public void LogicalDomainMovementInvalidatesHistoryWithoutSlotEviction()
    {
        EnsureContextValid();
        using var scene = new TraceGeometryGpuScene(48);
        var first = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        var next = TraceGeometryCoverage.Plan(new(1, 32, 0), true, 32, 256);
        Assert.Equal(first.Window, next.Window);
        scene.SetWindow(first);
        long revision = scene.Revision, history = scene.InvalidationRevision;
        scene.SetWindow(next);
        Assert.True(scene.Revision > revision);
        Assert.True(scene.InvalidationRevision > history);
        revision = scene.Revision; history = scene.InvalidationRevision;
        scene.SetWindow(next);
        Assert.Equal(revision, scene.Revision);
        Assert.Equal(history, scene.InvalidationRevision);
    }

    /// <summary>Preserves material capture, integer coordinates and the legacy valid-hit lighting formula.</summary>
    [Theory]
    [InlineData(0)] [InlineData(-16777216)] [InlineData(16777216)]
    [InlineData(-32)]
    public void ValidSharedSurfaceCapturesAndAccumulates(int anchor)
    {
        EnsureContextValid();
        using var material = new ScopedPbrMaterialFixture(); material.SetReadiness(true, true);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, 0), true, 32, 256);
        uint lighting = LumonSceneOccupancyPacking.PackClamped(32, 0, 0, (int)id);
        using var scene = new SharedTraceGeometryFixture(plan, materials, (_, _, _) => new(2 | id << 2, lighting, 0));
        scene.Publish();
        using var page = new SharedSurfacePageFixture(anchor);
        Assert.True(page.Capture(scene.Scene));
        byte[] captured = page.ReadMaterial();
        for (int i = 2; i < captured.Length; i += 4) Assert.NotEqual(0, captured[i]);
        Assert.True(page.Relight(scene.Scene)); Assert.True(page.Relight(scene.Scene));
        float[] result = page.ReadLighting();
        for (int i = 0; i < result.Length; i += 4)
        {
            Assert.InRange(result[i], 31.9f, 32.1f);
            Assert.Equal(2f, result[i + 3]);
        }
        page.ResetLighting();
        Assert.All(page.ReadLighting(), value => Assert.Equal(0f, value));
    }

    /// <summary>Dirty, missing, unsupported, empty and exhausted traces cannot create sky or sample weight.</summary>
    [Theory]
    [InlineData("missing")] [InlineData("dirty")] [InlineData("unsupported")]
    [InlineData("exit")] [InlineData("budget")] [InlineData("material")]
    public void UnavailableSharedSurfaceRemainsRetryable(string scenario)
    {
        EnsureContextValid();
        using var material = new ScopedPbrMaterialFixture(); material.SetReadiness(scenario != "material", true);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        uint kind = scenario == "unsupported" ? 3u : scenario is "exit" or "budget" ? 1u : 2u;
        using var scene = new SharedTraceGeometryFixture(plan, materials, (_, _, _) => new(kind | id << 2, 0, 0));
        if (scenario != "missing") scene.Publish();
        if (scenario == "dirty") scene.Dirty();
        using var page = new SharedSurfacePageFixture();
        bool captured = page.Capture(scene.Scene);
        Assert.Equal(scenario is "exit" or "budget" or "unsupported", captured);
        Assert.False(page.Relight(scene.Scene, scenario == "budget" ? 1u : 256u));
        Assert.All(page.ReadLighting(), value => Assert.Equal(0f, value));
    }
    #endregion
}
