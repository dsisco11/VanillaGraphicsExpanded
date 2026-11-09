using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises renderer ownership through the callbacks registered with the engine.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceCacheRuntimeTests : RenderTestBase
{


    #region Public API
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceCacheRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Runtime ownership
    /// <summary>Disposal removes every registered callback, so retired renderers cannot run in the next world.</summary>
    [Fact]
    public void DisposedRenderersRemoveTheirEngineCallbacks()
    {
        // Arrange: Construct the real render, geometry, feedback, and relight owners on a controlled engine event source.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name == "get_Event"
            ? events.Api : method.Invoke(assets.Api, args));
        var config = new VgeConfig();
        WorldPartitionModSystem? partitions = null;
        GBufferManager? buffers = null;
        TraceGeometryRenderer? geometry = null;
        LumonSceneFeedbackUpdateRenderer? feedback = null;
        LumonSceneRelightUpdateRenderer? relight = null;
        try
        {
            partitions = new WorldPartitionModSystem();
            buffers = new GBufferManager(api);
            geometry = new TraceGeometryRenderer(api, config, partitions, _ => new RuntimeTraceGeometrySource((_, _, _) => default));
            feedback = new LumonSceneFeedbackUpdateRenderer(api, config, buffers, partitions.GetCoordinator());
            relight = new LumonSceneRelightUpdateRenderer(api, config, feedback, geometry);
            Assert.Equal(3, events.Registrations.Count);
        }
        finally
        {
            // Act: Dispose every constructed owner in reverse order, including after constructor or assertion failure.
            try { relight?.Dispose(); }
            finally
            {
                try { feedback?.Dispose(); }
                finally
                {
                    try { geometry?.Dispose(); }
                    finally
                    {
                        try { buffers?.Dispose(); }
                        finally { partitions?.Dispose(); }
                    }
                }
            }
        }
        // Assert: Every registered callback is removed and disposal leaves OpenGL error-free.
        Assert.Empty(events.Registrations);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Registered production callbacks populate, display, recreate and repopulate surface lighting across world lifetime changes.</summary>
    [Fact]
    public void RegisteredCallbacksDriveSurfaceCacheAcrossRecreationAndWorldRestart()
    {
        // Arrange: Create the production runtime fixture and retain its first published irradiance atlas.
        EnsureContextValid();
        var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        try
        {
            // Act: Publish a settled frame through the registered callbacks.
            runtime.PrimeGeometry();
            runtime.RunUntil(runtime.SurfaceCacheSettled);
            // Assert: Registered callbacks execute in order and every world lifetime publishes fresh resources.
            Assert.Contains("vge_shared_trace_geometry", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_feedback", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_relight", runtime.Events.Executed);
            Assert.Contains("lumon_debug", runtime.Events.Executed);
            GpuTexture originalAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());

            // Act: Recreate the atlas and wait until the new resource generation settles.
            runtime.RequestAtlasRecreation();
            runtime.RunUntil(() => runtime.IrradianceAtlas() is { } atlas && !ReferenceEquals(atlas, originalAtlas) && runtime.SurfaceCacheSettled());
            // Assert: Recreation replaces and retires the original atlas.
            GpuTexture recreatedAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());
            Assert.NotSame(originalAtlas, recreatedAtlas);
            Assert.False(originalAtlas.IsValid);

            // Act: Retire the first world lifetime.
            runtime.LeaveWorld();
            // Assert: Leaving the world retires geometry, atlas identity, and all source owners.
            Assert.Null(runtime.Geometry.Resources);
            Assert.Equal(0, runtime.IrradianceAtlasId());
            Assert.All(runtime.Sources, source => Assert.True(source.Disposed));
            // Act: Restart with a separate source and atlas lifetime.
            runtime.PrimeGeometry();
            runtime.RunUntil(() => runtime.Geometry.Resources is not null && runtime.IrradianceAtlasId() != 0 && runtime.SurfaceCacheSettled());
            // Assert: Restart creates a distinct usable atlas lifetime and source set.
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
        // Arrange: Let the production callbacks publish their geometry through the real runtime owner.
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        // Act: Capture the published scene through both the production owner and established capture contract.
        for (int frame = 0; frame < 20; frame++) runtime.Frame();
        // Assert: Runtime publication is consumable by surface capture without GL errors.
        TraceGeometryGpuScene scene = Assert.IsType<TraceGeometryGpuScene>(runtime.Geometry.Resources);
        using var page = new SharedSurfacePageFixture();
        Assert.True(page.CaptureWithProductionOwner(runtime.Api, scene));
        Assert.True(page.Capture(scene));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Sampling changes invalidate publication immediately and reseed valid darkness within the configured page budget.</summary>
    [Fact]
    public void PublicationRejectsChangedSettingsAndResourcesBeforeReuse()
    {
        // Arrange: Publish lighting once and retain the first snapshot generation.
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        runtime.PrimeGeometry();
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var first));
        // Act: Change sampling policy and publish a new generation.
        runtime.Config.LumOn.LumonScene.RelightRaysPerTexel = 4;
        // Assert: Each change retires the previous publication before the new valid result is observable.
        Assert.False(runtime.TryGetLighting(out _));
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var second));
        Assert.True(second.Generation > first.Generation);
        Assert.NotEqual(first.DependencyRevision, second.DependencyRevision);
        // Act: Replace the atlas and wait for the new generation to publish.
        runtime.RequestAtlasRecreation();
        runtime.Frame();
        runtime.RunUntil(() => runtime.TryGetLighting(out var value) && !ReferenceEquals(value.OutgoingRadiance, second.OutgoingRadiance));
        Assert.False(second.OutgoingRadiance.IsValid);
        // Act: Retire the replacement generation by leaving the world.
        runtime.LeaveWorld();
        Assert.False(runtime.TryGetLighting(out _));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Partial publication exposes only initialized texels and carries them forward while another page starts.</summary>
    [Fact]
    public void PartialPagePublicationCarriesForwardUnchangedTiles()
    {
        // Arrange: Request two pages with a four-texel per-frame initialization budget.
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:2,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=4;
        runtime.PrimeGeometry();
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var first));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mappings,out _));
        uint[] ids=mappings.Keys.ToArray();
        Assert.Equal(2,ids.Length);
        int readCount=checked((int)ids.Max()+1);
        uint firstId;
        using (var ready=first.Readiness.MapRange<uint>(0,readCount,BufferMapAccess.Read))
        {
            Assert.True(ready.IsMapped);
            Assert.Equal(1u,ready.Span[(int)ids[0]]+ready.Span[(int)ids[1]]);
            firstId=ready.Span[(int)ids[0]]==1 ? ids[0] : ids[1];
        }
        using var before=CaptureTile(first,firstId);
        Assert.True(GpuLocalRead.Scalar(before.Texture,0)>0);
        var initialized=ImageAssertions.Predicates.Predicate(before.Texture, SurfacePredicates.SurfaceCacheRuntimeTests_PartialPagePublicationCarriesForwardUnchangedTiles);
        initialized.AssertEqual();Assert.Equal(4u,initialized.MatchingTexels);
        // Act: Advance one bounded frame so the second page begins while the first page remains published.
        runtime.Frame();
        // Assert: Both readiness entries become visible and the completed tile retains its exact prior image.
        Assert.True(runtime.TryGetLighting(out var second));
        Assert.True(second.Generation>first.Generation);
        Assert.Equal(first.DependencyRevision,second.DependencyRevision);
        using (var ready=second.Readiness.MapRange<uint>(0,readCount,BufferMapAccess.Read))
        { Assert.True(ready.IsMapped);Assert.Equal(1u,ready.Span[(int)ids[0]]);Assert.Equal(1u,ready.Span[(int)ids[1]]); }
        using var after=CaptureTile(second,firstId);
        ImageAssertions.Comparisons.CompareHistory(after.Texture,before).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
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
    }
    #endregion

    #region Private
    /// <summary>Retains exactly one complete published tile using the production physical-address layout.</summary>
    private static GpuImageSnapshot CaptureTile(in SurfaceLightingSnapshot snapshot,uint id)
    {
        var atlas=snapshot.OutgoingRadiance;
        int local=(int)((id-1)%(uint)snapshot.TilesPerAtlas),layer=(int)((id-1)/(uint)snapshot.TilesPerAtlas);
        int x=local%snapshot.TilesPerAxis*snapshot.TileSize,y=local/snapshot.TilesPerAxis*snapshot.TileSize;
        return new(atlas,new(new(0,layer),x,y,0,snapshot.TileSize,snapshot.TileSize,1));
    }
    #endregion
}
