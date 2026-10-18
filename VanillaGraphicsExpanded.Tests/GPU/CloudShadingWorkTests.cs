using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records matched trace costs and physical output differences using independently frozen shader assets.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudShadingWorkTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Alternates warmed production trace passes without imposing timing or approximation-quality gates.</summary>
    [Fact]
    public void MatchedShadingCostsAndTransportDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_SHADING_MEASUREMENT") != "1")
            Assert.Skip("Set VGE_RUN_CLOUD_SHADING_MEASUREMENT=1 and VGE_CLOUD_SHADING_BASELINE_ASSETS to frozen built assets.");
        EnsureContextValid();
        output.WriteLine("CLOUD_SHADING_DEVICE="+JsonSerializer.Serialize(new {fixture.GLRenderer,fixture.GLVersion}));
        string baselineRoot = Environment.GetEnvironmentVariable("VGE_CLOUD_SHADING_BASELINE_ASSETS")
            ?? throw new InvalidOperationException("A frozen baseline asset root is required.");
        Assert.True(Directory.Exists(baselineRoot), baselineRoot);
        using var baselinePrograms = new ComponentShaderPrograms(baselineRoot);
        using var currentPrograms = new ComponentShaderPrograms();
        var settings = CloudVolumeSettings.Default with { StopTransmittance = 0 };
        using var baseline = new CloudReconstructionScene(32, 32, baselinePrograms, false, 81, settings: settings);
        using var current = new CloudReconstructionScene(32, 32, currentPrograms, false, 81, settings: settings);
        using var timer = GpuTimerQuery.Create("Tests.Cloud.Shading.Elapsed");

        // Identical owners, inputs and schedules isolate the selected built shader assets.
        foreach (string scene in new[] { "homogeneous", "detail-lit", "terrain", "particle-within", "underwater", "aerial", "cloud-edge", "changing-fog" })
        {
            string inputScene=scene=="changing-fog"?"underwater":scene;
            baseline.Capture(0, .2f, 4, 1, 0, inputScene);
            current.Capture(0, .2f, 4, 1, 0, inputScene);
            if(scene=="aerial") {baseline.SetAerialDistanceRamp(20,.8f);current.SetAerialDistanceRamp(20,.8f);}
            if(scene=="changing-fog") {baseline.SetLocalFogDensity(.002f);current.SetLocalFogDensity(.002f);}
            baseline.SetDirectionalLights(Vector3.Normalize(new(1, .05f, 0)), -Vector3.UnitY, 3, 30, 3);
            current.SetDirectionalLights(Vector3.Normalize(new(1, .05f, 0)), -Vector3.UnitY, 3, 30, 3);
            for (int warm = 0; warm < 3; warm++) { baseline.RenderTrace(0); current.RenderTrace(0); }
            var baselineTimes = new List<double>();
            var currentTimes = new List<double>();
            double baselineSubmit = 0, currentSubmit = 0;
            for (int pair = 0; pair < 7; pair++)
            {
                // Reverse the order each pair rather than attributing temporal drift to one candidate.
                foreach (bool old in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    var measured = Measure(old ? baseline : current, timer);
                    (old ? baselineTimes : currentTimes).Add(measured.Device);
                    if (old) baselineSubmit += measured.Submit; else currentSubmit += measured.Submit;
                }
            }
            float[][] before = baseline.ReadTraceRecord(), after = current.ReadTraceRecord();
            int baselineOccupied=Enumerable.Range(0,before[0].Length/4).Count(pixel=>before[0][pixel*4+3]<1);
            int currentOccupied=Enumerable.Range(0,after[0].Length/4).Count(pixel=>after[0][pixel*4+3]<1);
            Assert.True(baselineOccupied>0&&currentOccupied>0,$"{scene}: matched trace fixtures must contain cloud support.");
            var differences = before.Zip(after, (a, b) => a.Zip(b, (x, y) => Math.Abs((double)x - y)).Max()).ToArray();
            double[]? translucentOitDifferences=null;
            if(scene=="particle-within")
            {
                // Resolve the authored translucent surface outside timing; trace alone cannot observe its blend.
                baseline.Render(0,false);current.Render(0,false);
                translucentOitDifferences=baseline.ReadDefault().Zip(current.ReadDefault(),
                    (a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
            }
            double? changedFogMaximumDifference=null;
            if(scene=="changing-fog")
            {
                baseline.SetLocalFogDensity(.02f);current.SetLocalFogDensity(.02f);
                baseline.RenderTrace(0);current.RenderTrace(0);
                before=baseline.ReadTraceRecord();after=current.ReadTraceRecord();
                changedFogMaximumDifference=before.Zip(after,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).Max();
            }
            output.WriteLine("CLOUD_SHADING_MEASUREMENT=" + JsonSerializer.Serialize(new
            {
                scene, fullWidth = 32, fullHeight = 32, traceWidth = 16, traceHeight = 16,
                baselineOccupied,currentOccupied,
                warmupPassesPerCandidate = 3, pairedSamples = 7,
                baselineGpuMilliseconds = baselineTimes, currentGpuMilliseconds = currentTimes,
                baselineSubmitMilliseconds = baselineSubmit / 7, currentSubmitMilliseconds = currentSubmit / 7,
                maximumAbsoluteDifferencePerPayloadLayer = differences,
                translucentOitDifferences,
                changedFogMaximumDifference,
                qualification = "Headless synthetic trace only; output differences are diagnostics and appearance remains user reviewed."
            }));
        }
        GlDebug.ThrowIfErrors("Cloud shading measurements");
    }
    #endregion

    #region Private
    /// <summary>Measures only trace submission and device execution, with readback outside the measured interval.</summary>
    private static (double Device, double Submit) Measure(CloudReconstructionScene scene, GpuTimerQuery timer)
    {
        timer.Begin();
        long started = Stopwatch.GetTimestamp();
        scene.RenderTrace(0);
        double submit = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        timer.End();
        return (timer.GetResultNanoseconds() / 1e6, submit);
    }
    #endregion
}
