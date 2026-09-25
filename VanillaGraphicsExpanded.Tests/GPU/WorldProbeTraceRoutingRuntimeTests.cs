using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises routing lifetime changes through the registered world-probe renderer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeTraceRoutingRuntimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    /// <summary>Switching either direction retires pending queries while retaining atlas values and allowing replacement work.</summary>
    [Fact]
    public void FlagChangesRetirePendingWorkWithoutClearingDisplayedAtlas()
    {
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(false);
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        var resources=runtime.WorldBuffers.Resources;
        var config=runtime.Cache.Config.WorldProbeClipmap;
        int traceBudget=config.TraceMaxProbesPerFrame,uploadBudget=config.UploadBudgetBytesPerFrame;
        foreach(bool enabled in new[]{false,true})
        {
            runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
            float[] retained=runtime.WorldPixels();
            config.TraceMaxProbesPerFrame=0;config.UploadBudgetBytesPerFrame=0;
            config.EnableGpuTracing=enabled;
            for(int frame=0;frame<3;frame++)
            {
                runtime.Frame();
                Assert.Same(resources,runtime.WorldBuffers.Resources);
                Assert.False(runtime.HasPendingSurfaceLightingQueries);
                Assert.Equal(retained,runtime.WorldPixels());
            }
            config.TraceMaxProbesPerFrame=traceBudget;config.UploadBudgetBytesPerFrame=uploadBudget;
            runtime.RunUntil(()=>runtime.HasPendingSurfaceLightingQueries);
            Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f);
        }
    }
}
