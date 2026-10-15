using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Qualifies instantaneous histogram metadata independently of the applied camera gain.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CameraVisualAdaptationGpuTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    private CameraHistogramShaderProgram? histogramProgram;
    private CameraAdaptShaderProgram? adaptationProgram;
    #region Public API
    /// <summary>The meter publishes current luminance while only the global camera gain adapts or hits its cap.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InstantMeterMetadataDoesNotCarryIndependentAdaptation(bool automatic)
    {
        EnsureShaderTestAvailable();
        var policy=new CameraExposureSettings().Snapshot() with {Enabled=automatic,ManualLogGain=.5f,MinLogGain=-20,MaxLogGain=2,CenterWeighted=false,LowPercentile=0,HighPercentile=1};
        float[] state=Measure(-23.5f,policy,new float[8],true,0);
        Close(automatic?2:.5f,state[0]);Close(-23.5f-(float)Math.Log2(.18),state[1]);Close(-23.5f,state[2]);Assert.Equal(1,state[3]);
        Close(MathF.Log2(.18f)+23.5f,state[4]);Assert.Equal(1,state[5]);
    }
    /// <summary>Current metering replaces stale physical metadata immediately without a second temporal history.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-100f)]
    public void PreviousMetadataCannotInfluenceCurrentMeter(float stale)
    {
        EnsureShaderTestAvailable();
        float[] state=Measure(-12,new CameraExposureSettings().Snapshot(),[2,stale,stale,1,20,1,0,0],false,0);
        Close(2,state[0]);Close(-12-(float)Math.Log2(.18),state[1]);Close(-12,state[2]);Assert.Equal(1,state[3]);
    }
    /// <summary>Out-of-domain input withdraws instantaneous statistics and leaves all reserved history slots neutral.</summary>
    [Theory]
    [InlineData(-24f)]
    [InlineData(-30f)]
    public void MissingMeterInputHasNeutralMetadata(float logarithm)
    {
        EnsureShaderTestAvailable();
        float[] state=Measure(logarithm,new CameraExposureSettings().Snapshot(),new float[8],true,0);
        Assert.Equal(0,state[1]);Assert.Equal(0,state[2]);Assert.Equal(0,state[3]);
        Assert.All(state,value=>Assert.True(float.IsFinite(value)));
    }
    #endregion
    #region Private
    /// <summary>Submits actual packaged histogram/adaptation compute with 32-byte history and explicitly enabled statistics.</summary>
    private float[] Measure(float logarithm, CameraExposureParameters policy, float[] previous, bool reset, float dt)
    {
        var histogram = histogramProgram ??= Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adaptation = adaptationProgram ??= Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        using var targets = new CameraExposureTargets();
        float level = MathF.Pow(2, logarithm);
        using var scene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [level, level, level, 1]);
        using var history = GpuShaderStorageBuffer.Create(debugName: "Tests.InstantMeter");
        history.UploadData(previous);
        using var camera = TestFrameCamera.CreateIdentity(1, 1, dt);
        histogram.SceneRadiance = scene; histogram.Histogram = targets.Histogram; histogram.Capture(policy, reset, true);
        adaptation.Histogram = targets.Histogram; adaptation.PreviousExposure = history;
        adaptation.OutputExposure = targets.WriteTarget; adaptation.FrameInputs = camera; adaptation.Capture(policy, reset, true);
        Assert.True(CameraExposureDispatch.TryRun(histogram, adaptation));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        float[] state = ExposureTestStorage.Read<float>(targets.WriteTarget);
        output.WriteLine($"Instant meter diagnostic log={logarithm:R} reset={reset} dt={dt:R} state={string.Join(',', state.Select(v => v.ToString("R")))}.");
        return state;
    }
    /// <summary>Checks meter publication within normalized encoding precision.</summary>
    private static void Close(float expected,float actual)=>Assert.InRange(actual,expected-.0003f,expected+.0003f);
    #endregion
}
