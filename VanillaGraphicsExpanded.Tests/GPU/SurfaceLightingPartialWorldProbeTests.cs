using System.Reflection;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises partial publication and bounded retries through registered CPU, GPU-query and atlas owners.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingPartialWorldProbeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Partial runtime publication
    /// <summary>A ready subset can publish below the original complete-batch cost without exceeding the upload budget.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void PartialUploadFitsBelowOriginalAdmissionCost()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=2095;
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f);
        int published=(int)runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.WorldImage, channelMask: 8).Nonzero;
        Assert.InRange(published,1,63);
        Assert.True(48+(published<<5)<=2095);
    }

    /// <summary>Unavailable pages cannot discard ready directions, and retries query only still unresolved descriptors.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void PartialCachePagesPublishAndRetryWithoutDiscardingReadyDirections()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        var observed=new Dictionary<LumOnWorldProbeUpdateRequest,System.Collections.Immutable.ImmutableArray<LumOnWorldProbeAtlasSample>>();
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
            return retriedSubset && (int)runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.WorldImage, channelMask: 8).Nonzero>0;
        });
        using var partial = new Assertions.GpuImageSnapshot(runtime.WorldImage);
        int validBefore=(int)runtime.Cache.ImageAssertions.Statistics.Inspect(partial.Texture, channelMask: 8).Nonzero;
        Assert.True(runtime.Energy(partial.Texture)>.001f);
        // Permanently unavailable pages exhaust bounded retries without erasing the completed directions.
        for(int i=0;i<16;i++)
        {
            runtime.Frame();
            Assert.All(Pending(runtime.WorldRenderer),r=>Assert.InRange(r.SurfaceRetryCount,0,3));
            runtime.Cache.ImageAssertions.Comparisons.CompareHistory(runtime.WorldImage, partial,
                channelMask: 8, retainNonzero: true).AssertEqual();
        }
        provider.Withhold=false;
        runtime.RunUntil(()=>(int)runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.WorldImage, channelMask: 8).Nonzero>validBefore);
        Assert.Equal(0,runtime.World.VanillaLightReads);
    }
    #endregion

    #region History lifetime
    /// <summary>A submitted bright query cannot publish after source readiness is withdrawn, without replacing the cache generation.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void PendingQueryRejectsSourceInvalidationWithoutGlobalCacheReplacement()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.GeometryAvailable=false;
        runtime.Cache.InvalidateGeometry();
        runtime.Frame();
        Assert.True(runtime.Cache.TryGetLighting(out var suspended));
        Assert.Equal(before.DependencyRevision,suspended.DependencyRevision);
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
        runtime.Cache.GeometryAvailable=true;
        runtime.Cache.InvalidateGeometry();
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f);
    }

    /// <summary>An unavailable engine accessor cannot postpone already queued slot retirement past the frame callback.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void MissingWorldAccessorStillFlushesQueuedHistory()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);runtime.Frame();
        var resources=runtime.WorldBuffers.Resources!;
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        resources.QueueClearLocalBox(0,new(),new(),new(3,3,3));
#if DEBUG
        long dispatches=resources.InvalidationDispatchCount;
#endif
        runtime.WorldAccessorAvailable=false;
        runtime.Frame();
        Assert.Equal(0,resources.PendingInvalidationCount);
#if DEBUG
        Assert.Equal(dispatches+1,resources.InvalidationDispatchCount);
#endif
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
    }

    /// <summary>Real scheduler anchor events clear introduced physical slots while preserving overlap in either direction.</summary>
    [Theory]
    [InlineData(4,48)] [InlineData(-4,48)] [InlineData(20,0)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void AnchorShiftClearsReusedSlotsAndPreservesOverlappingDirections(int delta,int remaining)
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);runtime.Frame();
        var resources=runtime.WorldBuffers.Resources!;
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        var scheduler=(LumOnWorldProbeScheduler)typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.WorldRenderer)!;
        scheduler.UpdateOrigins(new Vintagestory.API.MathTools.Vec3d(4+delta,36,6),4);
        resources.FlushHistoryInvalidation();
        Assert.Equal(remaining<<6,(int)runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 1, channelMask: 8).ConstantMatches);
        var zeros = runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0);
        var ones = runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 1);
        Assert.Equal(runtime.WorldImage.Width * runtime.WorldImage.Height * runtime.WorldImage.Depth * 4,
            (int)(zeros.ConstantMatches + ones.ConstantMatches));
    }

    /// <summary>Dirty geometry removes retained directions and rejects the already queued partial retry ticket.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void GeometryDirtyClearsRetainedDirectionsAndRejectsPendingRetry(bool explicitFlush)
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        using var provider=new SelectiveSurfaceLightingProvider(runtime.Cache.LightingProvider);
        runtime.WorldRenderer.SetSurfaceLightingProvider(provider,runtime.Cache.Geometry);
        runtime.RunUntil(()=>runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.WorldImage).Positive > 0 && Pending(runtime.WorldRenderer).Any(r=>r.SurfaceRetryCount>0));
        var pending=Pending(runtime.WorldRenderer).ToArray();
        var scheduler=(LumOnWorldProbeScheduler)typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.WorldRenderer)!;
        VanillaGraphicsExpanded.Numerics.Vector3d min=new(-100,-100,-100),max=new(100,100,100);
        scheduler.MarkDirtyWorldAabb(0,new(min.X,min.Y,min.Z),new(max.X,max.Y,max.Z),4);
        typeof(LumOnWorldProbeUpdateRenderer).GetMethod("ClearDirtyProbeHistory",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(runtime.WorldRenderer,[0,min,max,4d]);
        Assert.All(pending,r=>Assert.False(scheduler.IsCurrent(r.Request)));
        Assert.True(runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.WorldImage).Positive > 0);
        if(explicitFlush)
        {
            runtime.WorldBuffers.Resources!.FlushHistoryInvalidation();
            runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
        }
        runtime.Frame();
        runtime.Cache.ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0).AssertEqual();
    }
    #endregion

    #region Query observation
    /// <summary>Inspects admitted descriptors without polling GPU fences or altering the production scheduling lifecycle.</summary>
    private static IReadOnlyList<LumOnWorldProbeTraceResult> Pending(LumOnWorldProbeUpdateRenderer renderer)
        => (IReadOnlyList<LumOnWorldProbeTraceResult>)(typeof(LumOnWorldProbeUpdateRenderer)
            .GetField("pendingSurfaceResults",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!);
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
