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
    /// <summary>Default-resolution cache sweeps feed real probe workers while the camera crosses block boundaries.</summary>
    [Fact]
    public void MovingCamera_AllowsWorldProbeTracingAndPublication()
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, scene);
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
    [InlineData(false)] [InlineData(true)]
    public void RegisteredConsumersPropagateLightingAcrossLifetime(bool sh9)
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.IndirectFullTex!=null
            && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.FinalPixels())>.001f
            && runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        Assert.True(runtime.World.WorkerReads>0);
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Screen.ScreenProbeAtlasHistoryTex!.ReadPixels())>.001f);
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Screen.ScreenProbeAtlasFilteredTex!.ReadPixels())>.001f);
        Assert.True(runtime.DrawnPrograms.Count>0);
        Assert.Contains("lumon_probe_anchor",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_temporal",runtime.LoadedPrograms);
        Assert.Contains("lumon_probe_atlas_filter",runtime.LoadedPrograms);
        Assert.Contains(sh9?"lumon_probe_sh9_gather":"lumon_probe_atlas_gather",runtime.LoadedPrograms);
        Assert.Contains("lumon_upsample",runtime.LoadedPrograms);
        Assert.Contains("lumon_worldprobe_radiance_tile_resolve",runtime.LoadedPrograms);
        Assert.Contains(runtime.Screen.ProbeAnchorPositionTex!.ReadPixels().Where((_,i)=>i%4==3),v=>v>.5f);
        foreach(int light in new[]{0,32})
        {
            runtime.Cache.ChangeBlockLight(light);
            runtime.Frame();
            runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]>=.25f &&
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
    /// <summary>A pending real GPU query cannot publish under a budget smaller than one complete probe.</summary>
    [Fact]
    public void WholeProbeUploadBudgetDefersPendingQueries()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.WorldBuffers.RequestRecreate("verify atomic upload admission with a ready cache");
        runtime.Frame();
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        Assert.All(runtime.WorldPixels(),value=>Assert.Equal(0,value));
        Assert.Equal(0,runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]);
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=1575;
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            Assert.All(runtime.WorldPixels(),value=>Assert.Equal(0,value));
            Assert.Equal(0,runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]);
        }
        Assert.False(runtime.HasPendingSurfaceLightingQueries);
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=1576;
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
    }

    /// <summary>Dependency replacement discards an already submitted hit-query batch before its lighting can enter the atlas.</summary>
    [Fact]
    public void PendingGpuQueriesCannotRepopulateInvalidatedAtlases()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.ChangeBlockLight(0); runtime.Frame();
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]>=.25f);
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
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!=null && SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        runtime.World.HoldWorker();
        runtime.RunUntil(()=>runtime.World.WorkerWaiting);
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.ChangeBlockLight(0); runtime.Frame();
        Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        runtime.World.ReleaseWorker();
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out var after)&&after.DependencyRevision!=before.DependencyRevision
            && runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]>=.25f);
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
