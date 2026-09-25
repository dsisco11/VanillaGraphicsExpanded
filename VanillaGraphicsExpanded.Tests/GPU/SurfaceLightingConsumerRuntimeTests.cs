using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises registered production consumers, asynchronous trace work and coherent cache lifetime end to end.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingConsumerRuntimeTests : RenderTestBase
{
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    public SurfaceLightingConsumerRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Runtime transport
    /// <summary>Unresolved finite ray segments cannot publish sky or lighting even when the cache itself is ready.</summary>
    [Fact]
    public void InsufficientTraceRange_DoesNotPublishFalseSky()
    {
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, shortProbeRange: true);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        bool ready = false;
        for (int frame = 0; frame < 96; frame++)
        {
            runtime.Frame();
            ready |= runtime.Cache.TryGetLighting(out _);
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()), 0, .0001f);
            Assert.All(runtime.WorldPixels(), value => Assert.Equal(0, value));
            Assert.Equal(0, runtime.WorldConfidence);
            Thread.Yield();
        }
        Assert.True(ready);
        Assert.True(runtime.World.WorkerReads > 0);
    }

    /// <summary>The registered renderer publishes cached radiance without requesting vanilla hit lighting.</summary>
    [Fact]
    public void WorldProbePublication_DoesNotReadVanillaHitLighting()
    {
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(() => SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()) > .001f);
        Assert.True(runtime.World.WorkerReads > 0);
        Assert.Equal(0, runtime.World.VanillaLightReads);
    }

    /// <summary>Unavailable surface lighting keeps geometry workers active, retries unresolved hits, and resumes publication when ready.</summary>
    [Fact]
    public void MissingSurfaceLighting_ContinuesTracingAndRetriesUntilReady()
    {
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.WorldRenderer.SetSurfaceLightingProvider(null, null);
        runtime.Cache.Config.LumOn.DebugMode = VanillaGraphicsExpanded.LumOn.LumOnDebugMode.WorldProbeOrbsPoints;
        int firstReads = 0;
        bool exposedQueuedRays = false;
        for (int frame = 0; frame < 96; frame++)
        {
            runtime.Frame();
            exposedQueuedRays |= runtime.WorldBuffers.TryGetDebugTraceRays(out _, out int count, out _) && count > 0;
            if (frame == 47) firstReads = runtime.World.WorkerReads;
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()), 0, .0001f);
            Assert.False(runtime.HasPendingSurfaceLightingQueries);
            Thread.Yield();
        }
        Assert.True(firstReads > 0, "Unavailable surface lighting prevented CPU geometry tracing.");
        Assert.True(exposedQueuedRays, "The queued-ray debug path never received trace requests.");
        Assert.True(runtime.World.WorkerReads > firstReads, "Unresolved surface results stopped being retried.");
        runtime.WorldRenderer.SetSurfaceLightingProvider(runtime.Cache.LightingProvider, runtime.Cache.Geometry);
        runtime.RunUntil(() => SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()) > .001f);
    }

    /// <summary>Default-resolution cache sweeps feed real probe workers while the camera crosses block boundaries.</summary>
    [Fact]
    public void MovingCamera_AllowsWorldProbeTracingAndPublication()
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, scene);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.Cache.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = 4;
        bool published = false;
        for (int frame = 0; frame < 160 && !published; frame++)
        {
            scene.Position = new(frame % 2, 36, 5);
            runtime.Frame();
            published = runtime.World.WorkerReads > 0 &&
                SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()) > .001f;
            Thread.Yield();
        }
        Assert.True(published, $"Moving camera failed to produce world-probe radiance; worker reads={runtime.World.WorkerReads}, cache={runtime.Cache.TryGetLighting(out _)}.");
    }

    /// <summary>Registered callbacks generate anchors and propagate real produced lighting into both probe paths and full-resolution output.</summary>
    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void RegisteredConsumersPropagateLightingAcrossLifetime(bool sh9,bool gpu)
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=gpu;
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.IndirectFullTex!=null
            && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())>.001f
            && runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        if(gpu)Assert.Equal(0,runtime.World.WorkerReads);else Assert.True(runtime.World.WorkerReads>0);
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Screen.ScreenProbeAtlasHistoryTex!.ReadPixels())>.001f);
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Screen.ScreenProbeAtlasFilteredTex!.ReadPixels())>.001f);
        Assert.True(runtime.DrawnPrograms.Count>0);
        Assert.Contains("lumon_probe_anchor",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_temporal",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_filter",runtime.LoadedPrograms);
        Assert.Contains(sh9?"lumon_probe_sh9_gather":"lumon_probe_atlas_gather",runtime.LoadedPrograms);
        Assert.Contains("lumon_upsample",runtime.LoadedPrograms);
        // Resident GPU commits do not load the CPU-only raster upload program.
        if(!gpu)Assert.Contains("lumon_worldprobe_radiance_tile_resolve",runtime.LoadedPrograms);
        Assert.Contains(runtime.Screen.ProbeAnchorPositionTex!.ReadPixels().Where((_,i)=>i%4==3),v=>v>.5f);
        foreach(int light in new[]{0,32})
        {
            runtime.Cache.ChangeBlockLight(light);
            // Exercise a hard storage lifetime; automatic stale-light refresh is a separate scheduling contract.
            runtime.Cache.RequestAtlasRecreation();
            runtime.Frame();
            runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.WorldConfidence>=.25f &&
                (light!=0 ? SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())>.001f : SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())<=.0001f) &&
                (light!=0 ? SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f : SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())<=.0001f));
        }
        var oldAtlas=runtime.Cache.IrradianceAtlas();
        var oldWorld=runtime.WorldBuffers.Resources;
        long oldHistory=runtime.Screen.HistoryRevision;
        runtime.Cache.RequestAtlasRecreation();
        runtime.WorldBuffers.RequestRecreate("controlled lifetime transition");
        runtime.Screen.RequestRecreateBuffers("controlled lifetime transition");
        runtime.Frame();
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && !ReferenceEquals(oldAtlas,runtime.Cache.IrradianceAtlas())
            && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())>.001f
            && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        Assert.NotSame(oldWorld,runtime.WorldBuffers.Resources);
        Assert.True(runtime.Screen.HistoryRevision>oldHistory);
        runtime.Cache.LeaveWorld();
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Screen.ScreenProbeAtlasHistoryTex!.ReadPixels()),0,.0001f);
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels()),0,.0001f);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())>.001f
            && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
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
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=gpu;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.WorldBuffers.RequestRecreate("verify atomic upload admission with a ready cache");
        runtime.Frame();
        if(!gpu)runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=63;
        Assert.All(runtime.WorldPixels(),value=>Assert.Equal(0,value));
        Assert.Equal(0,runtime.WorldConfidence);
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.All(runtime.WorldPixels(),value=>Assert.Equal(0,value));
            Assert.Equal(0,runtime.WorldConfidence);
        }
        Assert.False(runtime.HasPendingSurfaceLightingQueries);
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=gpu?2096:1576;
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
    }

    /// <summary>Dependency replacement discards an already submitted hit-query batch before its lighting can enter the atlas.</summary>
    [Fact]
    public void PendingGpuQueriesCannotRepopulateInvalidatedAtlases()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.ChangeBlockLight(0); runtime.Cache.RequestAtlasRecreation(); runtime.Frame();
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldConfidence>=.25f);
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels()),0,.0001f);
        }
    }

    /// <summary>Late CPU work from a retired dependency cannot publish, while replacement workers recover valid dark data.</summary>
    [Fact]
    public void DelayedWorkerCompletionCannotRestoreObsoleteLighting()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(true);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.World.HoldWorker();
        runtime.RunUntil(()=>runtime.World.WorkerWaiting);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.ChangeBlockLight(0); runtime.Cache.RequestAtlasRecreation(); runtime.Frame();
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        runtime.World.ReleaseWorker();
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldConfidence>=.25f);
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels()),0,.0001f);
        }
    }

    /// <summary>Disposal removes all consumer callbacks and subscriptions before their dependencies disappear.</summary>
    [Fact]
    public void ConsumerDisposalUnregistersCallbacks()
    {
        EnsureContextValid();
        var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        var events=runtime.Cache.Events;
        Assert.Contains(events.Registrations,r=>r.Name=="lumon");
        Assert.Contains(events.Registrations,r=>r.Name=="vge_worldprobe_update");
        runtime.Dispose();
        Assert.Empty(events.Registrations);
        Assert.Equal(0,events.SubscriptionCount("LeaveWorld"));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
