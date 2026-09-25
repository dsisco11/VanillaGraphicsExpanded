using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises real GPU traversal followed by selective CPU collision and cache resolution.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeCpuFallbackGpuTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Geometry continuation
    /// <summary>Unsupported collision boxes preserve holes and hit the next actual surface instead of becoming opaque cubes.</summary>
    [Theory]
    [InlineData(.25,2)] [InlineData(.75,4)]
    public void UnsupportedShapeUsesActualCpuCollision(double x,int expectedZ)
    {
        EnsureContextValid();
        var partial=new Block {BlockId=1,CollisionBoxes=[new Vintagestory.API.MathTools.Cuboidf(0,0,0,.5f,1,1)]};
        var solid=new Block {BlockId=2,CollisionBoxes=Block.DefaultCollisionSelectionBoxes};
        var materials=new TraceGeometryMaterials();uint partialId=materials.Resolve(partial),solidId=materials.Resolve(solid);
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,36,0),true,32,256),materials,
            (vx,vy,vz)=>new(vx==0&&vy==35&&vz==2?3u|(partialId<<2):vx==0&&vy==35&&vz==4?2u|(solidId<<2):1u,0,0));
        geometry.Publish();
        var world=new ControlledVoxelWorld {MapSizeY=256};world.SetBlock(0,35,2,partial);world.SetBlock(0,35,4,solid);
        var cpu=new ObservedScene(new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false));
        using var assets=new BinaryShaderApiFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>geometry.Scene,()=>null,(_,_)=>true,cpu,_=>true);
        var item=Item(new(x,35.5,.5),-1);
        Assert.True(backend.TryEnqueue(item));backend.BeginFrame(1);
        var result=Read(backend);
        Assert.True(result.Success);Assert.Equal(1,cpu.Calls);
        var sample=Assert.Single(result.AtlasSamples);Assert.NotNull(sample.SurfaceHit);
        Assert.Equal(expectedZ,sample.SurfaceHit.Value.Z);Assert.Equal(expectedZ==2?1:2,sample.SurfaceHit.Value.BlockId);
        Assert.Empty(world.LightQueries);
    }

    /// <summary>Leaving uploaded coverage can establish sky only when the CPU actually reaches the authoritative world boundary.</summary>
    [Fact]
    public void CoverageExitCanReachVerifiedCpuSky()
    {
        EnsureContextValid();
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,36,0),true,16,64),new(),(_,_,_)=>new(1,0,0));geometry.Publish();
        var world=new ControlledVoxelWorld {MapSizeY=64};
        var cpu=new ObservedScene(new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false));
        using var assets=new BinaryShaderApiFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,64,()=>geometry.Scene,()=>null,(_,_)=>true,cpu,_=>true);
        var item=Item(new(.5,35.5,.5),-1) with {WorldProbeOctahedralTileSize=2,MaxTraceDistanceWorld=64};
        for(int frame=0;frame<16;frame++)
        {
            item=item with {FrameIndex=frame};
            uint selected=WorldProbeGpuIntegration.CreateDirections(item)[0];
            if(LumOnWorldProbeAtlasDirections.GetDirections(2)[(int)selected].Y>0)break;
        }
        Assert.True(backend.TryEnqueue(item));backend.BeginFrame(1);
        var result=Read(backend);Assert.True(result.Success);Assert.Equal(1,cpu.Calls);
        Assert.True(Assert.Single(result.AtlasSamples).AlphaEncodedDistSigned<0);Assert.Empty(world.LightQueries);
    }
    #endregion

    #region Delayed ownership and lighting
    /// <summary>CPU fallback results reject changed geometry, cache revisions and scheduler tickets after delayed collision completes.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DelayedFallbackRejectsObsoleteOwnership(int change)
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();room.Seed();MakeUnsupported(room);
        var world=new ControlledVoxelWorld{MapSizeY=256};world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var cpu=new ObservedScene(new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false),token=>{entered.Set();release.Wait(token);});
        using var assets=new BinaryShaderApiFixture();bool current=true;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,(_,_)=>true,cpu,_=>current);
        var item=Item(new(3.5,35.5,3.5),room.DependencyRevision);Assert.True(backend.TryEnqueue(item));backend.BeginFrame(1);
        Assert.False(backend.TryDequeueResult(out _));GpuTestFence.WaitForGpuOrSkip("Fallback ownership dispatch");
        Assert.False(backend.TryDequeueResult(out _));Assert.True(entered.Wait(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
        try
        {
            if(change==0)room.Geometry.Dirty();else if(change==1)room.DependencyRevision++;else current=false;
            release.Set();var result=Read(backend);Assert.False(result.Success);Assert.Equal(item.Request,result.Request);Assert.Empty(result.AtlasSamples);
        }
        finally {release.Set();}
    }

    /// <summary>A CPU-confirmed unsupported hit can resolve captured lighting with matching block identity.</summary>
    [Theory]
    [InlineData(0)] [InlineData(32)]
    public void UnsupportedConfirmedHitResolvesSurfaceCacheLighting(int light)
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture(blockLight:light);room.Seed();MakeUnsupported(room);
        var world=new ControlledVoxelWorld{MapSizeY=256};world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        var cpu=new ObservedScene(new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false));
        using var assets=new BinaryShaderApiFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,(_,_)=>true,cpu,_=>true);
        Assert.True(backend.TryEnqueue(Item(new(3.5,35.5,3.5),room.DependencyRevision)));backend.BeginFrame(1);
        var result=Read(backend);Assert.True(result.Success);var sample=Assert.Single(result.AtlasSamples);Assert.NotNull(sample.SurfaceHit);
        using var queries=new SurfaceLightingQueryBatch(assets.Api);queries.Submit(room.Geometry.Scene,room.Snapshot,[sample.SurfaceHit.Value]);
        GpuTestFence.WaitForGpuOrSkip("Unsupported CPU hit lighting");Assert.True(queries.TryRead(out var answers));
        Assert.Equal(1,answers[0].Result.W);Assert.Equal(light>0,answers[0].Result.X>0);Assert.Empty(world.LightQueries);
        var wrong=sample.SurfaceHit.Value;wrong.BlockId=int.MaxValue;
        queries.Submit(room.Geometry.Scene,room.Snapshot,[wrong]);GpuTestFence.WaitForGpuOrSkip("Replaced unsupported hit identity");
        Assert.True(queries.TryRead(out answers));Assert.Equal(Vector4.Zero,answers[0].Result);
        room.WithholdPages();queries.Submit(room.Geometry.Scene,room.Snapshot,[sample.SurfaceHit.Value]);
        GpuTestFence.WaitForGpuOrSkip("Unready unsupported hit lighting");Assert.True(queries.TryRead(out answers));Assert.Equal(Vector4.Zero,answers[0].Result);
    }
    #endregion

    #region Fixtures
    /// <summary>Uses the single +Z atlas direction for deterministic CPU collision observations.</summary>
    private static LumOnWorldProbeTraceWorkItem Item(Vector3d origin,long revision)=>new(1,new(0,new(),new(),0,Ticket:11),origin,16,1,1,false,.25f,-1,1e-6f,0,true,revision);
    /// <summary>Preserves material identity and captured pages while marking geometry as requiring exact CPU collision.</summary>
    private static void MakeUnsupported(SurfaceLightingEnclosureFixture room)
    {
        room.Geometry.Sample=(x,y,z)=>{var voxel=room.Sample(x,y,z);return (voxel.Geometry&3)==2?voxel with {Geometry=(voxel.Geometry&~3u)|3u}:voxel;};
        room.Geometry.Dirty();room.Geometry.Publish();
    }
    /// <summary>Polls production GPU and worker completion only from the render-context test thread.</summary>
    private static LumOnWorldProbeTraceResult Read(LumOnWorldProbeGpuTraceBackend backend)
    { LumOnWorldProbeTraceResult result=default;Assert.True(SpinWait.SpinUntil(()=>backend.TryDequeueResult(out result),TimeSpan.FromSeconds(5)));return result; }
    /// <summary>Observes actual CPU collision execution and optionally pauses before accessing geometry.</summary>
    private sealed class ObservedScene(IWorldProbeTraceScene inner,Action<CancellationToken>? before=null):IWorldProbeTraceScene
    {
        private int calls;
        public int Calls=>Volatile.Read(ref calls);
        /// <summary>Records one CPU geometry ray without adding any lighting reads.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d origin,Vector3 direction,double distance,CancellationToken token,out LumOnWorldProbeTraceHit hit)
        {Interlocked.Increment(ref calls);before?.Invoke(token);return inner.Trace(origin,direction,distance,token,out hit);}
    }
    #endregion
}
