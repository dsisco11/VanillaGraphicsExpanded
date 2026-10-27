using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using Newtonsoft.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Connects calibrated authoring to actual metering, temporal output and final local/global display composition.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraExposureEV100PipelineTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Enumerates bounded dark/daylight scenes, manual/automatic policy, local correction and two frozen storage scales.</summary>
    public static IEnumerable<object[]> EquivalentCases()
    {
        foreach (bool automatic in new[] { false, true })
        foreach (bool local in new[] { false, true })
        foreach (float nits in new[] { .0001f, 5730f })
        foreach (float scale in new[] { 1f, .125f })
            yield return [automatic, local, nits, scale];
    }

    /// <summary>Legacy and current identities produce identical physical gain, calibrated metadata and bounded display output.</summary>
    [Theory]
    [MemberData(nameof(EquivalentCases))]
    public void EquivalentAuthoringReachesSameFinalImage(bool automatic, bool local, float nits, float scale)
    {
        EnsureShaderTestAvailable();
        var legacy = JsonConvert.DeserializeObject<CameraExposureSettings>("{\"ManualEV\":1.5,\"MinEV\":-20,\"MaxEV\":2,\"Compensation\":0.5}")!;
        legacy.Enabled = automatic; legacy.LowPercentile = 0; legacy.HighPercentile = 1;
        var authored = new CameraExposureSettings { Enabled = automatic, ManualEV100 = -1.5f, MinEV100 = -2,
            MaxEV100 = 20, Compensation = .5f, LowPercentile = 0, HighPercentile = 1 };
        var settings = authored.Snapshot();
        Assert.Equal(legacy.Snapshot(), settings);
        using var lighting = new TestFrameLightingPublication(); lighting.Capture(scale);
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [nits * scale, nits * scale, nits * scale, 1]);
        using var zero = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0, 0, 0, 0]);
        using var targets = new CameraExposureTargets();
        using var frame = TestFrameCamera.CreateIdentity(1, 1);
        var histogram = Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adaptation = Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        histogram.FrameLightingInputs = lighting.Buffer; histogram.SceneRadiance = source;
        histogram.Histogram = targets.Histogram; histogram.Capture(settings, true, local);
        adaptation.Histogram = targets.Histogram; adaptation.PreviousExposure = targets.Exposure;
        adaptation.OutputExposure = targets.WriteTarget; adaptation.FrameInputs = frame; adaptation.Capture(settings, true, local);
        Assert.True(CameraExposureDispatch.TryRun(histogram, adaptation)); targets.Publish();
        float[] state = ExposureTestStorage.Read<float>(targets.Exposure);
        double sceneEV100 = Math.Log2(nits / .18);
        double desired = automatic ? -sceneEV100 + .5 : 2;
        double gain = Math.Clamp(desired, -20, 2);
        Assert.InRange(Math.Abs(state[0] - gain), 0, .0003);
        Assert.InRange(Math.Abs(state[1] - sceneEV100), 0, .0003);
        Assert.InRange(Math.Abs(state[2] - Math.Log2(nits)), 0, .0003);
        Assert.Equal(1, state[3]);
        Assert.InRange(Math.Abs(state[4] - (-sceneEV100 + .5)), 0, .0003);
        Assert.Equal(-sceneEV100 + .5 > 2 || -sceneEV100 + .5 < -20 ? 1 : 0, state[5]);
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = lighting.Buffer;
        var localPolicy = new LocalExposureParameters(local, 1, 1, 1, .5f, .5f, 4, 4, false);
        var final = Programs.Create<FinalDisplayShaderProgram>(p => p.LocalExposureEnabled = local);
        final.FrameInputs = frame; final.FrameLightingInputs = lighting.Buffer; final.SceneImage = source;
        final.BloomImage = zero; final.ShaftImage = zero; final.ExposureBuffer = targets.Exposure;
        if (local) final.LocalBase = owner.Render(Programs.Api, source, localPolicy);
        Vector4 controls = new(1, 1, 1, 0);
        using var grading = new ColorGradingLut(); final.GradingLookup = grading.Prepare(Programs.Api, controls);
        double pivot = Math.Log2(.18) + (automatic ? .5 : 0);
        final.Capture(false, new(controls, Vector4.Zero, Vector4.Zero), new(settings.ManualLogGain + .5f, automatic ? 1 : 0, 0, (float)pivot));
        final.CaptureLocal(localPolicy);
        using var display = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var draw = new PostprocessDraw(); var pipeline = draw.Prepare(final,display.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope22=commands.EnterBoundary(new("Tests.EV100.Final",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope22.Run(()=>draw.Submit(commands, pipeline,display.PostprocessPass(), final.CaptureBindings()));
}
        double correction = local ? Math.Clamp(.5 * (pivot - gain - Math.Log2(nits)), -4, 4) : 0;
        float exposed = (float)(nits * Math.Pow(2, gain + correction));
        float expected = FilmicGradingReference.Quantize(FilmicGradingReference.Lookup(new(exposed), controls).X);
        float[] actual = display[0].ReadPixels();
        for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(actual[c] - expected), 0, 1.001 / 255);
        Assert.Equal(1, actual[3]); Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.Equal(new[] { nits * scale, nits * scale, nits * scale, 1 }, source.ReadPixels());
    }
    #endregion
}
