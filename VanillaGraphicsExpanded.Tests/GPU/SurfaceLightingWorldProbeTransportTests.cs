using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests CPU hits, asynchronous cache queries and real GPU atlas publication across source changes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingWorldProbeTransportTests : SurfaceLightingHitTestBase
{
    /// <summary>Uses the shared material-isolated GPU context.</summary>
    public SurfaceLightingWorldProbeTransportTests(HeadlessGLFixture fixture):base(fixture) { }

    #region World-probe publication
    /// <summary>Every worker direction receives produced lighting, then updates the real directional atlas after removal and restoration.</summary>
    [Fact]
    public void ProducedLightingReachesWorldProbeAtlasAcrossLightChanges()
    {
        EnsureShaderTestAvailable();
        using var engine=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var atlas=new SurfaceLightingWorldProbeFixture();
        var world=new ControlledVoxelWorld();world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        var traced=TraceWorker(world,64,0,new(4,36,4));
        var bright=Resolve(room.Geometry.Scene,room.Snapshot,traced);
        AssertEnergy(atlas.Upload(bright),true,"world atlas bright");
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        var dark=Resolve(room.Geometry.Scene,room.Snapshot,traced);
        AssertEnergy(atlas.Upload(dark),false,"world atlas light removed");
        Assert.Equal(bright.Confidence,dark.Confidence);
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        AssertEnergy(atlas.Upload(Resolve(room.Geometry.Scene,room.Snapshot,traced)),true,"world atlas restored");
        room.WithholdPages();
        Assert.False(Resolve(room.Geometry.Scene,room.Snapshot,traced).Success);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>A GPU capture rejected before geometry publication is retried without new page feedback allocation.</summary>
    [Fact]
    public void FailedCaptureRetriesAfterGeometryBecomesAvailable()
    {
        EnsureShaderTestAvailable();
        using var engine=new EngineShaderPlatformScope();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true){GeometryAvailable=false};
        runtime.RunUntil(()=>runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _) && mapping.Count==1);
        Assert.False(runtime.TryGetLighting(out _));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out var gpu,out _,out _));
        using(var work=gpu.CaptureWork.Items.MapRange<LumonSceneCaptureWorkGpu>(0,1,MapBufferAccessMask.MapReadBit))
        { Assert.True(work.IsMapped);Assert.NotEqual(0u,work.Span[0].VirtualPageIndex&0x80000000u); }
        runtime.GeometryAvailable=true;
        runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var ready));
        Assert.True(ready.Generation>0);
    }

    /// <summary>Registered cache callbacks publish changed source lighting and replacement resources to real worker-hit queries.</summary>
    [Fact]
    public void RuntimePublicationAndInvalidationReachWorkerHits()
    {
        EnsureShaderTestAvailable();
        using var engine=new EngineShaderPlatformScope();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        var world=new ControlledVoxelWorld();world.AddRoom((0,32,0),(7,39,7),materialId:runtime.BlockId);
        // Select a real scheduled single-direction update hitting the one feedback-requested +X patch.
        // Other directions are deliberately outside this fixture's residency, not silently treated as black.
        LumOnWorldProbeTraceResult traced=default;
        for(int frame=0;frame<64;frame++)
        {
            var candidate=TraceWorker(world,1,frame,new(1.5,33.5,1.5));
            var hit=candidate.AtlasSamples[0].SurfaceHit;
            if(hit is {} h && h.X==0 && h.Y>=32 && h.Y<36 && h.Z>=0 && h.Z<4) { traced=candidate;break; }
        }
        Assert.True(traced.Success,"No scheduled direction reached the requested patch.");
        using var atlas=new SurfaceLightingWorldProbeFixture();
        Assert.True(runtime.TryGetLighting(out var original));
        AssertEnergy(atlas.Upload(Resolve(runtime.Geometry.Resources!,original,traced)),true,"runtime world atlas");
        runtime.ChangeBlockLight(0);runtime.Frame();
        runtime.RunUntil(()=>DirectLightEquals(runtime,0));
        Assert.True(runtime.TryGetLighting(out var dark));
        Assert.Equal(original.DependencyRevision,dark.DependencyRevision);
        AssertEnergy(atlas.Upload(Resolve(runtime.Geometry.Resources!,dark,traced)),false,"runtime light removal");
        runtime.ChangeBlockLight(32);runtime.Frame();
        runtime.RunUntil(()=>DirectLightEquals(runtime,32));
        Assert.True(runtime.TryGetLighting(out var restored));
        Assert.Equal(original.DependencyRevision,restored.DependencyRevision);
        AssertEnergy(atlas.Upload(Resolve(runtime.Geometry.Resources!,restored,traced)),true,"runtime light restoration");
        runtime.RequestAtlasRecreation();runtime.Frame();
        runtime.RunUntil(()=>runtime.TryGetLighting(out var current) && !ReferenceEquals(current.OutgoingRadiance,restored.OutgoingRadiance));
        Assert.True(runtime.TryGetLighting(out var recreated));
        Assert.False(restored.OutgoingRadiance.IsValid);
        AssertEnergy(atlas.Upload(Resolve(runtime.Geometry.Resources!,recreated,traced)),true,"runtime recreated cache");
        runtime.LeaveWorld();Assert.False(runtime.TryGetLighting(out _));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Worker and transport
    /// <summary>Waits for actual refreshed direct values rather than an identity revision that light changes must preserve.</summary>
    private static bool DirectLightEquals(SurfaceCacheRuntimeFixture runtime,float expected)
    {
        if(!runtime.TryGetLighting(out var snapshot) || !runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _)) return false;
        using var pixels=SurfaceLightingPageReadback.Read(snapshot.DirectIrradiance,snapshot,pages.Single().Key);
        for(int index=0;index<pixels.Length;index+=4)
            if(pixels.Span[index+3]!=1 || Math.Abs(pixels.Span[index]-expected)>.01f) return false;
        return true;
    }

    /// <summary>Runs the real CPU integrator with controlled collision data and deferred lighting enabled.</summary>
    private static LumOnWorldProbeTraceResult TraceWorker(ControlledVoxelWorld world,int count,int frame,VanillaGraphicsExpanded.Numerics.Vector3d position)
    {
        var request=new LumOnWorldProbeUpdateRequest(0,new(),new(),0);
        var work=new LumOnWorldProbeTraceWorkItem(frame,request,position,64,8,count,false,.25f,-1,1e-6f,DeferSurfaceLighting:true);
        var result=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        Assert.True(result.Success);Assert.All(result.AtlasSamples,s=>Assert.NotNull(s.SurfaceHit));
        return result;
    }

    /// <summary>Passes only actual CPU hit descriptors to the production asynchronous query transport.</summary>
    private static LumOnWorldProbeTraceResult Resolve(TraceGeometryGpuScene geometry,in SurfaceLightingSnapshot snapshot,in LumOnWorldProbeTraceResult traced)
    {
        using var assets=new BinaryShaderApiFixture();using var queries=new SurfaceLightingQueryBatch(assets.Api);
        queries.Submit(geometry,snapshot,traced.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray());
        GpuTestFence.WaitForGpuOrSkip("World cache consumer query");Assert.True(queries.TryRead(out var answers));
        int index=0;return WorldProbeSurfaceLighting.Resolve(traced,answers,ref index);
    }
    #endregion
}
