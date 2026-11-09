using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Checks actual half-float postprocess storage and physical display quantities on tiny fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PostprocessingPhotometricTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Moonless background remains representable in the real packed scene format under the assembled frame envelope.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MoonlessBackgroundSurvivesFrameEncodingAndDisplay(bool lumon)
    {
        // Arrange: Supply a moonless atmospheric background and choose the legacy or LumOn frame-encoding path.
        EnsureShaderTestAvailable();
        const float background = .0008f / MathF.PI;
        using var lights = new VgeLightsUniformBuffer();
        var atmosphere = new AtmosphereLighting(-Vector3.UnitY, Vector3.Zero, new Vector3(background),
            Vector3.Zero, Vector3.Zero, []) { HasNightSources = true, Moon = Vector3.UnitY,
            LookupStorageScale = LightingStorage.PersistentScale, CalibrationGeneration = 1,
            ReconstructionMaximumNits = background };
        float bound = FrameLightingBounds.FromPublications(atmosphere, lights, LightingCalibration.Default,
            lumon, 1, Vector3.One, LightingCalibration.EmissionReferenceNits, 0).Total;
        float scale = LightingStorage.FrameScale(256, bound);
        using var publication = new TestFrameLightingPublication(); publication.Capture(scale);
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.R11fG11fB10f,
            new[] { background * scale, background * scale, background * scale });
        float[] stored = source.ReadPixels();
        foreach (float component in stored.Take(3)) Assert.True(component > 0, $"LumOn={lumon}: scale={scale:R}, bound={bound:R}");
        using var zero = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var exposure = ExposureTestStorage.Create(8);
        using var draw = new PostprocessDraw();
        var final = Programs.Create<FinalDisplayShaderProgram>(); final.FrameLightingInputs = publication.Buffer;
        using var grading = new ColorGradingLut(); final.GradingLookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        final.FrameInputs = camera; final.SceneImage = source; final.BloomImage = zero;
        final.ShaftImage = zero; final.ExposureBuffer = exposure;
        final.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(0, 1, 0, 0));
        // Act and assert: Submit scene-linear display and require the packed moonless radiance to remain nonzero within quantization bounds.
        Submit(draw, final, target);
        float expected = FilmicGradingReference.Quantize(FilmicGradingReference.Lookup(new Vector3(stored[0] / scale * 256), new(1, 1, 1, 0)).X);
        foreach (float component in target[0].ReadTexel().Take(3)) Assert.InRange(component, expected - 1.001f / 255, expected + 1.001f / 255);
    }
    /// <summary>Maximum admitted display exposure and reciprocal storage scale keep three half-float inputs finite.</summary>
    [Theory]
    [InlineData(24f, 1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void FinalExposureContainsExceptionalCameraAndMaximumStoredLight(float ev, float expected)
    {
        // Arrange: Combine the smallest frame-lighting storage scale, maximum finite half input, and selected camera EV.
        EnsureShaderTestAvailable();
        using var publication = new TestFrameLightingPublication(); publication.Capture(LightingStorage.MinimumFrameScale);
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [65504f, 65504f, 65504f, 1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var exposure = ExposureTestStorage.Create(ev);
        using var draw = new PostprocessDraw();
        var final = Programs.Create<FinalDisplayShaderProgram>(); final.FrameLightingInputs = publication.Buffer;
        using var grading = new ColorGradingLut(); final.GradingLookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        final.FrameInputs = camera; final.SceneImage = source; final.BloomImage = source; final.ShaftImage = source; final.ExposureBuffer = exposure;
        final.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(0, 1, 0, 0));
        // Act and assert: Submit maximum stored light at each exceptional EV and verify finite contained half output.
        Submit(draw, final, target);
        var actual = target[0].ReadPixels();
        // A half-float LUT write can move a near-white code across ordered
        // quantization, and this fixture then narrows that code to half again.
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(actual[channel], expected == 0 ? 0 : expected - 1.001f / 255 - 1f / 2048, expected);
        Assert.Equal(1, actual[3]);
    }

    /// <summary>Luma preparation and radial shafts narrow only finite contained RGB while preserving non-light metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LumaAndRadialShaftsContainHalfStorage(bool shafts)
    {
        // Arrange: Feed maximum finite and nonfinite RGB through luma preparation, optionally followed by radial shafts.
        EnsureShaderTestAvailable();
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, .25f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var draw = new PostprocessDraw();
        GpuProgram program;
        if (shafts)
        {
            var shader = Programs.Create<LightShaftShaderProgram>(); shader.SourceImage = source;
            shader.VisibilityImage = source; shader.DepthImage = source; shader.FrameInputs = camera;
            shader.Capture(new(0, 0, 1, 64), Vector4.Zero, new(.5f, .5f, 1, 1)); program = shader;
        }
        else
        {
            var shader = Programs.Create<PostLumaShaderProgram>(); shader.SourceImage = source;
            shader.Capture(Vector4.Zero, Vector4.Zero); program = shader;
        }
        // Act and assert: Submit either luma or radial shafts and require finite channels to clamp without changing alpha.
        Submit(draw, program, target);
        Assert.Equal(new[] { 65504f, 65504f, 0f, .25f }, target[0].ReadPixels());
    }

    /// <summary>NaN lighting publishes neutral RGB rather than retaining prior-frame glare under an obsolete scale.</summary>
    [Fact]
    public void InvalidBloomSampleReplacesPriorGlareWithNeutralFallback()
    {
        // Arrange: Render a valid glare sample first, then replace it with a NaN source.
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [float.NaN, 1f, 1f, 1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f);
        using var draw = new PostprocessDraw();
        FramebufferTestSetup.ClearAttachments(target, .25f, .5f, .75f, .125f);
        shader.SourceImage = source; shader.SecondaryImage = source;
        shader.Capture(new(0, 0, 1, 0), Vector4.Zero);
        // Act and assert: Replace cleared prior glare with invalid lighting and require a neutral RGB fallback.
        Submit(draw, shader, target);
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, target[0].ReadPixels());
    }

    /// <summary>Reduction, Gaussian filtering and scale assembly contain finite or infinite over-range inputs before half writes.</summary>
    [Theory]
    [InlineData(0, 65504f, 65504f)]
    [InlineData(1, float.MaxValue, 65504f)]
    [InlineData(1, float.PositiveInfinity, 65504f)]
    [InlineData(1, float.NegativeInfinity, 0f)]
    [InlineData(2, float.MaxValue, 65504f)]
    [InlineData(3, float.MaxValue, 65504f)]
    [InlineData(4, 48000f, 65504f)]
    public void BloomContainsStorageBoundaries(int mode, float input, float expected)
    {
        // Arrange: Select one storage-boundary input and bloom mode, with a separate bright secondary source.
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [input, input, input, .37f]);
        using var secondary = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [48000f, 48000f, 48000f, .7f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f);
        using var exposure = ExposureTestStorage.Create(0);
        using var draw = new PostprocessDraw();
        shader.SourceImage = source; shader.SecondaryImage = secondary; shader.ExposureBuffer = exposure;
        if (mode is 2 or 3) shader.CaptureGaussian(4, mode == 3);
        else shader.Capture(new(0, 0, mode, mode == 4 ? 1 : 0), mode == 4 ? Vector4.One : new(1, 0, 1, 0));
        // Act and assert: Run the selected reduction/filter/assembly mode and check finite bounded half-float RGB and preserved source.
        Submit(draw, shader, target);
        var actual = target[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(float.IsFinite(actual[channel]));
            Assert.InRange(actual[channel], expected == 0 ? 0 : expected - 32, expected);
        }
        Assert.Equal(1, actual[3]);
        Assert.Equal(input, source.ReadTexel()[0]);
    }

    /// <summary>Exposed highlight selection retains the same physical RGB at different frozen storage scales.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.25f)]
    public void BloomThresholdAndFinalDisplayDecodeTheSamePhysicalScale(float scale)
    {
        // Arrange: Scale stored scene radiance and publish the matching frozen physical-lighting scale.
        EnsureShaderTestAvailable();
        using var publication = new TestFrameLightingPublication(); publication.Capture(scale);
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [8 * scale, 4 * scale, 2 * scale, .37f]);
        using var zero = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var glare = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var exposure = ExposureTestStorage.Create(-2);
        using var draw = new PostprocessDraw();
        var bloom = Programs.Create<BloomShaderProgram>(); bloom.FrameLightingInputs = publication.Buffer;
        bloom.SourceImage = source; bloom.SecondaryImage = zero; bloom.ExposureBuffer = exposure;
        bloom.Capture(new(0, 0, 0, 1), new(1, 0, .5f, 12));
        // Act and assert: Generate physical-scale glare, then display scene plus glare using the same frozen scale.
        Submit(draw, bloom, glare);
        Assert.Equal(new[] { 4 * scale, 2 * scale, scale, 1 }, glare[0].ReadPixels());
        var final = Programs.Create<FinalDisplayShaderProgram>(); final.FrameLightingInputs = publication.Buffer;
        using var grading = new ColorGradingLut(); final.GradingLookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        final.FrameInputs = camera; final.SceneImage = source; final.BloomImage = glare[0];
        final.ShaftImage = zero; final.ExposureBuffer = exposure;
        final.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(12, 1, 0, 0));
        Submit(draw, final, target);
        var actual = target[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            // Scene plus generated glare has physical peak12; exposure=-2 gives peak3.
            Vector3 encoded = FilmicGradingReference.Lookup(new(3, 1.5f, .75f), new(1, 1, 1, 0));
            float expected = FilmicGradingReference.Quantize(encoded[channel]);
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
        Assert.Equal(new[] { 8 * scale, 4 * scale, 2 * scale, .37f }, source.ReadPixels());
    }

    #endregion

    #region Private
/// <summary>Uses the owning prepared postprocess draw and restored engine command boundary.</summary>
    private static void Submit(PostprocessDraw draw, GpuProgram shader, GpuFramebuffer target)
    {
        var pipeline = draw.Prepare(shader,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope72=commands.EnterBoundary(new("Tests.PhotometricPost",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope72.Run(()=>draw.Submit(commands, pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
