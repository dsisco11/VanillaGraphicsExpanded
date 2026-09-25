using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises bounded production compute traversal against independently authored CPU geometry.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeComputeTraceTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Geometry and lighting
    /// <summary>Mixed floor hits and verified sky retain nontrivial bent-direction metadata and signed hit-distance encoding.</summary>
    [Fact]
    public void MixedSkyAndFloorMetadataMatchesCpu()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:40,surfaceResolution:64);room.Seed();
        using var assets=new BinaryShaderApiFixture();using var batch=new WorldProbeTraceBatch(assets.Api);
        var world=new ControlledVoxelWorld { MapSizeY=40 };
        var block=new Vintagestory.API.Common.Block { BlockId=room.BlockId,CollisionBoxes=Vintagestory.API.Common.Block.DefaultCollisionSelectionBoxes };
        for(int x=-32;x<=32;x++)for(int z=-32;z<=32;z++)world.SetBlock(x,32,z,block);
        var item=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(3.5,35.5,3.5),16,4,16,false,.25f,-1,1e-6f,0,true);
        var expected=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),item,CancellationToken.None);
        var rays=WorldProbeGpuIntegration.CreateDirections(item);
        batch.Submit(room.Geometry.Scene,room.Snapshot,[new(item.ProbePosWorld,item.MaxTraceDistanceWorld,40,item.WorldProbeOctahedralTileSize,0,rays.Length,item.NearbySolidHitDistance)],rays.AsSpan());GpuTestFence.WaitForGpuOrSkip("Mixed sky and floor metadata");
        Assert.True(batch.TryRead(out var answers));
        var actual=WorldProbeGpuIntegration.Integrate(item,answers,0,answers.Length);
        Assert.True(expected.Success);Assert.True(actual.Success);
        Assert.Contains(actual.AtlasSamples,sample=>sample.AlphaEncodedDistSigned<0);
        Assert.Contains(actual.AtlasSamples,sample=>sample.AlphaEncodedDistSigned>0);
        Assert.True(actual.ShortRangeAoConfidence>0);
        Assert.Equal(expected.Confidence,actual.Confidence);Assert.Equal(expected.ImportanceFlags,actual.ImportanceFlags);
        Assert.InRange(Vector3.Distance(expected.ShortRangeAoDirWorld,actual.ShortRangeAoDirWorld),0,.0001f);
        Assert.InRange(Math.Abs(expected.ShortRangeAoConfidence-actual.ShortRangeAoConfidence),0,.0001f);
        Assert.InRange(Math.Abs(expected.MeanLogHitDistance-actual.MeanLogHitDistance),0,.0001f);
        for(int index=0;index<actual.AtlasSamples.Length;index++)
            Assert.InRange(Math.Abs(expected.AtlasSamples[index].AlphaEncodedDistSigned-actual.AtlasSamples[index].AlphaEncodedDistSigned),0,.0001f);
    }

    /// <summary>GPU answers preserve the shared selected directions, confidence, distance, AO and importance calculations.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IntegratedMetadataMatchesCpuForSelectedDirections(bool importanceSampling)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var world=new ControlledVoxelWorld { MapSizeY=256 };
        world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        var item=new LumOnWorldProbeTraceWorkItem(11,new(0,new(),new(),3,Ticket:47),new(3.5,35.5,3.5),16,8,16,importanceSampling,.25f,-1,1e-6f,1.5,true,19);
        var expected=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),item,CancellationToken.None);
        var rays=WorldProbeGpuIntegration.CreateDirections(item);
        batch.Submit(room.Geometry.Scene,room.Snapshot,[new(item.ProbePosWorld,item.MaxTraceDistanceWorld,256,item.WorldProbeOctahedralTileSize,0,rays.Length,item.NearbySolidHitDistance)],rays.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("World probe integration parity");
        Assert.True(batch.TryRead(out var answers));
        var actual=WorldProbeGpuIntegration.Integrate(item,answers,0,answers.Length);
        Assert.True(expected.Success); Assert.True(actual.Success);
        Assert.Equal(expected.Request,actual.Request); Assert.Equal(expected.FrameIndex,actual.FrameIndex);
        Assert.Equal(expected.SurfaceRevision,actual.SurfaceRevision);
        Assert.Equal(expected.ImportanceFlags,actual.ImportanceFlags);
        Assert.Equal(expected.Confidence,actual.Confidence); Assert.Equal(expected.SkyIntensity,actual.SkyIntensity);
        Assert.InRange(Math.Abs(expected.MeanLogHitDistance-actual.MeanLogHitDistance),0,.0001f);
        Assert.InRange(Vector3.Distance(expected.ShortRangeAoDirWorld,actual.ShortRangeAoDirWorld),0,.0001f);
        Assert.InRange(Math.Abs(expected.ShortRangeAoConfidence-actual.ShortRangeAoConfidence),0,.0001f);
        Assert.Equal(expected.AtlasSamples.Length,actual.AtlasSamples.Length);
        for(int index=0;index<actual.AtlasSamples.Length;index++)
        {
            Assert.Equal(expected.AtlasSamples[index].OctX,actual.AtlasSamples[index].OctX);
            Assert.Equal(expected.AtlasSamples[index].OctY,actual.AtlasSamples[index].OctY);
            Assert.InRange(Math.Abs(expected.AtlasSamples[index].AlphaEncodedDistSigned-actual.AtlasSamples[index].AlphaEncodedDistSigned),0,.0001f);
            Assert.Null(actual.AtlasSamples[index].SurfaceHit);
            Assert.True(actual.AtlasSamples[index].RadianceRgb.X>0);
        }
    }

    /// <summary>Signed large anchors preserve sub-voxel distances, normals and material identity on all cube faces.</summary>
    [Theory]
    [InlineData(0)] [InlineData(16777216)] [InlineData(-16777216)]
    public void FullCubeHitsMatchCpuWithoutFloatWorldCoordinateLoss(int offset)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(xOffset:offset);
        room.Seed();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var world=new ControlledVoxelWorld { MapSizeY=256 };
        world.AddRoom((offset,32,0),(offset+7,39,7),materialId:room.BlockId);
        var cpu=world.CreateTraceScene();
        foreach(var direction in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ})
        {
            var origin=new VanillaGraphicsExpanded.Numerics.Vector3d(offset+3.25,35.5,3.75);
            Assert.Equal(WorldProbeTraceOutcome.Hit,cpu.Trace(origin,direction,16,CancellationToken.None,out var expected));
            var result=Trace(batch,room.Geometry.Scene,room.Snapshot,new(origin,direction,16,256));
            Assert.Equal(WorldProbeTraceOutcome.Hit,result.TraceOutcome); Assert.False(result.RequiresCpuFallback);
            Assert.Equal(expected.HitBlockPos.X,result.Hit.X); Assert.Equal(expected.HitBlockPos.Y,result.Hit.Y); Assert.Equal(expected.HitBlockPos.Z,result.Hit.Z);
            Assert.Equal(expected.HitFaceNormal.X,result.Hit.NormalX); Assert.Equal(expected.HitFaceNormal.Y,result.Hit.NormalY); Assert.Equal(expected.HitFaceNormal.Z,result.Hit.NormalZ);
            Assert.Equal(expected.HitBlockId,result.Hit.BlockId); Assert.InRange(Math.Abs(expected.HitDistance-result.Hit.Fraction.W),0,.0001);
            Assert.Equal(1,result.Hit.Result.W); Assert.True(result.Hit.Result.X>0);
        }
        Assert.Equal(64,Marshal.SizeOf<WorldProbeTraceProbeGpu>());
    }

    /// <summary>Valid black and unavailable hit lighting remain distinct without losing the geometry intersection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GeometryHitDoesNotDependOnLightingAvailability(bool lightingAvailable)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(blockLight:0); room.Seed();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var result=Trace(batch,room.Geometry.Scene,lightingAvailable?room.Snapshot:null,new(new(3.5,35.5,3.5),Vector3.UnitX,16,256));
        Assert.Equal(WorldProbeTraceOutcome.Hit,result.TraceOutcome); Assert.Equal(room.BlockId,result.Hit.BlockId);
        Assert.Equal(lightingAvailable?1:0,result.Hit.Result.W); Assert.Equal(0,result.Hit.Result.X);
        Assert.False(result.RequiresCpuFallback);
    }

    /// <summary>A full-cube entry exactly at the finite endpoint is a hit, while a shorter clear segment remains unresolved.</summary>
    [Theory]
    [InlineData(3.75,true)] [InlineData(3.749,false)]
    public void ExactSurfaceEndpointMatchesCpu(double distance,bool hits)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var result=Trace(batch,room.Geometry.Scene,room.Snapshot,new(new(3.25,35.5,3.5),Vector3.UnitX,distance,256));
        Assert.Equal(hits?WorldProbeTraceOutcome.Hit:WorldProbeTraceOutcome.DistanceLimit,result.TraceOutcome);
        Assert.Equal(hits?1:0,result.Hit.Result.W);
    }
    #endregion

    #region Traversal outcomes
    /// <summary>GPU-generated tiles keep per-probe output ownership and restore the caller's indirect binding.</summary>
    [Fact]
    public void MultipleProbeTilesUseIndependentInputsAndRestoreIndirectBinding()
    {
        EnsureContextValid();
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,2,0),true,32,4),new(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        using var assets=new BinaryShaderApiFixture();using var batch=new WorldProbeTraceBatch(assets.Api);
        using var prior=GpuIndirectBuffer.Create();prior.UploadDispatchCommand(new(7,8,9));
        using var binding=prior.BindDispatchScope();
        int original=GL.GetInteger(GetPName.DispatchIndirectBufferBinding);
        WorldProbeTraceProbeGpu[] probes=
        [
            new(new(.5,.5,.5),8,4,8,0,129,8),
            new(new(3.5,2.5,3.5),.25,4,8,129,3,.25),
            new(new(-3.5,1.5,-3.5),8,4,8,132,65,8,maxSteps:0)
        ];
        batch.Submit(geometry.Scene,null,probes,Enumerable.Repeat(0x80000002u,197).ToArray());
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        GpuTestFence.WaitForGpuOrSkip("Multiple generated probe tiles");
        Assert.True(batch.TryRead(out var answers));Assert.Equal(197,answers.Length);
        for(int index=0;index<answers.Length;index++)
        {
            var expected=index<129?WorldProbeTraceOutcome.Sky:index<132?WorldProbeTraceOutcome.DistanceLimit:WorldProbeTraceOutcome.BudgetExhausted;
            Assert.Equal(expected,answers[index].TraceOutcome);
        }
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        var smaller=Trace(batch,geometry.Scene,null,new(new(.5,.5,.5),Vector3.UnitY,.25,4));
        Assert.Equal(WorldProbeTraceOutcome.DistanceLimit,smaller.TraceOutcome);
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Invalid selector slices cannot create uninitialized output or out-of-range indirect work.</summary>
    [Fact]
    public void InvalidDirectionOwnershipAndSelectorsAreRejected()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();using var batch=new WorldProbeTraceBatch(assets.Api);
        var probe=new WorldProbeTraceProbeGpu(new(.5,.5,.5),8,4,8,0,1,8);
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe],[64u]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe],[0x80000006u]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe with { FirstDirection=1 }],[0u]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe],[0u,1u]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe,probe],[0u,1u]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(null,null,[probe with { NearbyDistance=default }],[0x80000000u]));
        Assert.False(batch.Pending);
    }

    /// <summary>Partial workgroups and reused capacity return only current admissions, with bounded rejection before dispatch.</summary>
    [Fact]
    public void BatchBoundsAndCapacityReuseDoNotExposeOldAnswers()
    {
        EnsureContextValid();
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,2,0),true,32,4),new(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(geometry.Scene,null,[],[]));
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(geometry.Scene,null,[new(new(.5,.5,.5),8,4,8,0,8193)],new uint[8193]));
        var probe=new WorldProbeTraceProbeGpu(new(.5,.5,.5),8,4,8,0,65,nearbyDistance:8);
        var selectors=Enumerable.Repeat(0x80000002u,65).ToArray();
        batch.Submit(geometry.Scene,null,[probe],selectors);
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(geometry.Scene,null,[probe],selectors));
        GpuTestFence.WaitForGpuOrSkip("World probe partial workgroup");
        Assert.True(batch.TryRead(out var answers));Assert.Equal(65,answers.Length);
        Assert.All(answers,answer=>Assert.Equal(WorldProbeTraceOutcome.Sky,answer.TraceOutcome));
        var shortRay=new CardinalTrace(new(.5,.5,.5),Vector3.UnitY,1,4);
        Assert.Equal(WorldProbeTraceOutcome.DistanceLimit,Trace(batch,geometry.Scene,null,shortRay).TraceOutcome);
    }

    /// <summary>A clear route certifies sky only at the authoritative boundary within both traversal limits.</summary>
    [Theory]
    [InlineData(8d,512,(int)WorldProbeTraceOutcome.Sky)]
    [InlineData(3.5d,4,(int)WorldProbeTraceOutcome.Sky)]
    [InlineData(3.49d,4,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(2d,512,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(8d,2,(int)WorldProbeTraceOutcome.BudgetExhausted)]
    [InlineData(8d,0,(int)WorldProbeTraceOutcome.BudgetExhausted)]
    public void ClearVerticalRouteMatchesCpuBoundaryMeaning(double distance,int steps,int expected)
    {
        EnsureContextValid();
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,2,0),true,32,4),new(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var result=Trace(batch,geometry.Scene,null,new(new(.5,.5,.5),Vector3.UnitY,distance,4,steps));
        Assert.Equal((WorldProbeTraceOutcome)expected,result.TraceOutcome); Assert.False(result.RequiresCpuFallback); Assert.Equal(0,result.Hit.Result.W);
    }

    /// <summary>Coverage exits and unsupported shapes request CPU continuation; unpublished cells wait without claiming sky.</summary>
    [Theory]
    [InlineData(1u,true,true)] [InlineData(3u,true,true)] [InlineData(0u,true,false)] [InlineData(1u,false,false)]
    public void UnavailableReasonsControlGeometryFallback(uint word,bool published,bool fallback)
    {
        EnsureContextValid();
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,36,0),true,32,256),new(),(_,_,_)=>new(word,0,0));
        if(published)geometry.Publish();
        using var assets=new BinaryShaderApiFixture(); using var batch=new WorldProbeTraceBatch(assets.Api);
        var result=Trace(batch,geometry.Scene,null,new(new(.5,36.5,.5),Vector3.UnitX,64,256));
        Assert.Equal(WorldProbeTraceOutcome.Unavailable,result.TraceOutcome); Assert.Equal(fallback,result.RequiresCpuFallback);
        Assert.Equal(0,result.Hit.Result.W);
    }
    #endregion

    #region Readback
    /// <summary>Waits only in the test harness, then consumes exactly one production asynchronous answer.</summary>
    private static WorldProbeTraceAnswerGpu Trace(WorldProbeTraceBatch batch,TraceGeometryGpuScene geometry,SurfaceLightingSnapshot? lighting,CardinalTrace ray)
    {
        int cardinal=Array.IndexOf(new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ},ray.Direction);
        Assert.InRange(cardinal,0,5);
        batch.Submit(geometry,lighting,[new(ray.Origin,ray.Distance,ray.WorldHeight,8,0,1,ray.Distance,ray.Steps)],[0x80000000u|(uint)cardinal]); Assert.True(batch.Pending);
        GpuTestFence.WaitForGpuOrSkip("World probe compute trace");
        Assert.True(batch.TryRead(out var results)); Assert.Single(results); Assert.False(batch.Pending);
        Assert.False(batch.TryRead(out _));
        return results[0];
    }
    /// <summary>Describes a cardinal test ray which is encoded through the production nearby selector contract.</summary>
    private readonly record struct CardinalTrace(VanillaGraphicsExpanded.Numerics.Vector3d Origin,Vector3 Direction,double Distance,int WorldHeight,int Steps=512);
    #endregion
}
