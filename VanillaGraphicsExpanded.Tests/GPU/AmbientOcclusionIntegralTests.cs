using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Compares the production analytic visibility integral with independent cosine-weighted numerical quadrature.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class AmbientOcclusionIntegralTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Normal tilt, asymmetric horizons and neutral/full occlusion preserve normalized cosine-weighted visibility.</summary>
    [Theory]
    [InlineData(-1.2f)]
    [InlineData(-.6f)]
    [InlineData(0f)]
    [InlineData(.6f)]
    [InlineData(1.2f)]
    public void AnalyticIntegralMatchesQuadrature(float angle) {
        EnsureShaderTestAvailable();var program=Programs.Create<AoIntegralProgram>();
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        float low=Math.Max(-MathF.PI/2,angle-MathF.PI/2),high=Math.Min(MathF.PI/2,angle+MathF.PI/2);
        foreach(var fraction in new[]{(1f,1f),(.2f,.8f),(.8f,.2f),(0f,0f)}) {
            float a=low*fraction.Item1,b=high*fraction.Item2;
            program.Capture(angle,a,b);TestFramework.RenderQuadTo(program,target);
            float[] result=target[0].ReadPixels();
            double expected=Integrate(a,b,angle),baseline=Integrate(low,high,angle);
            Assert.InRange(result[0],expected-.00002,expected+.00002);
            Assert.InRange(result[1],baseline-.00002,baseline+.00002);
            Assert.InRange(result[2],expected/baseline-.00003,expected/baseline+.00003);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Stronger evidence raises immediately; weaker evidence releases a bounded fraction, including repeated wall and thin-object sequences.</summary>
    [Theory]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void RunningHorizonUsesBoundedOrderedEvidence(float relaxation) {
        EnsureShaderTestAvailable();var program=Programs.Create<AoIntegralProgram>();
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        foreach(float[] evidence in new float[][] { [.8f,.8f,.8f,.8f], [.8f,0f,0f,0f], [.2f,.6f,.4f,.9f], [0f,0f,0f,0f] }) {
            float actual=0,expected=0;
            foreach(float candidate in evidence) {
                program.CaptureRelaxation(actual,candidate,relaxation);TestFramework.RenderQuadTo(program,target);
                actual=target[0].ReadPixels()[0];
                expected=candidate>=expected?candidate:expected+(candidate-expected)*Math.Clamp(relaxation,0,1);
                Assert.InRange(actual,expected-.00001f,expected+.00001f);
                Assert.InRange(actual,0,1);
            }
            if(evidence.All(value=>value==.8f)) Assert.InRange(actual,.79999f,.80001f);
            if(evidence[0]==.8f && evidence[1]==0 && relaxation>0) Assert.True(actual<.8f);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Numerically integrates the projected cosine measure without using the shader's closed form.</summary>
    private static double Integrate(double low,double high,double normal) {
        const int steps=16384;double interval=(high-low)/steps,sum=0;
        for(int i=0;i<steps;i++) {double angle=low+(i+.5)*interval;sum+=Math.Cos(angle-normal)*Math.Abs(Math.Sin(angle));}
        return sum*interval;
    }
    #endregion
}
