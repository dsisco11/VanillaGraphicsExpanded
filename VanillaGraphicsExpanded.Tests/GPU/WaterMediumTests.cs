using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares production SPIR-V transport against independent homogeneous-medium analytic solutions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterMediumTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks measured absorption, path monotonicity, thin paths, zero density, zero incident light and bounded anisotropy.</summary>
    [Fact]
    public void HomogeneousTransportMatchesAnalyticReference()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<WaterMediumShaderProgram>();
        using var target = CreateRenderTarget(5, 4, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        var pixels = target[0].ReadPixels();
        double[] distances = [0, .000001, 1, 10, 100];
        double[] absorption = [.340, .0565, .00922];
        double[] extinction = [.3, .5, .7];
        double[] scattering = [.2, .3, .4];
        for (int x = 0; x < 5; x++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                AssertClose(Math.Exp(-absorption[channel] * distances[x]), pixels[x * 4 + channel]);
                AssertClose((channel + 2) * scattering[channel] / extinction[channel]
                    * (1 - Math.Exp(-extinction[channel] * distances[x])), pixels[(5 + x) * 4 + channel]);
                AssertClose(1, pixels[(10 + x) * 4 + channel]);
                AssertClose(0, pixels[(15 + x) * 4 + channel]);
                if (x > 0) Assert.True(pixels[x * 4 + channel] <= pixels[(x - 1) * 4 + channel]);
            }
            double cosine = x == 0 ? 1 : -1;
            double phase = (1 - .7 * .7) / (4 * Math.PI * Math.Pow(1 + .7 * .7 - 2 * .7 * cosine, 1.5));
            AssertClose(phase, pixels[x * 4 + 3]);
        }

    }
    #endregion

    #region Private
    /// <summary>Uses a small absolute plus relative float tolerance without accepting non-finite values.</summary>
    private static void AssertClose(double expected, float actual)
    {
        Assert.True(float.IsFinite(actual));
        Assert.InRange((double)actual, expected - 2e-6 * Math.Max(1, Math.Abs(expected)),
            expected + 2e-6 * Math.Max(1, Math.Abs(expected)));
    }
    #endregion
}
