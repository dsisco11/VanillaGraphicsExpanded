using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises routing lifetime changes through the registered world-probe renderer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class WorldProbeTraceRoutingRuntimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API


    /// <summary>Switching from CPU queries to compute retains atlas values; both routing changes allow replacement work.</summary>
    [Fact]
    public void FlagChangesRetirePendingWorkWithoutClearingDisplayedAtlas()
    {
        // Arrange
        EnsureContextValid();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        runtime.Cache.Config.WorldProbeClipmap.EnableGpuTracing=false;
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f);
        var resources=runtime.WorldBuffers.Resources;
        var config=runtime.Cache.Config.WorldProbeClipmap;
        int traceBudget=config.TraceMaxProbesPerFrame,uploadBudget=config.UploadBudgetBytesPerFrame;
        foreach(bool enabled in new[]{true,false})
        {
            runtime.RunUntil(()=>config.EnableGpuTracing?runtime.HasPendingGpuTrace:runtime.HasPendingSurfaceLightingQueries);
            using var retained = new Assertions.GpuImageSnapshot(runtime.WorldImage);
            config.TraceMaxProbesPerFrame=0;config.UploadBudgetBytesPerFrame=0;
            config.EnableGpuTracing=enabled;
            for(int frame=0;frame<3;frame++)
            {
                // Act
                runtime.Frame();
                // Assert
                Assert.Same(resources,runtime.WorldBuffers.Resources);
                Assert.False(runtime.HasPendingSurfaceLightingQueries);
                Assert.False(runtime.HasPendingGpuTrace);
                ImageAssertions.Comparisons.CompareHistory(runtime.WorldImage, retained).AssertEqual();
            }
            config.TraceMaxProbesPerFrame=traceBudget;config.UploadBudgetBytesPerFrame=uploadBudget;
            // Act
            runtime.RunUntil(()=>config.EnableGpuTracing?runtime.HasPendingGpuTrace:runtime.HasPendingSurfaceLightingQueries);
            // Assert
            Assert.True(runtime.Energy(runtime.WorldImage)>.001f);
        }
    }
    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
