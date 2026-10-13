using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual half-float postprocess storage and physical display quantities on tiny fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PostprocessingPhotometricTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Maximum admitted display exposure and reciprocal storage scale keep three half-float inputs finite.</summary>
    [Theory]
    [InlineData(24f, 1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void FinalExposureContainsExceptionalCameraAndMaximumStoredLight(float ev, float expected)
    {
        EnsureShaderTestAvailable();
        using var publication = new TestFrameLightingPublication(); publication.Capture(LightingStorage.MinimumFrameScale);
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [65504f, 65504f, 65504f, 1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var exposure = ExposureTestStorage.Create(ev);
        using var draw = new PostprocessDraw();
        var final = Programs.Create<FinalDisplayShaderProgram>(); final.FrameLightingInputs = publication.Buffer;
        final.FrameInputs = camera; final.SceneImage = source; final.BloomImage = source; final.ShaftImage = source; final.ExposureBuffer = exposure;
        final.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(0, 1, 0, 0));
        Submit(draw, final, target);
        var actual = target[0].ReadPixels();
        Assert.Equal(new[] { expected, expected, expected, 1 }, actual);
    }

    /// <summary>Luma preparation and radial shafts narrow only finite contained RGB while preserving non-light metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LumaAndRadialShaftsContainHalfStorage(bool shafts)
    {
        EnsureShaderTestAvailable();
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, .25f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
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
        Submit(draw, program, target);
        Assert.Equal(new[] { 65504f, 65504f, 0f, .25f }, target[0].ReadPixels());
    }

    /// <summary>NaN lighting publishes neutral RGB rather than retaining prior-frame glare under an obsolete scale.</summary>
    [Fact]
    public void InvalidBloomSampleReplacesPriorGlareWithNeutralFallback()
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [float.NaN, 1f, 1f, 1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var draw = new PostprocessDraw();
        target.BindAndClear(.25f, .5f, .75f, .125f);
        shader.SourceImage = source; shader.SecondaryImage = source;
        shader.Capture(new(0, 0, 1, 0), Vector4.Zero);
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
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [input, input, input, .37f]);
        using var secondary = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [48000f, 48000f, 48000f, .7f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var exposure = ExposureTestStorage.Create(0);
        using var draw = new PostprocessDraw();
        shader.SourceImage = source; shader.SecondaryImage = secondary; shader.ExposureBuffer = exposure;
        if (mode is 2 or 3) shader.CaptureGaussian(4, mode == 3);
        else shader.Capture(new(0, 0, mode, mode == 4 ? 1 : 0), mode == 4 ? Vector4.One : new(1, 0, 1, 0));
        Submit(draw, shader, target);
        var actual = target[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(float.IsFinite(actual[channel]));
            Assert.InRange(actual[channel], expected == 0 ? 0 : expected - 32, expected);
        }
        Assert.Equal(1, actual[3]);
        Assert.Equal(input, source.ReadPixels()[0]);
    }

    /// <summary>Exposed highlight selection retains the same physical RGB at different frozen storage scales.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.25f)]
    public void BloomThresholdAndFinalDisplayDecodeTheSamePhysicalScale(float scale)
    {
        EnsureShaderTestAvailable();
        using var publication = new TestFrameLightingPublication(); publication.Capture(scale);
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [8 * scale, 4 * scale, 2 * scale, .37f]);
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var glare = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var exposure = ExposureTestStorage.Create(-2);
        using var draw = new PostprocessDraw();
        var bloom = Programs.Create<BloomShaderProgram>(); bloom.FrameLightingInputs = publication.Buffer;
        bloom.SourceImage = source; bloom.SecondaryImage = zero; bloom.ExposureBuffer = exposure;
        bloom.Capture(new(0, 0, 0, 1), new(1, 0, .5f, 12));
        Submit(draw, bloom, glare);
        Assert.Equal(new[] { 4 * scale, 2 * scale, scale, 1 }, glare[0].ReadPixels());
        var final = Programs.Create<FinalDisplayShaderProgram>(); final.FrameLightingInputs = publication.Buffer;
        final.FrameInputs = camera; final.SceneImage = source; final.BloomImage = glare[0];
        final.ShaftImage = zero; final.ExposureBuffer = exposure;
        final.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(12, 1, 0, 0));
        Submit(draw, final, target);
        var actual = target[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            // Scene plus generated glare has physical peak12; exposure=-2 gives peak3.
            float linear = (3f / (1 << channel)) / 4;
            float encoded = 1.055f * MathF.Pow(linear, 1f / 2.4f) - .055f;
            float expected = MathF.Floor(Math.Clamp(encoded - .4921875f / 255, 0, 1) * 255 + .5f) / 255;
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
        Assert.Equal(new[] { 8 * scale, 4 * scale, 2 * scale, .37f }, source.ReadPixels());
    }
    #endregion

    #region Private
    /// <summary>Uses the owning prepared postprocess draw and restored engine command boundary.</summary>
    private static void Submit(PostprocessDraw draw, GpuProgram shader, GpuFramebuffer target)
    {
        var pipeline = draw.Prepare(shader, target);
        Assert.True(GraphicsCommandContext.TryRun("Tests.PhotometricPost", [pipeline], true,
            commands => draw.Submit(commands, pipeline, target)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
