using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies separated submerged intervals, underwater initialization and RGB transport in production SPIR-V.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterBoundaryTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Checks analytic interval lengths, colored transmission, zero density, scattering and unresolved winding.</summary>
    [Fact]
    public void SignedIntervalsMatchAnalyticWaterPaths()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<WaterBoundaryShaderProgram>();
        using var target = CreateRenderTarget(5, 3, TextureFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        var expectedLengths=new double[16];
        var expectedScattering=new double[16];
        double[] lengths = [2, 3, 5, 0];
        double[] extinction = [.3, .5, .7];
        double[] sources = [.2, .3, .4];
        for (int column = 0; column < 4; column++)
        {
            expectedLengths[column*4+3]=lengths[column];
            for (int channel = 0; channel < 3; channel++)
            {
                double transmission = Math.Exp(-extinction[channel] * lengths[column]);
                expectedLengths[column*4+channel]=transmission;
                double scattering = sources[channel] / extinction[channel] * (1 - transmission);
                expectedScattering[column*4+channel]=scattering;
            }
        }
        ImageAssertions.Comparisons.CompareDouble(target[0],expectedLengths,1e-5,new(new(0,0),0,0,0,4,1,1)).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(target[0],1,new(new(0,0),0,1,0,4,1,1),7).AssertEqual();
        ImageAssertions.Comparisons.CompareDouble(target[0],expectedScattering,1e-5,new(new(0,0),0,2,0,4,1,1),7).AssertEqual();
        var first=GpuLocalRead.Tuple(target[0],0,0);
        Assert.True(first[0]>first[1]&&first[1]>first[2]);
        ImageAssertions.Comparisons.CompareConstant(target[0],-1,new(new(0,0),4,0,0,1,3,1),8).AssertEqual();

    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
