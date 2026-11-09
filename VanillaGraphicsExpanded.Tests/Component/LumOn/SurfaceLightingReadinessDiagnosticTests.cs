using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks first-publication diagnostics against actual capture and relight callback outcomes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingReadinessDiagnosticTests : RenderTestBase
{
    #region Public API
    #region Construction
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingReadinessDiagnosticTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Publication states
    /// <summary>Instrumentation toggles retain published page readiness and dependency identity while normal refresh continues.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RuntimeDiagnosticTogglePreservesPublishedLighting()
    {
        // Arrange
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var before));
        // Act
        foreach(bool enabled in new[]{false,true,false,true})
        {
            // Act
            runtime.Config.LumOn.LumonScene.SurfaceWorkDiagnosticsEnabled=enabled;
            for(int frame=0;frame<3;frame++)
            {
                runtime.Frame();
                // Assert
                Assert.True(runtime.TryGetLighting(out var after));
                Assert.Equal(before.DependencyRevision,after.DependencyRevision);
                Assert.True(runtime.AllRequestedLightingReady());
            }
        }
        // Act
        runtime.LeaveWorld();
        // Assert
        Assert.False(runtime.TryGetLighting(out _));
    }

    /// <summary>A disabled relight budget is distinguished from failed capture or shader work.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ZeroBudget_ReportsNoSeedAttempts()
    {
        // Arrange
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.PrimeGeometry();
        runtime.RunUntil(runtime.AllRequestedCaptured);
        // Act
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string line);
        // Assert
        Assert.Contains("LSR: zero-budget", line);
        Assert.Contains("seedFail:0/0", line);
        Assert.False(runtime.TryGetLighting(out _));
    }

    /// <summary>An out-of-domain source defers GPU capture and recovers when the camera brings it into coverage.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void CaptureOutsideCoverage_DefersWithoutGpuAttemptsThenPublishesAfterRecovery()
    {
        // Arrange
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, feedbackPlaneX: 24);
        runtime.PrimeGeometry();
        // Act: let the out-of-coverage source reach the capture boundary.
        for (int frame = 0; frame < 12; frame++) runtime.Frame();
        // Assert
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string blocked);
        Assert.Contains("LSR: awaiting-capture", blocked);
        Assert.Contains("seedFail:0/0", blocked);
        runtime.Feedback.TryGetSelfCheckLine(out string capture);
        var failed = ReadCounter(capture, "captureFail");
        Assert.Equal(0, failed.Failures);
        Assert.Equal(0, failed.Attempts);
        Assert.Contains("captureReadFail:0", capture);
        Assert.False(runtime.TryGetLighting(out _));

        // Act: move the source into coverage and allow capture and seed work to complete.
        runtime.CameraX = 16;
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        // Assert
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string ready);
        Assert.Contains("LSR: published", ready);
        var seed = ReadCounter(ready, "seedFail");
        Assert.Equal(0, seed.Failures);
        Assert.True(seed.Attempts > 0, ready);
        runtime.Feedback.TryGetSelfCheckLine(out string recoveredCapture);
        Assert.Equal(0,ReadCounter(recoveredCapture,"captureFail").Failures);
        Assert.True(ReadCounter(recoveredCapture,"captureFail").Attempts>0);
    }

    /// <summary>A captured source inside coverage reports seed failures when its exterior light-query cell lies outside.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SeedOutsideCoverage_ReportsFailedSeedAttempts()
    {
        // Arrange
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, feedbackPlaneX:31);
        runtime.CameraX = 24;
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.PrimeGeometry();
        runtime.RunUntil(runtime.AllRequestedCaptured);
        // Keep the captured source x31 in [0,32), while its +X light-query cell x32 is outside.
        // Act: reposition the query and enable seed work while its neighbor remains uncovered.
        runtime.CameraX = 16;
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        // Assert
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string line);
        var seed = ReadCounter(line, "seedFail");
        Assert.True(seed.Failures > 0, line);
        Assert.Equal(seed.Attempts, seed.Failures);
        Assert.False(runtime.TryGetLighting(out _));
    }

    /// <summary>A successful cold start reports completed seed work without inventing completion failures.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void FirstPublication_ReportsSuccessfulSeedAndResetsOnLeaveWorld()
    {
        // Arrange
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions);
        runtime.PrimeGeometry();
        // Act: complete the initial publication, then inspect its published counters.
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        var producer = (LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        producer.TryGetSelfCheckLine(out string ready);
        // Assert
        Assert.Contains("LSR: published", ready);
        var seed = ReadCounter(ready, "seedFail");
        Assert.Equal(0, seed.Failures);
        Assert.True(seed.Attempts > 0, ready);
        Assert.Contains("combineFail:0 readFail:0", ready);
        // Act: leaving the world clears the diagnostic generation.
        runtime.LeaveWorld();
        producer.TryGetSelfCheckLine(out string reset);
        // Assert
        Assert.Contains("LSR: not-run", reset);
        Assert.Contains("seedFail:0/0 indirectFail:0/0", reset);
    }
    #endregion

    #endregion

    #region Private
    #region Counter parsing
    /// <summary>Reads one public diagnostic failure/attempt pair without inspecting renderer implementation fields.</summary>
    private static (long Failures, long Attempts) ReadCounter(string line, string name)
    {
        var match = Regex.Match(line, Regex.Escape(name) + @":(\d+)/(\d+)");
        Assert.True(match.Success, line);
        return (long.Parse(match.Groups[1].Value), long.Parse(match.Groups[2].Value));
    }
    #endregion
    #endregion
}
