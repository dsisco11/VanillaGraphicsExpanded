using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests the explicit scene-linear to display boundary independently from lighting composition.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrDisplayResolveTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared graphics context.</summary>
    public PbrDisplayResolveTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Display transfer
    /// <summary>HDR channels undergo one Reinhard and sRGB conversion; negative values clamp and alpha survives.</summary>
    [Fact]
    public void GeometryConvertsHdrExactlyOnceAndSkyPassesThrough()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(3, 1, PixelInternalFormat.Rgba16f,
            new[] { 0f, 1f, 4f, .5f, -.5f, .125f, 16f, 1f, .125f, .5f, .75f, .25f });
        using var depth = TestFramework.CreateTexture(3, 1, PixelInternalFormat.R32f, new[] { .5f, .9999993f, 1f });
        using var output = TestFramework.CreateTestGBuffer(3, 1, PixelInternalFormat.Rgba8);
        output.BindWithViewport();
        using (program.UseScope())
        {
            program.PrimaryScene = scene.TextureId;
            program.PrimaryDepth = depth.TextureId;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        float[] actual = output[0].ReadPixels();
        float[] expected = [Transfer(0, 4), Transfer(1, 4), Transfer(4, 4), .5f,
            Transfer(-.5f, 16), Transfer(.125f, 16), Transfer(16, 16), 1, .125f, .5f, .75f, .25f];
        for (int i = 0; i < expected.Length; i++) Assert.InRange(actual[i], expected[i] - .0041f, expected[i] + .0041f);
        Assert.True(actual[2] > actual[1]);
    }
    /// <summary>Night, twilight and daylight retain linear RGB ratios under the common exposure and shoulder.</summary>
    [Theory]
    [InlineData(.00001f)]
    [InlineData(.01f)]
    [InlineData(1f)]
    [InlineData(100f)]
    [InlineData(10000f)]
    public void SharedExposurePreservesHueAcrossRadianceScales(float scale)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f,
            new[] { scale, scale * .25f, scale * .0625f, .37f });
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { .5f });
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        output.BindWithViewport();
        using (program.UseScope())
        {
            program.PrimaryScene = scene.TextureId;
            program.PrimaryDepth = depth.TextureId;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        float[] actual = output[0].ReadPixels();
        float[] ratios = [1, .25f, .0625f];
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(actual[channel], 0f, 1f);
            float expected = Transfer(scale * ratios[channel], scale);
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
            Assert.InRange(Decode(actual[channel]) / Decode(actual[0]), ratios[channel] - .0001f, ratios[channel] + .0001f);
        }
        Assert.InRange(actual[3], .36999f, .37001f);
    }
    #endregion

    #region Independent transfer reference
    /// <summary>Evaluates the specified display operator on CPU, with no shader helper reuse.</summary>
    private static float Transfer(float radiance, float peak)
    {
        float positive = Math.Max(0, radiance);
        float mapped = positive / (1 + peak);
        return mapped <= .0031308f ? mapped * 12.92f : 1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f;
    }
    /// <summary>Decodes display values to verify linear hue independently of sRGB encoding.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
}
