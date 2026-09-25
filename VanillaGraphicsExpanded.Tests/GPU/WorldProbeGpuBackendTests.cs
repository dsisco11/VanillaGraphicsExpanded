using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests bounded compute completion against changing borrowed resource lifetimes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeGpuBackendTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Completion lifetime
    /// <summary>Each drained admission validates current geometry and lighting even after an earlier admission was accepted.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void LaterCompletionRejectsChangedLifetime(int change)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();
        bool geometryAvailable=true;
        int claims=0;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>geometryAvailable?room.Geometry.Scene:null,
            ()=>room.Snapshot,(_,_)=>{claims++;return true;},new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.UnexpectedWorldProbeTraceScene(),_=>true);
        var first=new LumOnWorldProbeTraceWorkItem(1,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,8,false,.25f,-1,1e-6f,0,true);
        var second=first with { Request=first.Request with { StorageLinearIndex=1,Ticket=12 } };
        Assert.True(backend.TryEnqueue(first));Assert.True(backend.TryEnqueue(second));Assert.Equal(0,claims);
        Assert.False(backend.TryDequeueResult(out _));Assert.Equal(2,claims);
        GpuTestFence.WaitForGpuOrSkip("World probe backend completion");
        Assert.True(backend.TryDequeueResult(out var complete));Assert.True(complete.Success);Assert.Equal(first.Request,complete.Request);
        Assert.All(complete.AtlasSamples,sample=>Assert.Null(sample.SurfaceHit));
        if(change==0)room.Geometry.Dirty(); else if(change==1)room.DependencyRevision++;else geometryAvailable=false;
        Assert.True(backend.TryDequeueResult(out var rejected));Assert.False(rejected.Success);Assert.Equal(second.Request,rejected.Request);
        Assert.Empty(rejected.AtlasSamples);
    }

    /// <summary>Uploaded geometry still produces deferred hit descriptors when the Surface Cache is unavailable.</summary>
    [Fact]
    public void MissingCacheDoesNotStopGeometryTracing()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        using var assets=new BinaryShaderApiFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>null,(_,_)=>true,new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.UnexpectedWorldProbeTraceScene(),_=>true);
        var item=new LumOnWorldProbeTraceWorkItem(1,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,8,false,.25f,-1,1e-6f,0,true,-1);
        Assert.True(backend.TryEnqueue(item));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("World probe geometry without cache");
        Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
        Assert.All(result.AtlasSamples,sample=>{Assert.NotNull(sample.SurfaceHit);Assert.True(sample.AlphaEncodedDistSigned>0);});
    }
    #endregion

    #region Production renderer
    /// <summary>Default L0 compute publication fills the real atlas without CPU ray-tracing worker reads.</summary>
    [Fact]
    public void DefaultComputePathPublishesWithoutCpuTraversal()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        Assert.True(runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing);
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f,maximumFrames:256);
        Assert.Equal(0,runtime.World.WorkerReads); Assert.Equal(0,runtime.World.VanillaLightReads);
        Assert.True(runtime.WorldConfidence>0);
    }
    #endregion
}
