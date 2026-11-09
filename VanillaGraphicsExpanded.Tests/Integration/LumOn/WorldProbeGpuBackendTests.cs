using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Tests bounded compute completion against changing borrowed resource lifetimes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class WorldProbeGpuBackendTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{

    #region Public API
    #region Completion lifetime
    /// <summary>Each drained admission validates current geometry and lighting even after an earlier admission was accepted.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void LaterCompletionRejectsChangedLifetime(int change)
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        using var assets=new BinaryShaderApiFixture();
        bool geometryAvailable=true;
        int claims=0;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>geometryAvailable?room.Geometry.Scene:null,
            ()=>room.Snapshot,(_,_)=>{claims++;return true;},new VanillaGraphicsExpanded.Tests.Support.Lighting.UnexpectedWorldProbeTraceScene(),_=>true);
        var first=new LumOnWorldProbeTraceWorkItem(1,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,8,false,.25f,-1,1e-6f,0,true);
        var second=first with { Request=first.Request with { StorageLinearIndex=1,Ticket=12 } };
        // Act
        Assert.True(backend.TryEnqueue(first));Assert.True(backend.TryEnqueue(second));Assert.Equal(0,claims);
        // Act
        Assert.False(backend.TryDequeueResult(out _));Assert.Equal(2,claims);
        GpuTestFence.WaitForGpuOrSkip("World probe backend completion");
        // Act
        Assert.True(backend.TryDequeueResult(out var complete));Assert.True(complete.Success);Assert.Equal(first.Request,complete.Request);
        // Assert
        Assert.All(complete.AtlasSamples,sample=>Assert.Null(sample.SurfaceHit));
        if(change==0)room.Geometry.Dirty(); else if(change==1)room.DependencyRevision++;else geometryAvailable=false;
        // Act
        Assert.True(backend.TryDequeueResult(out var rejected));Assert.False(rejected.Success);Assert.Equal(second.Request,rejected.Request);
        // Assert
        Assert.Empty(rejected.AtlasSamples);
    }

    /// <summary>Uploaded geometry still produces deferred hit descriptors when the Surface Cache is unavailable.</summary>
    [Fact]
    public void MissingCacheDoesNotStopGeometryTracing()
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        using var assets=new BinaryShaderApiFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>null,(_,_)=>true,new VanillaGraphicsExpanded.Tests.Support.Lighting.UnexpectedWorldProbeTraceScene(),_=>true);
        var item=new LumOnWorldProbeTraceWorkItem(1,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,8,false,.25f,-1,1e-6f,0,true,-1);
        // Act
        Assert.True(backend.TryEnqueue(item));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("World probe geometry without cache");
        // Act
        Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
        // Assert
        Assert.All(result.AtlasSamples,sample=>{Assert.NotNull(sample.SurfaceHit);Assert.True(sample.AlphaEncodedDistSigned>0);});
    }
    #endregion

    #region Production renderer
    /// <summary>Default L0 compute publication fills the real atlas without CPU ray-tracing worker reads.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void DefaultComputePathPublishesWithoutCpuTraversal()
    {
        // Arrange
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        // Assert
        Assert.True(runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing);
        // Act
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f,maximumFrames:256);
        // Assert
        Assert.Equal(0,runtime.World.WorkerReads); Assert.Equal(0,runtime.World.VanillaLightReads);
        Assert.True(runtime.WorldConfidence>0);
    }
    #endregion
    #endregion

}
