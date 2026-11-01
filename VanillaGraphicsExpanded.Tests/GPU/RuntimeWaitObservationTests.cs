using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Preserves wait-boundary correctness checks and diagnostic context without duplicate predicate evaluation.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class RuntimeWaitObservationTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public RuntimeWaitObservationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Wait boundaries
    /// <summary>Success retains intermediate validation, and failure retains the complete diagnostic fields.</summary>
    [Fact]
    public void ConsumerWaitValidatesOutputsAndPreservesFailureContext()
    {
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false);
        // Allocation deliberately skips submission; validate outputs only after their producer publishes.
        runtime.RunUntil(() => runtime.Screen.HasPublishedIndirect);
        int calls = 0;
        runtime.RunUntil(() => { calls++; return true; }, maximumFrames: 0);
        Assert.Equal(1, calls);
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => runtime.RunUntil(() => false, maximumFrames: 0));
        foreach (string field in new[] { "frames=", "workerReads=", "final=", "world=", "worldConfidence=", "trace=", "filter=", "gather=", "anchors=", "pending=", "programs=", "logs=" })
            Assert.Contains(field, failure.Message);

        // A predicate reporting success must not bypass finite checks on intermediate outputs.
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
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        int calls = 0;
        runtime.RunUntil(() => { calls++; return true; }, maximumFrames: 0);
        Assert.Equal(1, calls);
        calls = 0;
        runtime.RunUntil(() => ++calls == 2, maximumFrames: 1);
        Assert.Equal(2, calls);
        calls = 0;
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => runtime.RunUntil(() => { calls++; return false; }, maximumFrames: 0));
        Assert.Equal(1, calls);
        foreach (string field in new[] { "Feedback:", "Relight:", "Sources:", "Geometry:", "Executed:", "Logs:" })
            Assert.Contains(field, failure.Message);
    }
    #endregion
}
