using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies separated submerged intervals, underwater initialization and RGB transport in production SPIR-V.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterBoundaryTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks analytic interval lengths, colored transmission, zero density, scattering and unresolved winding.</summary>
    [Fact]
    public void SignedIntervalsMatchAnalyticWaterPaths()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<WaterBoundaryShaderProgram>();
        using var target = CreateRenderTarget(5, 3, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        var values = target[0].ReadPixels();
        double[] lengths = [2, 3, 5, 0];
        double[] extinction = [.3, .5, .7];
        double[] sources = [.2, .3, .4];
        for (int column = 0; column < 4; column++)
        {
            Assert.InRange(values[column * 4 + 3], lengths[column] - 1e-5, lengths[column] + 1e-5);
            for (int channel = 0; channel < 3; channel++)
            {
                double transmission = Math.Exp(-extinction[channel] * lengths[column]);
                Assert.InRange(values[column * 4 + channel], transmission - 1e-5, transmission + 1e-5);
                Assert.Equal(1, values[(5 + column) * 4 + channel]);
                double scattering = sources[channel] / extinction[channel] * (1 - transmission);
                Assert.InRange(values[(10 + column) * 4 + channel], scattering - 1e-5, scattering + 1e-5);
            }
        }
        Assert.True(values[0] > values[1] && values[1] > values[2]);
        for (int row = 0; row < 3; row++) Assert.Equal(-1, values[(row * 5 + 4) * 4 + 3]);

    }
    #endregion
}
