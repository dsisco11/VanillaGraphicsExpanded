using System.Reflection;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises partial publication and bounded retries through registered CPU, GPU-query and atlas owners.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingPartialWorldProbeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Partial runtime publication
    /// <summary>A ready subset can publish below the original complete-batch cost without exceeding the upload budget.</summary>
    [Fact]
    public void PartialUploadFitsBelowOriginalAdmissionCost()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        Assert.All(runtime.WorldPixels(),value=>Assert.Equal(0,value));
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=1575;
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        int published=runtime.WorldPixels().Where((_,i)=>(i&3)==3).Count(v=>v!=0);
        Assert.InRange(published,1,63);
        Assert.True(40+24*published<=1575);
    }

    /// <summary>Unavailable pages cannot discard ready directions, and retries query only still unresolved descriptors.</summary>
    [Fact]
    public void PartialCachePagesPublishAndRetryWithoutDiscardingReadyDirections()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        var observed=new Dictionary<LumOnWorldProbeUpdateRequest,LumOnWorldProbeAtlasSample[]>();
        bool retriedSubset=false;
        runtime.RunUntil(()=>
        {
            foreach(var result in Pending(runtime.WorldRenderer))
            {
                Assert.InRange(result.SurfaceRetryCount,0,3);
                if(observed.TryGetValue(result.Request,out var previous) && result.AtlasSamples.Length<previous.Length)
                {
                    Assert.NotEmpty(result.AtlasSamples);
                    Assert.All(result.AtlasSamples,s=>Assert.Contains(s,previous));
                    Assert.All(result.AtlasSamples,s=>Assert.NotNull(s.SurfaceHit));
                    retriedSubset=true;
                }
                observed[result.Request]=result.AtlasSamples;
            }
            Assert.InRange(Pending(runtime.WorldRenderer).Sum(r=>r.AtlasSamples.Count(s=>s.SurfaceHit.HasValue)),0,4096);
            return retriedSubset && runtime.WorldPixels().Where((_,i)=>(i&3)==3).Count(v=>v!=0)>0;
        });
        var partial=runtime.WorldPixels();
        int validBefore=partial.Where((_,i)=>(i&3)==3).Count(v=>v!=0);
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(partial)>.001f);
        // Permanently unavailable pages exhaust bounded retries without erasing the completed directions.
        for(int i=0;i<16;i++)
        {
            runtime.Frame();
            Assert.All(Pending(runtime.WorldRenderer),r=>Assert.InRange(r.SurfaceRetryCount,0,3));
            var current=runtime.WorldPixels();
            for(int channel=3;channel<partial.Length;channel+=4)
                if(partial[channel]!=0) Assert.Equal(partial[channel],current[channel]);
        }
        provider.Withhold=false;
        runtime.RunUntil(()=>runtime.WorldPixels().Where((_,i)=>(i&3)==3).Count(v=>v!=0)>validBefore);
        Assert.Equal(0,runtime.World.VanillaLightReads);
    }
    #endregion

    #region History lifetime
    /// <summary>Real scheduler anchor events clear introduced physical slots while preserving overlap in either direction.</summary>
    [Theory]
    [InlineData(4,48)] [InlineData(-4,48)] [InlineData(20,0)]
    public void AnchorShiftClearsReusedSlotsAndPreservesOverlappingDirections(int delta,int remaining)
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);runtime.Frame();
        var resources=runtime.WorldBuffers.Resources!;
        resources.GetRadianceFbo().BindWithViewport();
        OpenTK.Graphics.OpenGL.GL.ClearBuffer(OpenTK.Graphics.OpenGL.ClearBuffer.Color,0,new[]{1f,1f,1f,1f});
        var scheduler=(LumOnWorldProbeScheduler)typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.WorldRenderer)!;
        scheduler.UpdateOrigins(new Vintagestory.API.MathTools.Vec3d(4+delta,36,6),4);
        Assert.Equal(remaining<<6,runtime.WorldPixels().Where((_,i)=>(i&3)==3).Count(v=>v==1));
        Assert.All(runtime.WorldPixels(),v=>Assert.True(v==0 || v==1));
    }

    /// <summary>Dirty geometry removes retained directions and rejects the already queued partial retry ticket.</summary>
    [Fact]
    public void GeometryDirtyClearsRetainedDirectionsAndRejectsPendingRetry()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        runtime.RunUntil(()=>runtime.WorldPixels().Any(v=>v>0) && Pending(runtime.WorldRenderer).Any(r=>r.SurfaceRetryCount>0));
        var pending=Pending(runtime.WorldRenderer).ToArray();
        var scheduler=(LumOnWorldProbeScheduler)typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.WorldRenderer)!;
        VanillaGraphicsExpanded.Numerics.Vector3d min=new(-100,-100,-100),max=new(100,100,100);
        scheduler.MarkDirtyWorldAabb(0,new(min.X,min.Y,min.Z),new(max.X,max.Y,max.Z),4);
        typeof(LumOnWorldProbeUpdateRenderer).GetMethod("ClearDirtyProbeHistory",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(runtime.WorldRenderer,[0,min,max,4d]);
        Assert.All(pending,r=>Assert.False(scheduler.IsCurrent(r.Request)));
        Assert.All(runtime.WorldPixels(),v=>Assert.Equal(0,v));
        runtime.Frame();
        Assert.All(runtime.WorldPixels(),v=>Assert.Equal(0,v));
    }
    #endregion

    #region Query observation
    /// <summary>Inspects admitted descriptors without polling GPU fences or altering the production scheduling lifecycle.</summary>
    private static IReadOnlyList<LumOnWorldProbeTraceResult> Pending(LumOnWorldProbeUpdateRenderer renderer)
        => (IReadOnlyList<LumOnWorldProbeTraceResult>)(typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("pendingSurfaceResults",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!);
    #endregion
}
