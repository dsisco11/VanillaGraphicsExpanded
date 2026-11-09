using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Exercises registered production consumers and cache lifetime across asynchronous trace work.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceLightingConsumerRuntimeTests : RenderTestBase
{

    #region Public API
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    public SurfaceLightingConsumerRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Runtime transport
    /// <summary>Unresolved finite ray segments cannot publish sky or lighting even when the cache itself is ready.</summary>
    [Fact]
    public void InsufficientTraceRange_DoesNotPublishFalseSky()
    {
        // Arrange a ready cache with rays that cannot reach a surface.
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false, shortProbeRange: true);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        bool ready = false;
        // Act one production frame at a time while the cache cannot answer the finite ray.
        for (int frame = 0; frame < 96; frame++)
        {
            runtime.Frame();
            ready |= runtime.Cache.TryGetLighting(out _);
            // Assert unresolved samples never become sky radiance or world-probe confidence.
            Assert.InRange(runtime.Energy(runtime.WorldImage), 0, .0001f);
            ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
            Assert.Equal(0, runtime.WorldConfidence);
            Thread.Yield();
        }
        Assert.True(ready);
        Assert.True(runtime.World.WorkerReads > 0);
    }

    /// <summary>Unavailable surface lighting keeps geometry workers active, retries unresolved hits, and resumes publication when ready.</summary>
    [Fact]
    public void MissingSurfaceLighting_ContinuesTracingAndRetriesUntilReady()
    {
        // Arrange valid geometry work while withholding only the lighting provider.
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.WorldRenderer.SetSurfaceLightingProvider(null, null);
        runtime.Cache.Config.LumOn.DebugMode = VanillaGraphicsExpanded.LumOn.LumOnDebugMode.WorldProbeOrbsPoints;
        int firstReads = 0;
        bool exposedQueuedRays = false;
        // Act through repeated frames and observe that CPU tracing continues while lighting is unavailable.
        for (int frame = 0; frame < 96; frame++)
        {
            runtime.Frame();
            exposedQueuedRays |= runtime.WorldBuffers.TryGetDebugTraceRays(out _, out int count, out _) && count > 0;
            if (frame == 47) firstReads = runtime.World.WorkerReads;
            Assert.InRange(runtime.Energy(runtime.WorldImage), 0, .0001f);
            Assert.False(runtime.HasPendingSurfaceLightingQueries);
            Thread.Yield();
        }
        // Assert the unavailable interval kept tracing and retrying, then restore publication and require recovery.
        Assert.True(firstReads > 0, "Unavailable surface lighting prevented CPU geometry tracing.");
        Assert.True(exposedQueuedRays, "The queued-ray debug path never received trace requests.");
        Assert.True(runtime.World.WorkerReads > firstReads, "Unresolved surface results stopped being retried.");
        runtime.WorldRenderer.SetSurfaceLightingProvider(runtime.Cache.LightingProvider, runtime.Cache.Geometry);
        runtime.RunUntil(() => runtime.Energy(runtime.WorldImage) > .001f);
    }

    /// <summary>Default-resolution cache sweeps feed real probe workers while the camera crosses block boundaries.</summary>
    [Fact]
    public void MovingCamera_AllowsWorldProbeTracingAndPublication()
    {
        // Arrange a real worker-backed world-probe path and move the camera across block boundaries.
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false, scene);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.Cache.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = 4;
        bool published = false;
        // Act until a worker read and corresponding world radiance are both observed.
        for (int frame = 0; frame < 160 && !published; frame++)
        {
            scene.Position = new(frame % 2, 36, 5);
            runtime.Frame();
            published = runtime.World.WorkerReads > 0 &&
                runtime.Energy(runtime.WorldImage) > .001f;
            Thread.Yield();
        }
        // Assert the worker read and world radiance were both observed.
        Assert.True(published, $"Moving camera failed to produce world-probe radiance; worker reads={runtime.World.WorkerReads}, cache={runtime.Cache.TryGetLighting(out _)}.");
    }

    /// <summary>Registered callbacks generate anchors and propagate real produced lighting into both probe paths and full-resolution output.</summary>
    [Theory]
    [InlineData(false,false)] [InlineData(true,true)]
    public void RegisteredConsumersPropagateLightingAcrossLifetime(bool sh9,bool gpu)
    {
        // Arrange the registered runtime with the selected CPU or GPU trace backend.
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=gpu;
        // Act through normal registered callbacks until screen and world consumers receive produced lighting.
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.HasPublishedIndirect
            && runtime.Energy(runtime.FinalImage)>.001f
            && runtime.WorldBuffers.Resources!=null && runtime.Energy(runtime.WorldImage)>.001f);
        // Assert backend-specific work and the published screen, world, and renderer-program outputs.
        if(gpu)Assert.Equal(0,runtime.World.WorkerReads);else Assert.True(runtime.World.WorkerReads>0);
        if(!gpu)Assert.Equal(0,runtime.World.VanillaLightReads);
        Assert.True(runtime.Energy(runtime.Screen.ScreenProbeAtlasHistoryTex!)>.001f);
        Assert.True(runtime.Energy(runtime.Screen.ScreenProbeAtlasFilteredTex!)>.001f);
        Assert.True(runtime.DrawnPrograms.Count>0);
        Assert.Contains("lumon_probe_anchor",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_temporal",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_filter",runtime.LoadedPrograms);
        Assert.Contains(sh9?"lumon_probe_sh9_gather":"lumon_probe_atlas_gather",runtime.LoadedPrograms);
        Assert.Contains("lumon_upsample",runtime.LoadedPrograms);
        // Resident GPU commits do not load the CPU-only raster upload program.
        if(!gpu)Assert.Contains("lumon_worldprobe_radiance_tile_resolve",runtime.LoadedPrograms);
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.Screen.ProbeAnchors!, selection: new(new(0, 0), 0, 0, 0, runtime.Screen.ProbeAnchors!.Width, runtime.Screen.ProbeAnchors.Height), channelMask: 8).Maximum > .5f);
        // Act across source removal/restoration with explicit atlas recreation, checking that dark and lit data recover.
        foreach(int light in new[]{0,32})
        {
            runtime.Cache.ChangeBlockLight(light);
            // Exercise a hard storage lifetime; automatic stale-light refresh is a separate scheduling contract.
            runtime.Cache.RequestAtlasRecreation();
            runtime.Frame();
            runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.HasPublishedIndirect && runtime.WorldConfidence>=.25f &&
                (light!=0 ? runtime.Energy(runtime.FinalImage)>.001f : runtime.Energy(runtime.FinalImage)<=.0001f) &&
                (light!=0 ? runtime.Energy(runtime.WorldImage)>.001f : runtime.Energy(runtime.WorldImage)<=.0001f));
        }
        // Act through a coordinated cache, world-buffer, and screen-history recreation.
        var oldAtlas=runtime.Cache.IrradianceAtlas();
        var oldWorld=runtime.WorldBuffers.Resources;
        long oldHistory=runtime.Screen.HistoryRevision;
        runtime.Cache.RequestAtlasRecreation();
        runtime.WorldBuffers.RequestRecreate("controlled lifetime transition");
        runtime.Screen.RequestRecreateBuffers("controlled lifetime transition");
        runtime.Frame();
        // Reallocation deliberately skips rendering; allocated storage is not produced lighting.
        Assert.False(runtime.Screen.HasPublishedIndirect);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.HasPublishedIndirect && !ReferenceEquals(oldAtlas,runtime.Cache.IrradianceAtlas())
            && runtime.Energy(runtime.FinalImage)>.001f
            && runtime.Energy(runtime.WorldImage)>.001f);
        // Assert replacement storage is repopulated, then leaving and re-entering the world removes stale lighting.
        Assert.NotSame(oldWorld,runtime.WorldBuffers.Resources);
        Assert.True(runtime.Screen.HistoryRevision>oldHistory);
        runtime.Cache.LeaveWorld();
        Assert.InRange(runtime.Energy(runtime.Screen.ScreenProbeAtlasHistoryTex!),0,.0001f);
        Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
        Assert.InRange(runtime.Energy(runtime.FinalImage),0,.0001f);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.HasPublishedIndirect && runtime.Energy(runtime.FinalImage)>.001f
            && runtime.Energy(runtime.WorldImage)>.001f);
        var done=runtime.Cache.Events.Registrations.Where(r=>r.Stage==EnumRenderStage.Done).OrderBy(r=>r.Renderer.RenderOrder).Select(r=>r.Name).ToArray();
        Assert.True(Array.IndexOf(done,"vge_lumonscene_feedback")<Array.IndexOf(done,"vge_lumonscene_relight"));
        Assert.True(Array.IndexOf(done,"vge_lumonscene_relight")<Array.IndexOf(done,"vge_worldprobe_update"));
        Assert.Equal("vge_worldprobe_update",done[^1]);
        var opaque=runtime.Cache.Events.Registrations.Where(r=>r.Stage==EnumRenderStage.Opaque).OrderBy(r=>r.Renderer.RenderOrder).Select(r=>r.Name).ToArray();
        Assert.Equal("lumon",opaque[^1]);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Neither trace backend publishes below the metadata plus one-direction upload cost.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MinimumDirectionalUploadBudgetDefersPendingQueries(bool gpu)
    {
        // Arrange one ready atlas publication, then reduce the upload budget below one directional update.
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=gpu;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && runtime.Energy(runtime.WorldImage)>.001f);
        runtime.WorldBuffers.RequestRecreate("verify atomic upload admission with a ready cache");
        runtime.Frame();
        if(!gpu)runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        // Act under the insufficient budget and assert each frame remains unpublished.
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=63;
        // Assert the insufficient budget leaves the world atlas empty and confidence unset.
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
        Assert.Equal(0,runtime.WorldConfidence);
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
            Assert.Equal(0,runtime.WorldConfidence);
        }
        Assert.False(runtime.HasPendingSurfaceLightingQueries);
        // Restore the backend's minimum admissible budget and verify deferred radiance can publish.
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=gpu?2096:1576;
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f);
    }

    /// <summary>Dependency replacement discards an already submitted hit-query batch before its lighting can enter the atlas.</summary>
    [Fact]
    public void PendingGpuQueriesCannotRepopulateInvalidatedAtlases()
    {
        // Arrange a pending CPU query against an illuminated atlas and capture its dependency revision.
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && runtime.Energy(runtime.WorldImage)>.001f);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        // Act by invalidating the dependency before the submitted query is consumed.
        runtime.Cache.ChangeBlockLight(0); runtime.Cache.RequestAtlasRecreation(); runtime.Frame();
        Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldConfidence>=.25f);
        // Assert the replacement remains dark across subsequent frames; stale query results cannot repopulate it.
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
            Assert.InRange(runtime.Energy(runtime.FinalImage),0,.0001f);
        }
    }

    /// <summary>Late CPU work from a retired dependency cannot publish, while replacement workers recover valid dark data.</summary>
    [Fact]
    public void DelayedWorkerCompletionCannotRestoreObsoleteLighting()
    {
        // Arrange an illuminated CPU worker and hold its completion after it starts tracing.
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, true);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && runtime.Energy(runtime.WorldImage)>.001f);
        runtime.World.HoldWorker();
        runtime.RunUntil(()=>runtime.World.WorkerWaiting);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        // Act by replacing the dependency while the old worker is blocked, then release its stale result.
        runtime.Cache.ChangeBlockLight(0); runtime.Cache.RequestAtlasRecreation(); runtime.Frame();
        Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
        runtime.World.ReleaseWorker();
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldConfidence>=.25f);
        // Assert stale work never restores light after the replacement worker publishes valid dark data.
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
            Assert.InRange(runtime.Energy(runtime.FinalImage),0,.0001f);
        }
    }

    /// <summary>Disposal removes all consumer callbacks and subscriptions before their dependencies disappear.</summary>
    [Fact]
    public void ConsumerDisposalUnregistersCallbacks()
    {
        // Arrange registered callbacks and subscriptions owned by one runtime.
        EnsureContextValid();
        var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        var events=runtime.Cache.Events;
        bool disposalAttempted=false;
        try
        {
            Assert.Contains(events.Registrations,r=>r.Name=="lumon");
            Assert.Contains(events.Registrations,r=>r.Name=="vge_worldprobe_update");
            // Act by disposing the runtime before its dependencies are destroyed.
            disposalAttempted=true;
            runtime.Dispose();
            // Assert every owned registration and subscription was removed cleanly.
            Assert.Empty(events.Registrations);
            Assert.Equal(0,events.SubscriptionCount("LeaveWorld"));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally
        {
            if (!disposalAttempted)
            {
                runtime.Dispose();
            }
        }
    }
    #endregion
    #endregion
    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
