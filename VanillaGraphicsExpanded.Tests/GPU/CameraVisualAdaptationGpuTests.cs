using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies opt-in physical adaptation independently of capped or manual display exposure.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraVisualAdaptationGpuTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    private CameraHistogramShaderProgram? histogramProgram;
    private CameraAdaptShaderProgram? adaptationProgram;
    #region Public API
    /// <summary>The extended dark domain publishes physical adaptation while global exposure remains authoritative.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DarkPhysicalAdaptationIsIndependentOfDisplayCap(bool automatic)
    {
        EnsureShaderTestAvailable();
        var policy = new CameraExposureSettings().Snapshot() with
        { Enabled = automatic, ManualEV = .5f, MinEV = -20, MaxEV = 2, CenterWeighted = false, LowPercentile = 0, HighPercentile = 1 };
        float[] state = Measure(-23.5f, policy, new float[8], true, 0);
        Assert.InRange(state[0], (automatic ? 2 : .5f) - .0001f, (automatic ? 2 : .5f) + .0001f);
        Assert.InRange(state[1], -23.5003f, -23.4997f);
        Assert.InRange(state[2], -23.5003f, -23.4997f);
        Assert.Equal(1, state[3]);
        Assert.InRange(state[4], MathF.Log2(policy.MiddleGray) + 23.5f - .0003f, MathF.Log2(policy.MiddleGray) + 23.5f + .0003f);
        Assert.Equal(1, state[5]);
    }

    /// <summary>The declared open meter floor and darker finite inputs remain missing statistics rather than invented adaptation.</summary>
    [Theory]
    [InlineData(-24f)]
    [InlineData(-30f)]
    public void SamplesAtOrBelowOptInFloorHaveNeutralMissingMetadata(float logarithm)
    {
        EnsureShaderTestAvailable();
        float[] state = Measure(logarithm, new CameraExposureSettings().Snapshot(), new float[8], true, 0);
        Assert.Equal(0, state[3]);
        Assert.All(state, value => Assert.True(float.IsFinite(value)));
        Assert.Equal(0, state[1]); Assert.Equal(0, state[2]);
    }

    /// <summary>Independent luminance adaptation honors frame partition while the clamped exposure remains capped.</summary>
    [Fact]
    public void AdaptationHistoryRetainsFrameRateInvariantPhysicalEstimate()
    {
        EnsureShaderTestAvailable();
        var policy = new CameraExposureSettings().Snapshot() with
        { MinEV = -20, MaxEV = 2, CenterWeighted = false, LowPercentile = 0, HighPercentile = 1 };
        float[] initial = Measure(-23.5f, policy, new float[8], true, 0);
        float[] whole = Measure(-12, policy, initial, false, 1);
        float[] split = initial;
        for (int i = 0; i < 10; i++) split = Measure(-12, policy, split, false, .1f);
        Assert.InRange(Math.Abs(whole[1] - split[1]), 0, .0001f);
        Assert.True(whole[1] > -23.5f && whole[1] < -12);
        Assert.Equal(2, whole[0]);
        Assert.InRange(whole[2], -12.0003f, -11.9997f);
        Assert.Equal(1, whole[3]);
        float[] reset = Measure(-12, policy, initial, true, 0);
        Assert.InRange(reset[1], -12.0003f, -11.9997f);
    }

    /// <summary>Invalid luminance or missing/nonfinite validity resets the physical estimate without importing corrupt history.</summary>
    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(-23.5f, float.NaN)]
    [InlineData(-23.5f, float.PositiveInfinity)]
    [InlineData(-23.5f, 0f)]
    public void InvalidPhysicalHistoryUsesCurrentMeasuredLuminance(float previousLogarithm, float validity)
    {
        EnsureShaderTestAvailable();
        float[] previous = [2f, previousLogarithm, previousLogarithm, validity, 20f, 1f, 0f, 0f];
        float[] state = Measure(-12, new CameraExposureSettings().Snapshot(), previous, false, 0);
        Assert.InRange(state[1], -12.0003f, -11.9997f);
        Assert.Equal(1, state[3]);
        Assert.All(state, value => Assert.True(float.IsFinite(value)));
    }

    /// <summary>The widened opt-in meter's highest populated workgroup stays within the independently derived uint sum limit.</summary>
    [Fact]
    public void ExtendedMeterUpperRangeCannotOverflowIntegerSum()
    {
        EnsureShaderTestAvailable();
        var policy = new CameraExposureSettings().Snapshot() with { CenterWeighted = false };
        var histogram = histogramProgram ??= Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        using var targets = new CameraExposureTargets();
        float level = MathF.Pow(2, 39.99f);
        using var scene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [level, level, level, 1]);
        histogram.SceneRadiance = scene; histogram.Histogram = targets.Histogram; histogram.Capture(policy, true, true);
        histogram.Dispatch(CameraExposureTargets.HistogramGroupCount); GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        uint[] encoded = ExposureTestStorage.Read<uint>(targets.Histogram);
        double expected = (Math.Log2(level) + 24) / 64 * 4096 * 128;
        Assert.True(64d * 4096 * 128 <= uint.MaxValue);
        for (int group = 0; group < CameraExposureTargets.HistogramGroupCount; group++)
        {
            int last = (group * 64 + 63) * 2;
            Assert.Equal(group==0 ? 4096u : 0u, encoded[last]);
            Assert.InRange((double)encoded[last + 1], (group==0 ? expected : 0) - 1, (group==0 ? expected : 0) + 1);
            for (int bin = 0; bin < 63; bin++)
            {
                Assert.Equal(0u, encoded[(group * 64 + bin) * 2]);
                Assert.Equal(0u, encoded[(group * 64 + bin) * 2 + 1]);
            }
        }
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
        using var history = GpuShaderStorageBuffer.Create(debugName: "Tests.PhysicalAdaptation");
        history.UploadData(previous);
        using var camera = TestFrameCamera.CreateIdentity(1, 1, dt);
        histogram.SceneRadiance = scene; histogram.Histogram = targets.Histogram; histogram.Capture(policy, reset, true);
        adaptation.Histogram = targets.Histogram; adaptation.PreviousExposure = history;
        adaptation.OutputExposure = targets.WriteTarget; adaptation.FrameInputs = camera; adaptation.Capture(policy, reset, true);
        Assert.True(CameraExposureDispatch.TryRun(histogram, adaptation));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        float[] state = ExposureTestStorage.Read<float>(targets.WriteTarget);
        output.WriteLine($"Physical adaptation diagnostic log={logarithm:R} reset={reset} dt={dt:R} state={string.Join(',', state.Select(v => v.ToString("R")))}.");
        return state;
    }
    #endregion
}
