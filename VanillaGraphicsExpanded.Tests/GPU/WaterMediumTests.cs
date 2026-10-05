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
        using var target = CreateRenderTarget(5, 17, PixelInternalFormat.Rgba32f);
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
        // Coupled evaluation must preserve the analytic limit, coefficient/source
        // clamps and cancellation-safe transition around the thin-path threshold.
        double[] background = [8,4,2], thresholdDistances = [0,.0009999,.001,.0010001,100];
        for (int row = 4; row < 10; row++)
        for (int x = 0; x < 5; x++)
        for (int channel = 0; channel < 3; channel++)
        {
            double distance = row == 5 ? 0 : row == 9 ? thresholdDistances[x] : distances[x];
            double rawScatter = row == 7 ? 0 : row == 8 && channel == 2 ? -.4 : row == 9 ? .5 : scattering[channel];
            double scatter = Math.Max(rawScatter,0);
            double sigma = row == 4 ? 0 : row == 9 ? 1 : Math.Max(.1 * (channel + 1) + rawScatter,0);
            double source = row == 8 && channel == 0 ? 0 : channel + 2;
            double transmission = Math.Exp(-sigma * distance);
            double integral = sigma == 0 ? distance : (1 - transmission) / sigma;
            AssertClose(background[channel] * transmission + source * scatter * integral,
                pixels[(row * 5 + x) * 4 + channel]);
        }
        double[] anisotropies=[0,.7,-.7,1e-7,-1e-7,2,-2];
        for(int row=0;row<anisotropies.Length;row++)
        for(int x=0;x<5;x++)
        {
            double g=Math.Clamp(anisotropies[row],-.95,.95), cosine=x*.5-1;
            double denominator=Math.Max(1+g*g-2*g*cosine,.0025);
            double expected=(1-g*g)/(4*Math.PI*denominator*Math.Sqrt(denominator));
            float actual=pixels[((row+10)*5+x)*4];
            // Near the clamped forward peak, float cancellation in 1+g*g-2*g
            // amplifies rounding; retain the tighter bound everywhere else.
            if(Math.Abs(g)==.95 && g*cosine>0 && Math.Abs(cosine)==1)
            {
                Assert.InRange((double)actual,expected*(1-5e-5),expected*(1+5e-5));
                Assert.InRange((double)pixels[((row+10)*5+x)*4+1],expected*(1-5e-5),expected*(1+5e-5));
            }
            else
            {
                AssertClose(expected,actual);
                AssertClose(expected,pixels[((row+10)*5+x)*4+1]);
            }
            if(row==0)
            {
                Assert.Equal(1f/12.56637061436f,actual);
                Assert.Equal(actual,pixels[((row+10)*5+x)*4+1]);
            }
            if(row is 3 or 4 && x is 0 or 4) Assert.NotEqual(1f/12.56637061436f,actual);
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
