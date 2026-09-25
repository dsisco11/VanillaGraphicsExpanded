using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Guards deferred hit descriptors, resolved darkness, and delayed-result invalidation.</summary>
public sealed class WorldProbeSurfaceLightingTests
{
    #region Deferred hit lighting
    /// <summary>Workers retain hits and distances without evaluating the previous lighting approximation.</summary>
    [Fact]
    public void WorkerDefersHitsAndResolvedLightingPreservesGeometry()
    {
        var world=new ControlledVoxelWorld {DefaultLight=Vector4.One};
        world.AddRoom((0,32,0),(7,39,7));
        var request=new LumOnWorldProbeUpdateRequest(0,new(),new(),0);
        var work=new LumOnWorldProbeTraceWorkItem(0,request,new(4,36,4),64,8,64,false,.25f,-1,1e-6f,
            DeferSurfaceLighting:true,SurfaceRevision:12);
        var result=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        Assert.True(result.Success); Assert.Equal(64,result.AtlasSamples.Length);
        Assert.All(result.AtlasSamples,s=> {Assert.True(s.SurfaceHit.HasValue);Assert.Equal(Vector3.Zero,s.RadianceRgb);Assert.True(s.AlphaEncodedDistSigned>0);});
        var answers=result.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray();
        for(int i=0;i<answers.Length;i++)answers[i].Result=new(2,3,4,1);
        int index=0;
        var resolved=WorldProbeSurfaceLighting.Resolve(result,answers,ref index);
        Assert.True(resolved.Success); Assert.Equal(64,index);
        Assert.Equal(result.Confidence,resolved.Confidence);
        Assert.Equal(result.MeanLogHitDistance,resolved.MeanLogHitDistance);
        Assert.Equal(result.ShortRangeAoConfidence,resolved.ShortRangeAoConfidence);
        Assert.Equal(12,resolved.SurfaceRevision);
        for(int i=0;i<answers.Length;i++)
        {
            Assert.Equal(new Vector3(2,3,4),resolved.AtlasSamples[i].RadianceRgb);
            Assert.Equal(result.AtlasSamples[i].AlphaEncodedDistSigned,resolved.AtlasSamples[i].AlphaEncodedDistSigned);
        }
        for(int i=0;i<answers.Length;i++)answers[i].Result=new(0,0,0,1);
        index=0; Assert.True(WorldProbeSurfaceLighting.Resolve(result,answers,ref index).Success);
        answers[12].Result=default;
        index=0;
        var partial=WorldProbeSurfaceLighting.Resolve(result,answers,ref index);
        Assert.True(partial.Success);
        Assert.Equal(63,partial.AtlasSamples.Length);
        Assert.Single(partial.RetrySamples!);
    }
    #endregion

    #region Partial resolution
    /// <summary>Valid darkness and sky remain publishable while missing and nonfinite hit answers retain their descriptors.</summary>
    [Fact]
    public void MixedAnswersPreserveReadyDirectionsAndRetryOnlyUnresolvedHits()
    {
        var world=new ControlledVoxelWorld();world.AddRoom((0,32,0),(7,39,7));
        var work=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(4,36,4),64,8,4,false,.25f,-1,1e-6f,
            DeferSurfaceLighting:true,SurfaceRevision:12);
        var traced=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        var sky=traced.AtlasSamples[0] with { OctX=7,OctY=7,SurfaceHit=null,AlphaEncodedDistSigned=-1 };
        var source=traced with { AtlasSamples=[..traced.AtlasSamples,sky] };
        var answers=traced.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray();
        answers[0].Result=new(0,0,0,1);
        answers[1].Result=new(2,3,4,1);
        answers[2].Result=default;
        answers[3].Result=new(float.NaN,1,1,1);
        int index=0;
        var partial=WorldProbeSurfaceLighting.Resolve(source,answers,ref index);
        Assert.True(partial.Success);Assert.Equal(4,index);
        Assert.Equal(3,partial.AtlasSamples.Length);Assert.Equal(2,partial.RetrySamples!.Length);
        Assert.Equal(Vector3.Zero,partial.AtlasSamples[0].RadianceRgb);
        Assert.Equal(new Vector3(2,3,4),partial.AtlasSamples[1].RadianceRgb);
        Assert.Equal(sky,partial.AtlasSamples[2]);
        Assert.All(partial.AtlasSamples,s=>Assert.Null(s.SurfaceHit));
        Assert.Equal(traced.AtlasSamples[2..],partial.RetrySamples);
        var retry=source with { AtlasSamples=partial.RetrySamples };
        var ready=retry.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray();
        foreach(ref var answer in ready.AsSpan()) answer.Result=new(5,6,7,1);
        index=0;var complete=WorldProbeSurfaceLighting.Resolve(retry,ready,ref index);
        Assert.True(complete.Success);Assert.Equal(2,index);Assert.Equal(2,complete.AtlasSamples.Length);
        Assert.True(complete.RetrySamples is null || complete.RetrySamples.Length==0);
        Assert.Equal(source.Request,complete.Request);Assert.Equal(12,complete.SurfaceRevision);
    }

    /// <summary>Missing answer storage never turns unresolved hits into valid zero lighting.</summary>
    [Fact]
    public void MissingAnswersProduceOnlyRetryDescriptors()
    {
        var world=new ControlledVoxelWorld();world.AddRoom((0,32,0),(7,39,7));
        var work=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(4,36,4),64,8,4,false,.25f,-1,1e-6f,DeferSurfaceLighting:true);
        var source=new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        int index=0;var result=WorldProbeSurfaceLighting.Resolve(source,[],ref index);
        Assert.False(result.Success);Assert.Empty(result.AtlasSamples);Assert.Equal(source.AtlasSamples,result.RetrySamples);
    }
    #endregion

    #region Request lifetime
    /// <summary>A delayed answer must not complete a reused slot or survive a geometry dirty event.</summary>
    [Fact]
    public void RequestTicketsRejectResetReuseAndDirtyHits()
    {
        var scheduler=new LumOnWorldProbeScheduler(1,2);
        Vec3d camera=new(0,0,0);
        scheduler.UpdateOrigins(camera,1);
        var first=scheduler.BuildUpdateList(0,camera,1,[8],1,4096,8).Single();
        Assert.True(scheduler.TryClaim(first,0)); Assert.True(scheduler.IsCurrent(first));
        scheduler.MarkDirtyWorldAabb(0,new(-100,-100,-100),new(100,100,100),1);
        Assert.False(scheduler.IsCurrent(first));
        scheduler.Complete(first,1,false);
        scheduler.ResetAll(); scheduler.UpdateOrigins(camera,1);
        var next=scheduler.BuildUpdateList(2,camera,1,[8],1,4096,8).Single();
        Assert.NotEqual(first.Ticket,next.Ticket);
        Assert.False(scheduler.TryClaim(first,2));
        Assert.True(scheduler.TryClaim(next,2));
        scheduler.Complete(first,3,true);
        Assert.True(scheduler.IsCurrent(next));
    }
    #endregion
}
