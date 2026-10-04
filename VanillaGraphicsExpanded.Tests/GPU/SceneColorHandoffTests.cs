using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the selectable scene handoff without asserting whole-frame HDR activation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorHandoffTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>HDR handoff preserves geometry and sky radiance and legacy mode still resolves geometry only.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ProductionHandoffSelectsColorConvention(int linearScene)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(2, 1, PixelInternalFormat.Rgba32f,
            new[] { 8f, 4f, 2f, .25f, 8f, 4f, 2f, .75f });
        using var depth = TestFramework.CreateTexture(2, 1, PixelInternalFormat.R32f, new[] { .5f, 1f });
        using var output = TestFramework.CreateTestGBuffer(2, 1, PixelInternalFormat.Rgba32f);
        program.PrimaryScene = scene.TextureId;
        program.PrimaryDepth = depth.TextureId;
        program.SceneLinear = linearScene;
        TestFramework.RenderQuadTo(program, output);
        float[] pixels = output[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            float radiance = 8f / (1 << channel);
            float expected = linearScene == 1 ? radiance : Encode(radiance / 9f) - .4921875f / 255f;
            Assert.InRange(pixels[channel], expected - .00001f, expected + .00001f);
            Assert.Equal(radiance, pixels[channel + 4]);
        }
        Assert.Equal(.25f, pixels[3]);
        Assert.Equal(.75f, pixels[7]);
    }
    /// <summary>Premultiplied particles compose once over geometry and sky only on the linear handoff.</summary>
    [Theory]
    [InlineData(0, 0f, 1)]
    [InlineData(0, .5f, 1)]
    [InlineData(0, 1f, 1)]
    [InlineData(1, 0f, 1)]
    [InlineData(1, .5f, 1)]
    [InlineData(1, 1f, 1)]
    [InlineData(1, .5f, 0)]
    public void ParticleLayerComposesOnceOverTransportedScene(int linearScene, float coverage, int layerEnabled)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(2, 1, PixelInternalFormat.Rgba32f,
            new[] { 8f, 4f, 2f, .25f, 8f, 4f, 2f, .75f });
        using var depth = TestFramework.CreateTexture(2, 1, PixelInternalFormat.R32f, new[] { .5f, 1f });
        using var layer = DynamicTexture2D.Create(2, 1, PixelInternalFormat.Rgba32f);
        layer.UploadDataImmediate(Enumerable.Repeat(new[] { 4f * coverage, 8f * coverage, 2f * coverage, coverage }, 2).SelectMany(pixel => pixel).ToArray());
        using var output = TestFramework.CreateTestGBuffer(2, 1, PixelInternalFormat.Rgba32f);
        program.PrimaryScene = scene.TextureId;
        program.PrimaryDepth = depth.TextureId;
        program.ParticleLayer = layer;
        program.ParticleLayerEnabled = layerEnabled;
        program.SceneLinear = linearScene;
        TestFramework.RenderQuadTo(program, output);
        float[] actual = output[0].ReadPixels();
        float[] background = [8, 4, 2];
        float[] particles = [4, 8, 2];
        for (int pixel = 0; pixel < 2; pixel++)
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = linearScene != 0 ? background[channel] * (1 - coverage * layerEnabled) + particles[channel] * coverage * layerEnabled
                : pixel == 1 ? background[channel] : Encode(background[channel] / 9f) - .4921875f / 255f;
            Assert.InRange(actual[pixel * 4 + channel], expected - .00001f, expected + .00001f);
        }
        Assert.Equal(.25f, actual[3]);
        Assert.Equal(.75f, actual[7]);
    }
    #endregion

    #region Private
    /// <summary>Computes the specified sRGB transfer independently of production GLSL.</summary>
    private static float Encode(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
    #endregion
}
