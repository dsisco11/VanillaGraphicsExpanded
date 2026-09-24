using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks first-publication diagnostics against actual capture and relight callback outcomes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingReadinessDiagnosticTests : RenderTestBase
{
    #region Construction
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingReadinessDiagnosticTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Publication states
    /// <summary>A disabled relight budget is distinguished from failed capture or shader work.</summary>
    [Fact]
    public void ZeroBudget_ReportsNoSeedAttempts()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 0;
        runtime.PrimeGeometry();
        runtime.RunUntil(runtime.AllRequestedCaptured);
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string line);
        Assert.Contains("LSR: zero-budget", line);
        Assert.Contains("seedFail:0/0", line);
        Assert.False(runtime.TryGetLighting(out _));
    }

    /// <summary>An out-of-domain source increments capture failures and recovers when the camera brings it into coverage.</summary>
    [Fact]
    public void CaptureOutsideCoverage_ReportsFailureThenPublishesAfterRecovery()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(feedbackPlaneX: 24);
        runtime.PrimeGeometry();
        for (int frame = 0; frame < 12; frame++) runtime.Frame();
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string blocked);
        Assert.Contains("LSR: awaiting-capture", blocked);
        Assert.Contains("seedFail:0/0", blocked);
        runtime.Feedback.TryGetSelfCheckLine(out string capture);
        var failed = ReadCounter(capture, "captureFail");
        Assert.True(failed.Failures > 0, capture);
        Assert.True(failed.Attempts >= failed.Failures, capture);
        Assert.Contains("captureReadFail:0", capture);
        Assert.False(runtime.TryGetLighting(out _));

        runtime.CameraX = 16;
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string ready);
        Assert.Contains("LSR: published", ready);
        var seed = ReadCounter(ready, "seedFail");
        Assert.Equal(0, seed.Failures);
        Assert.True(seed.Attempts > 0, ready);
        runtime.Feedback.TryGetSelfCheckLine(out string recoveredCapture);
        Assert.True(ReadCounter(recoveredCapture, "captureFail").Failures >= failed.Failures);
    }

    /// <summary>A captured page outside the current seed domain reports lighting failures separately from capture failures.</summary>
    [Fact]
    public void SeedOutsideCoverage_ReportsFailedSeedAttempts()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 0;
        runtime.PrimeGeometry();
        runtime.RunUntil(runtime.AllRequestedCaptured);
        runtime.CameraX = 24;
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 4;
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string line);
        var seed = ReadCounter(line, "seedFail");
        Assert.True(seed.Failures > 0, line);
        Assert.Equal(seed.Attempts, seed.Failures);
        Assert.False(runtime.TryGetLighting(out _));
    }

    /// <summary>A successful cold start reports completed seed work without inventing completion failures.</summary>
    [Fact]
    public void FirstPublication_ReportsSuccessfulSeedAndResetsOnLeaveWorld()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        runtime.PrimeGeometry();
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        var producer = (LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        producer.TryGetSelfCheckLine(out string ready);
        Assert.Contains("LSR: published", ready);
        var seed = ReadCounter(ready, "seedFail");
        Assert.Equal(0, seed.Failures);
        Assert.True(seed.Attempts > 0, ready);
        Assert.Contains("combineFail:0 readFail:0", ready);
        runtime.LeaveWorld();
        producer.TryGetSelfCheckLine(out string reset);
        Assert.Contains("LSR: not-run", reset);
        Assert.Contains("seedFail:0/0 indirectFail:0/0", reset);
    }
    #endregion

    #region Counter parsing
    /// <summary>Reads one public diagnostic failure/attempt pair without inspecting renderer implementation fields.</summary>
    private static (long Failures, long Attempts) ReadCounter(string line, string name)
    {
        var match = Regex.Match(line, Regex.Escape(name) + @":(\d+)/(\d+)");
        Assert.True(match.Success, line);
        return (long.Parse(match.Groups[1].Value), long.Parse(match.Groups[2].Value));
    }
    #endregion
}
