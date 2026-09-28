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
        using var scene = TestFramework.CreateTexture(2, 1, PixelInternalFormat.Rgba32f,
            new[] { scale, scale * .25f, scale * .0625f, .37f, scale, scale * .25f, scale * .0625f, .37f });
        using var depth = TestFramework.CreateTexture(2, 1, PixelInternalFormat.R32f, new[] { .5f, .5f });
        using var output = TestFramework.CreateTestGBuffer(2, 1, PixelInternalFormat.Rgba32f);
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
        actual = actual[4..8];
        // Pixel (1,0) has rank 32; undo its known positive offset before checking the pure transfer.
        for (int channel = 0; channel < 3; channel++) actual[channel] -= .5f / (64f * 255f);
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

    #region Quantized output dithering
    /// <summary>Quantization retains black and white endpoints and bypasses already-resolved sky pixels exactly.</summary>
    [Fact]
    public void EndpointsAndAlreadyResolvedSkyAreUnchanged()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        const int width = 24;
        float[] pixels = new float[width * 8 * 4];
        float[] depths = new float[width * 8];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            float value = x < 8 ? -1f : x < 16 ? 1e10f : 100.4f / 255f;
            pixels[index * 4] = pixels[index * 4 + 1] = pixels[index * 4 + 2] = value;
            pixels[index * 4 + 3] = 73f / 255f;
            depths[index] = x < 16 ? .5f : 1f;
        }
        using var scene = TestFramework.CreateTexture(width, 8, PixelInternalFormat.Rgba32f, pixels);
        using var depth = TestFramework.CreateTexture(width, 8, PixelInternalFormat.R32f, depths);
        using var output = TestFramework.CreateTestGBuffer(width, 8, PixelInternalFormat.Rgba8);
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
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            int expected = x < 8 ? 0 : x < 16 ? 255 : 100;
            for (int channel = 0; channel < 3; channel++)
                Assert.Equal(expected, (int)MathF.Round(actual[offset + channel] * 255f));
            Assert.Equal(73, (int)MathF.Round(actual[offset + 3] * 255f));
        }
    }

    /// <summary>Sub-code-value ramps occupy both neighboring codes with balanced neutral noise, unchanged alpha and deterministic output.</summary>
    [Fact]
    public void QuantizedRampIsBalancedNeutralAndDeterministic()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        const int width = 64;
        const int height = 8;
        float[] pixels = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Each tile moves one eighth of an output code, too little for undithered smooth output.
            float encoded = (100f + (x / 8 + .5f) / 8f) / 255f;
            float mapped = Decode(encoded);
            float radiance = mapped / (1f - mapped);
            int offset = (y * width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = radiance;
            pixels[offset + 3] = 73f / 255f;
        }
        using var scene = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba32f, pixels);
        using var depth = TestFramework.CreateTexture(width, height, PixelInternalFormat.R32f,
            Enumerable.Repeat(.5f, width * height).ToArray());
        using var output = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba8);
        using var preciseOutput = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba32f);
        float[] precise = Render(preciseOutput);
        float[] first = Render();
        Assert.Equal(first, Render());
        // An explicit reference matrix keeps the check independent of the shader's bit-interleaving loop.
        int[] ranks = [0,32,8,40,2,34,10,42, 48,16,56,24,50,18,58,26,
            12,44,4,36,14,46,6,38, 60,28,52,20,62,30,54,22,
            3,35,11,43,1,33,9,41, 51,19,59,27,49,17,57,25,
            15,47,7,39,13,45,5,37, 63,31,55,23,61,29,53,21];
        int previousUpperCodes = -1;
        for (int tile = 0; tile < 8; tile++)
        {
            int upperCodes = 0;
            float offsetSum = 0;
            for (int y = 0; y < height; y++)
            for (int localX = 0; localX < 8; localX++)
            {
                int offset = (y * width + tile * 8 + localX) * 4;
                int code = (int)MathF.Round(first[offset] * 255f);
                Assert.InRange(code, 100, 101);
                upperCodes += code - 100;
                float offsetCodes = precise[offset] * 255f - (100f + (tile + .5f) / 8f);
                Assert.InRange(MathF.Abs(offsetCodes), 0f, .493f);
                float expectedOffset = (ranks[y * 8 + localX] + .5f) / 64f - .5f;
                Assert.InRange(MathF.Abs(offsetCodes - expectedOffset), 0f, .0001f);
                offsetSum += offsetCodes;
                Assert.Equal(first[offset], first[offset + 1]);
                Assert.Equal(first[offset], first[offset + 2]);
                Assert.Equal(73, (int)MathF.Round(first[offset + 3] * 255f));
            }
            Assert.InRange(MathF.Abs(offsetSum / 64f), 0f, .0001f);
            Assert.True(upperCodes > previousUpperCodes);
            previousUpperCodes = upperCodes;
            // Bound final brightness error to 1/16 of one code; hardware fixed-point conversion need not be ideal rounding.
            Assert.InRange(MathF.Abs(upperCodes / 64f - (tile + .5f) / 8f), 0f, 1f / 16f);
        }

        // A second draw must reproduce the exact pattern, without temporal noise.
        float[] Render(VanillaGraphicsExpanded.Rendering.GpuFramebuffer? destination = null)
        {
            (destination ?? output).BindWithViewport();
            using (program.UseScope())
            {
                program.PrimaryScene = scene.TextureId;
                program.PrimaryDepth = depth.TextureId;
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.FramebufferSrgb);
                TestFramework.RenderQuad(program);
            }
            return (destination ?? output)[0].ReadPixels();
        }
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
