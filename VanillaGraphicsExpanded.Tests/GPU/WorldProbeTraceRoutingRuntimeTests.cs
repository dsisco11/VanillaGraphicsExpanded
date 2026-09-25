using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises routing lifetime changes through the registered world-probe renderer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeTraceRoutingRuntimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    /// <summary>Switching from CPU queries to compute retains atlas values; both routing changes allow replacement work.</summary>
    [Fact]
    public void FlagChangesRetirePendingWorkWithoutClearingDisplayedAtlas()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        var resources=runtime.WorldBuffers.Resources;
        var config=runtime.Cache.Config.WorldProbeClipmap;
        int traceBudget=config.TraceMaxProbesPerFrame,uploadBudget=config.UploadBudgetBytesPerFrame;
        foreach(bool enabled in new[]{true,false})
        {
            runtime.RunUntil(()=>config.EnableGpuTracing?runtime.HasPendingGpuTrace:runtime.HasPendingSurfaceLightingQueries);
            float[] retained=runtime.WorldPixels();
            config.TraceMaxProbesPerFrame=0;config.UploadBudgetBytesPerFrame=0;
            config.EnableGpuTracing=enabled;
            for(int frame=0;frame<3;frame++)
            {
                runtime.Frame();
                Assert.Same(resources,runtime.WorldBuffers.Resources);
                Assert.False(runtime.HasPendingSurfaceLightingQueries);
                Assert.False(runtime.HasPendingGpuTrace);
                Assert.Equal(retained,runtime.WorldPixels());
            }
            config.TraceMaxProbesPerFrame=traceBudget;config.UploadBudgetBytesPerFrame=uploadBudget;
            runtime.RunUntil(()=>config.EnableGpuTracing?runtime.HasPendingGpuTrace:runtime.HasPendingSurfaceLightingQueries);
            Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        }
    }
}
