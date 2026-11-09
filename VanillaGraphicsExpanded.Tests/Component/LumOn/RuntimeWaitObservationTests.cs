using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Preserves wait-boundary correctness checks and diagnostic context without duplicate predicate evaluation.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class RuntimeWaitObservationTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the material-isolated graphics context.</summary>
    public RuntimeWaitObservationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Wait boundaries
    /// <summary>Success retains intermediate validation, and failure retains the complete diagnostic fields.</summary>
    [Fact]
    public void ConsumerWaitValidatesOutputsAndPreservesFailureContext()
    {
        // Arrange: Start with unsubmitted outputs so the wait must observe producer publication.
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false);
        // Allocation deliberately skips submission; validate outputs only after their producer publishes.
        // Act: Exercise successful, exhausted, and corrupted-output waits at their observation boundary.
        runtime.RunUntil(() => runtime.Screen.HasPublishedIndirect);
        int calls = 0;
        runtime.RunUntil(() => { calls++; return true; }, maximumFrames: 0);
        // Assert: One boundary observation suffices and failures retain the full diagnostic context.
        Assert.Equal(1, calls);
        // Act: Exercise exhaustion, then corrupt each intermediate output while a predicate reports success.
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => runtime.RunUntil(() => false, maximumFrames: 0));
        foreach (string field in new[] { "frames=", "workerReads=", "final=", "world=", "worldConfidence=", "trace=", "filter=", "gather=", "anchors=", "pending=", "programs=", "logs=" })
            Assert.Contains(field, failure.Message);

        // Assert: A predicate reporting success must not bypass finite checks on intermediate outputs.
        GpuTexture[] intermediates = [runtime.Screen.ScreenProbeAtlasHistoryTex!, runtime.Screen.ScreenProbeAtlasFilteredTex!, runtime.Screen.IndirectHalfTex!];
        foreach (var texture in intermediates)
        {
            using var original=new GpuImageSnapshot(texture);
            float[] invalid=texture.ReadPixelsRegion(0,0,1,1);
            invalid[0] = float.NaN;
            try
            {
                texture.UploadDataImmediate(invalid,0,0,1,1);
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => runtime.RunUntil(() => true, maximumFrames: 0));
            }
            finally
            {
                var region=new RHITextureRegion(new(0),0,0,0,texture.Width,texture.Height,1);
                RHICommandContext.Current.CopyTexture(original.Texture,region,texture,region);
            }
        }
    }

    /// <summary>Cache waits evaluate each boundary once and retain producer diagnostics on budget exhaustion.</summary>
    [Fact]
    public void CacheWaitUsesSingleBoundaryObservation()
    {
        // Arrange: Use a real cache runtime and count the predicate calls made by each wait.
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        int calls = 0;
        // Act: Check success at the initial boundary, after one frame, and on budget exhaustion.
        runtime.RunUntil(() => { calls++; return true; }, maximumFrames: 0);
        // Assert: Each boundary is observed once and exhaustion reports the producer state.
        Assert.Equal(1, calls);
        calls = 0;
        // Act: Observe a success after one frame, then an exhausted wait at the initial boundary.
        runtime.RunUntil(() => ++calls == 2, maximumFrames: 1);
        Assert.Equal(2, calls);
        calls = 0;
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => runtime.RunUntil(() => { calls++; return false; }, maximumFrames: 0));
        Assert.Equal(1, calls);
        foreach (string field in new[] { "Feedback:", "Relight:", "Sources:", "Geometry:", "Executed:", "Logs:" })
            Assert.Contains(field, failure.Message);
    }
    #endregion
    #endregion
}
