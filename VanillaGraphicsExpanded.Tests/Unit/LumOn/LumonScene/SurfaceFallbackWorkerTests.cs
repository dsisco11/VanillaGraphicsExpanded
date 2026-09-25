using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks bounded Surface Cache geometry work and immutable completion ownership.</summary>
public sealed class SurfaceFallbackWorkerTests
{
    #region Budgets and lifetime
    /// <summary>GPU requests and commits preserve their explicit storage strides.</summary>
    [Fact]
    public void StorageLayoutsMatchShaderRecords()
    {
        Assert.Equal(64,Marshal.SizeOf<SurfaceFallbackRequest>());
        Assert.Equal(32,Marshal.SizeOf<SurfaceFallbackCommit>());
    }

    /// <summary>Unused frame credit cannot accumulate and an undrained result retains its admission.</summary>
    [Fact]
    public void RayCreditIsPerFrameAndAdmissionIncludesUndrainedResults()
    {
        var scene=new Scene();using var worker=new SurfaceFallbackWorker(_=>scene);
        worker.BeginFrame(0);worker.BeginFrame(1);
        Assert.True(worker.TrySubmit([Request(64),Request(1)]));
        Assert.True(SpinWait.SpinUntil(()=>scene.Calls.Count==64,TimeSpan.FromSeconds(3)));
        worker.BeginFrame(1);
        Assert.False(SpinWait.SpinUntil(()=>scene.Calls.Count>64,TimeSpan.FromMilliseconds(50)));
        Assert.False(worker.TryRead(out _));Assert.False(worker.TrySubmit([Request()]));
        worker.BeginFrame(2);
        Assert.True(SpinWait.SpinUntil(()=>scene.Calls.Count==65,TimeSpan.FromSeconds(3)));
        Assert.True(worker.Busy);Assert.False(worker.TrySubmit([Request()]));
        var result=Read(worker);Assert.NotNull(result);Assert.All(result.Texels,item=>Assert.True(item.Complete));
        Assert.False(worker.Busy);Assert.True(worker.TrySubmit([Request()]));
    }

    /// <summary>Input storage and per-texel ray counts remain bounded before worker allocation.</summary>
    [Theory]
    [InlineData(17,1)] [InlineData(1,0)] [InlineData(1,65)]
    public void AdmissionRejectsUnboundedInputs(int texels,int rays)
    {
        using var worker=new SurfaceFallbackWorker(_=>new Scene());
        Assert.Throws<ArgumentOutOfRangeException>(()=>worker.TrySubmit(Enumerable.Repeat(Request(rays),texels).ToImmutableArray()));
    }

    /// <summary>The maximum retained batch drains over sixteen independent frame allowances without dropping rays.</summary>
    [Fact]
    public void MaximumBatchResumesAcrossFrames()
    {
        var scene=new Scene();using var worker=new SurfaceFallbackWorker(_=>scene);
        Assert.True(worker.TrySubmit(Enumerable.Repeat(Request(64),16).ToImmutableArray()));
        Assert.Empty(scene.Calls);
        for(int frame=0;frame<16;frame++)
        {
            worker.BeginFrame(frame);int expected=(frame+1)<<6;
            Assert.True(SpinWait.SpinUntil(()=>scene.Calls.Count==expected,TimeSpan.FromSeconds(3)));
        }
        var result=Read(worker);Assert.NotNull(result);Assert.Equal(16,result.Texels.Length);Assert.Equal(1024,scene.Calls.Count);
    }

    /// <summary>Cancellation does not permit overlapping terrain workers while a source ignores cancellation.</summary>
    [Fact]
    public void CancelRetainsAdmissionUntilTerrainReturns()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var scene=new Scene{BeforeTrace=()=>{entered.Set();release.Wait(TestContext.Current.CancellationToken);}};
        using var worker=new SurfaceFallbackWorker(_=>scene);worker.BeginFrame(0);
        Assert.True(worker.TrySubmit([Request()]));Assert.True(entered.Wait(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
        try { worker.Cancel();Assert.True(worker.Busy);Assert.False(worker.TrySubmit([Request()]));Assert.False(worker.TryRead(out _)); }
        finally { release.Set(); }
        Assert.Null(Read(worker));Assert.False(worker.Busy);
    }
    #endregion

    #region Geometry outcomes and dependencies
    /// <summary>Only hits and established sky produce complete estimators; other outcomes retain unresolved texels.</summary>
    [Theory]
    [InlineData(0,false)] [InlineData(1,true)] [InlineData(2,true)]
    [InlineData(3,false)] [InlineData(4,false)] [InlineData(5,false)]
    public void CompletionPreservesExplicitTraceOutcomes(int outcome,bool complete)
    {
        var scene=new Scene{Outcome=(WorldProbeTraceOutcome)outcome};
        using var worker=new SurfaceFallbackWorker(_=>scene);worker.BeginFrame(0);
        var request=Request(3);Assert.True(worker.TrySubmit([request]));var result=Read(worker);Assert.NotNull(result);
        var texel=Assert.Single(result.Texels);Assert.Equal(complete,texel.Complete);Assert.Equal(request,texel.Request);
        Assert.Equal(outcome==1?3:0,result.Queries.Length);Assert.Equal(complete?3:1,scene.Calls.Count);
        Assert.All(scene.Calls,call=>{Assert.Equal(new Vector3d(-16777216.25,35.5,16777216.75),call.Origin);Assert.Equal(512,call.Distance);});
    }

    /// <summary>Duplicate chunk observations are coalesced while changed identities invalidate the batch.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ChunkIdentityMustRemainStableWithinBatch(bool replace)
    {
        object identity=new();int count=0;
        using var worker=new SurfaceFallbackWorker(observe=>new Scene{BeforeTrace=()=>observe(new(1,2,3),replace&&++count>1?new object():identity)});
        worker.BeginFrame(0);Assert.True(worker.TrySubmit([Request(2)]));var result=Read(worker);
        if(replace) Assert.Null(result);
        else { Assert.NotNull(result);Assert.Same(identity,Assert.Single(result.Dependencies).Identity); }
    }

    /// <summary>Dependency overflow discards estimates instead of retaining unbounded chunk references.</summary>
    [Fact]
    public void DependencyCapRejectsExcessiveTraversal()
    {
        using var worker=new SurfaceFallbackWorker(observe=>new Scene{BeforeTrace=()=>
        { for(int index=0;index<=SurfaceFallbackWorker.MaximumDependencies;index++)observe(new(index,0,0),null); }});
        worker.BeginFrame(0);Assert.True(worker.TrySubmit([Request()]));Assert.Null(Read(worker));
    }

    /// <summary>Actual block collision tracing reports its chunk dependencies without querying vanilla light.</summary>
    [Fact]
    public void ProductionCollisionAdapterNeverReadsBlockLighting()
    {
        var world=new ControlledVoxelWorld{MapSizeY=256};
        world.AddRoom((-2,32,-2),(2,38,2));
        using var worker=new SurfaceFallbackWorker(observe=>new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),
            sampleVanillaLighting:false,maxTraversalSteps:512,observeChunk:observe));
        worker.BeginFrame(0);var request=Request(4);request.X=0;request.Z=0;request.Fraction=new(.5f,.5f,.5f,4);
        Assert.True(worker.TrySubmit([request]));var result=Read(worker);Assert.NotNull(result);
        Assert.True(Assert.Single(result.Texels).Complete);Assert.Equal(4,result.Queries.Length);
        Assert.NotEmpty(result.Dependencies);Assert.Empty(world.LightQueries);
    }
    #endregion

    #region Fixture helpers
    /// <summary>Creates a large signed origin without losing its sub-block fraction.</summary>
    private static SurfaceFallbackRequest Request(int rays=1)=>new(){Page=1,Slot=2,Patch=3,Linear=27,
        X=-16777217,Y=35,Z=16777216,Seed=41,Fraction=new(.75f,.5f,.75f,rays),Normal=new(0,1,0,0)};

    /// <summary>Waits in the test harness for one drained immutable completion.</summary>
    private static SurfaceFallbackResult? Read(SurfaceFallbackWorker worker)
    { SurfaceFallbackResult? result=null;Assert.True(SpinWait.SpinUntil(()=>worker.TryRead(out result),TimeSpan.FromSeconds(3)));return result; }

    /// <summary>Records inputs while providing deterministic collision outcomes and synchronization hooks.</summary>
    private sealed class Scene:IWorldProbeTraceScene
    {
        public ConcurrentQueue<(Vector3d Origin,Vector3 Direction,double Distance)> Calls {get;}=new();
        public WorldProbeTraceOutcome Outcome {get;init;}=WorldProbeTraceOutcome.Sky;
        public Action? BeforeTrace {get;init;}
        /// <summary>Returns geometry metadata independent of any radiance source.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d origin,Vector3 direction,double distance,CancellationToken token,out LumOnWorldProbeTraceHit hit)
        { BeforeTrace?.Invoke();Calls.Enqueue((origin,direction,distance));hit=new(.25,71,default,new(-16777217,35,16777216),new(0,-1,0),default,default);return Outcome; }
    }
    #endregion
}
