using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares production water optical helpers to independent linear and photon-direction predictions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterTransportTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>HDR radiance survives confidence blending, colored attenuation and entry/exit world-space scattering.</summary>
    [Fact]
    public void LinearCompositionAndWaterPhotonDirectionsMatchReference()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<WaterTransportShaderProgram>();
        using var aerial = DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        using var attenuation = DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        aerial.UploadDataImmediate(new float[] {1,2,3,0, 0,0,0,0},0,0,0,1,2,1,0);
        attenuation.UploadDataImmediate([.2f,.4f,.6f,0],0,0,0,1,1,1,0);
        using var neutralOcclusion = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [0f]);
        program.LightShaftOcclusion = neutralOcclusion;
        program.AerialRadiance = aerial; program.AerialAttenuation = attenuation;
        using var target = CreateMRTRenderTarget(4, 13, PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] pixels = target[0].ReadPixels();
        double[] weights = [0, .25, .5, 1], fallback = [2,4,8], receiver = [16,8,4];
        double[] background = [8,4,2], extinction = [.3,.5,.7], scattering = [.2,.3,.4];
        for (int x = 0; x < 4; x++)
        {
            double alpha = .25 * (1 - weights[x]) + weights[x];
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = (fallback[channel] * .25 * (1 - weights[x]) + receiver[channel] * weights[x]) / alpha;
                Close(expected, pixels[x * 4 + channel]);
                double transmission = Math.Exp(-extinction[channel] * weights[x] * 10);
                Close(background[channel] * transmission + (channel + 3) * scattering[channel] / extinction[channel]
                    * (1 - transmission), pixels[(4 + x) * 4 + channel]);
            }
            Close(alpha, pixels[x * 4 + 3]);
            // Entry follows the reverse refracted ray; exit follows the camera-side water ray.
            float refractedX = .6f / 1.333f;
            Vector3 outgoing = x % 2 == 0 ? new(-refractedX, 0, MathF.Sqrt(1 - refractedX * refractedX)) : new(-.6f, 0, .8f);
            if (x >= 2) outgoing = new(outgoing.Z, outgoing.Y, -outgoing.X);
            for (int channel = 0; channel < 3; channel++) Close(outgoing[channel], pixels[(8 + x) * 4 + channel]);
            double cosine = Vector3.Dot(-Vector3.Normalize(new(.2f,.8f,-.4f)), outgoing);
            Close((1 - .7 * .7) / (4 * Math.PI * Math.Pow(1 + .7 * .7 - 2 * .7 * cosine, 1.5)), pixels[(8 + x) * 4 + 3]);
        }
        float[] separatelyTransported = target[1].ReadPixels();
        double[] coverages = [.001,.25,1], visibility = [0,.4,1], loss = [.2,.4,.6];
        for (int row = 3; row < 12; row++)
        for (int x = 0; x < 4; x++)
        {
            double coverage = coverages[(row - 3) / 3], sky = visibility[(row - 3) % 3];
            double alpha = coverage * (1 - weights[x]) + weights[x];
            int offset = (row * 4 + x) * 4;
            // The atmospheric source is weighted once by the resulting coverage,
            // independent of whether either constituent has full transmission.
            for (int channel = 0; channel < 3; channel++)
            {
                double mixture = (fallback[channel] * coverage * (1 - weights[x]) + receiver[channel] * weights[x]) / alpha;
                double expected = mixture * (1 - loss[channel] * sky) + (channel + 1) * sky;
                Close(expected,pixels[offset + channel]);
                Close(expected,separatelyTransported[offset + channel]);
            }
            Close(alpha,pixels[offset + 3]); Close(alpha,separatelyTransported[offset + 3]);
        }
        for (int x = 0; x < 4; x++)
        for (int channel = 0; channel < 3; channel++)
        {
            double air = background[channel] * (1 - loss[channel] * weights[x]) + (channel + 1) * weights[x];
            Close(air * Math.Exp(-.1 * (channel + 1) * 2),pixels[(12 * 4 + x) * 4 + channel]);
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects nonfinite results and bounds float error relative to radiance magnitude.</summary>
    private static void Close(double expected, float actual)
    {
        Assert.True(float.IsFinite(actual));
        Assert.InRange((double)actual, expected - 2e-5 * Math.Max(1, Math.Abs(expected)), expected + 2e-5 * Math.Max(1, Math.Abs(expected)));
    }
    #endregion
}
