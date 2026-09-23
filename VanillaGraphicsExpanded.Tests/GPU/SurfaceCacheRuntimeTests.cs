using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises renderer ownership through the callbacks registered with the engine.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheRuntimeTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceCacheRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Runtime ownership
    /// <summary>Disposal removes every registered callback, so retired renderers cannot run in the next world.</summary>
    [Fact]
    public void DisposedRenderersRemoveTheirEngineCallbacks()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name == "get_Event"
            ? events.Api : method.Invoke(assets.Api, args));
        var config = new VgeConfig();
        var partitions = new WorldPartitionModSystem();
        using var buffers = new GBufferManager(api);
        using var geometry = new TraceGeometryRenderer(api, config, partitions, _ => new RuntimeTraceGeometrySource((_, _, _) => default));
        var feedback = new LumonSceneFeedbackUpdateRenderer(api, config, buffers, partitions.GetCoordinator());
        var relight = new LumonSceneRelightUpdateRenderer(api, config, feedback, geometry);
        Assert.Equal(3, events.Registrations.Count);
        relight.Dispose(); feedback.Dispose(); geometry.Dispose();
        partitions.Dispose();
        Assert.Empty(events.Registrations);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Registered production callbacks populate, display, recreate and repopulate surface lighting across world lifetime changes.</summary>
    [Fact]
    public void RegisteredCallbacksDriveSurfaceCacheAcrossRecreationAndWorldRestart()
    {
        EnsureContextValid();
        var runtime = new SurfaceCacheRuntimeFixture();
        try
        {
            runtime.PrimeGeometry();
            runtime.RunUntil(runtime.SurfaceCacheSettled);
            Assert.Contains("vge_shared_trace_geometry", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_feedback", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_relight", runtime.Events.Executed);
            Assert.Contains("lumon_debug", runtime.Events.Executed);
            GpuTexture originalAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());

            runtime.RequestAtlasRecreation();
            runtime.RunUntil(() => runtime.IrradianceAtlas() is { } atlas && !ReferenceEquals(atlas, originalAtlas) && runtime.SurfaceCacheSettled());
            GpuTexture recreatedAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());
            Assert.NotSame(originalAtlas, recreatedAtlas);
            Assert.False(originalAtlas.IsValid);

            runtime.LeaveWorld();
            Assert.Null(runtime.Geometry.Resources);
            Assert.Equal(0, runtime.IrradianceAtlasId());
            Assert.All(runtime.Sources, source => Assert.True(source.Disposed));
            runtime.PrimeGeometry();
            runtime.RunUntil(() => runtime.Geometry.Resources is not null && runtime.IrradianceAtlasId() != 0 && runtime.SurfaceCacheSettled());
            Assert.True(recreatedAtlas.IsValid);
            Assert.True(runtime.Sources.Count >= 2);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            runtime.Dispose();
        }
        Assert.Empty(runtime.Events.Registrations);
        Assert.Equal(0, runtime.Events.SubscriptionCount("LeaveWorld"));
    }

    /// <summary>Confirms geometry published by the runtime renderer satisfies the established capture contract.</summary>
    [Fact]
    public void RuntimePublishedGeometrySupportsSurfaceCapture()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        for (int frame = 0; frame < 20; frame++) runtime.Frame();
        TraceGeometryGpuScene scene = Assert.IsType<TraceGeometryGpuScene>(runtime.Geometry.Resources);
        using var page = new SharedSurfacePageFixture();
        Assert.True(page.CaptureWithProductionOwner(runtime.Api, scene));
        Assert.True(page.Capture(scene));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
