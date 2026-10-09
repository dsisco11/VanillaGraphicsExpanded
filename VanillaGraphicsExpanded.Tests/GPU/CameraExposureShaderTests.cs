using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production histogram and exposure adaptation binaries against independent numerical expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraExposureShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Generic borrowed metadata still rejects a 3D texture at a production 2D sampler boundary.</summary>
    [Fact]
    public void BorrowedTextureTargetMismatchIsRejected()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<CameraHistogramShaderProgram>();
        Assert.True(shader.EnsureReady());
        using var external=Texture3D.Create(2,2,2,PixelInternalFormat.Rgba16f,TextureFilterMode.Nearest,TextureTarget.Texture3D,"Borrowed.Mismatch");
        using var borrowed=new BorrowedTexture(external.TextureId);
        var binding=shader.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"sceneRadiance"));
        Assert.True(binding.Active);
        var error=Assert.Throws<InvalidOperationException>(()=>ShaderPreparedSubmission.ValidateSampler(binding,borrowed));
        Assert.Contains("Incompatible texture target",error.Message);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #region Metering
    /// <summary>Invalid and out-of-range RGB never contaminate the valid scene population or its log luminance.</summary>
    [Fact]
    public void HistogramRejectsInvalidSamplesAndPreservesSource()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        float[] levels = [.18f, 0, float.NaN, float.PositiveInfinity, 65536, .000001f];
        float[] pixels = Pixels(index => levels[index % levels.Length]);
        using var source = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f, pixels);
        rig.Meter(source, Settings());
        float[] histogram = rig.Histogram[0].ReadPixels();
        AssertClose(384, histogram.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(384 * MathF.Log2(.18f), histogram.Where((_, i) => i % 2 == 1).Sum(), .02f);
        float[] unchanged = source.ReadPixels();
        for (int i = 0; i < pixels.Length; i++)
            if (float.IsNaN(pixels[i])) Assert.True(float.IsNaN(unchanged[i]));
            else Assert.Equal(pixels[i], unchanged[i]);
        AssertClose(0, rig.Adapt(Settings(), 3, 0, true));
    }

    /// <summary>Percentile trimming removes sparse sun/dark outliers and retains the geometric scene mean.</summary>
    [Fact]
    public void PercentilesRejectSolarOutliersAndWeightPartialBins()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var outliers = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f,
            Pixels(i => i < 216 ? .001f : i >= 2304 - 216 ? 4096 : .18f));
        var trimmed = Settings() with { LowPercentile = .1f, HighPercentile = .9f };
        rig.Meter(outliers, trimmed);
        AssertClose(0, rig.Adapt(trimmed, 0, 0, true));
        Assert.True(MathF.Abs(rig.Adapt(Settings(), 0, 0, true)) > .25f);
        using var partial = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f, Pixels(i => i < 576 ? .125f : 8));
        var settings = Settings() with { LowPercentile = .125f, HighPercentile = .875f };
        rig.Meter(partial, settings);
        // Retained population is one sixth log2(.125) and five sixths log2(8), giving mean log2=2.
        AssertClose(MathF.Log2(.18f) - 2, rig.Adapt(settings, 0, 0, true));
    }

    /// <summary>Equal luminance populations use a geometric mean and central highlights gain weight only when requested.</summary>
    [Fact]
    public void MeteringUsesGeometricMeanAndOptionalCenterWeight()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var equal = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f, Pixels(i => i < 1152 ? 1 : 4));
        rig.Meter(equal, Settings());
        AssertClose(MathF.Log2(.18f) - 1, rig.Adapt(Settings(), 0, 0, true));
        using var central = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f,
            Pixels(i => Math.Abs(i % 64 - 32) < 8 && Math.Abs(i / 64 - 18) < 8 ? 16 : .18f));
        rig.Meter(central, Settings());
        float uniform = rig.Adapt(Settings(), 0, 0, true);
        var weighted = Settings() with { CenterWeighted = true };
        rig.Meter(central, weighted);
        float center = rig.Adapt(weighted, 0, 0, true);
        Assert.True(center < uniform - .15f);
    }
    #endregion
    #region Adaptation
    /// <summary>Separate response rates bound distant changes and converge exponentially within one stop.</summary>
    [Theory]
    [InlineData(2f, .5f, .5f)]
    [InlineData(-2f, .2f, -.6f)]
    public void AdaptationUsesSeparateBoundedRates(float target, float dt, float expected)
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        rig.PublishTarget(target);
        AssertClose(expected, rig.Adapt(Settings(), 0, dt, false));
        rig.PublishTarget(.5f);
        AssertClose(.5f * (1 - MathF.Exp(-1)), rig.Adapt(Settings(), 0, 1, false));
    }

    /// <summary>Splitting elapsed time preserves the same result even across the linear-to-exponential transition.</summary>
    [Fact]
    public void AdaptationIsInvariantToFramePartitionAndHonorsBounds()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        var settings = Settings() with { BrightenRate = 2 };
        rig.PublishTarget(4);
        float full = rig.Adapt(settings, -2, 3, false);
        float split = -2;
        for (int i = 0; i < 30; i++) split = rig.Adapt(settings, split, .1f, false);
        AssertClose(4 - MathF.Exp(-1), full);
        AssertClose(full, split, .00003f);
        AssertClose(-2, rig.Adapt(settings, -2, -1, false));
        rig.PublishTarget(20);
        AssertClose(3, rig.Adapt(settings with { MaxEV = 3 }, 0, 0, true));
        rig.PublishTarget(-20);
        AssertClose(-3, rig.Adapt(settings with { MinEV = -3 }, 0, 0, true));
    }

    /// <summary>One production program consumes independent shared camera durations without mutating either view snapshot.</summary>
    [Fact]
    public void AlternateSharedFramesKeepIndependentAdaptationTime()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var first = TestFrameCamera.CreateIdentity(1920, 1080, .5f);
        using var second = TestFrameCamera.CreateIdentity(640, 360, .2f);
        byte[] firstBytes = first.Bytes.ToArray(), secondBytes = second.Bytes.ToArray();
        rig.PublishTarget(2);
        AssertClose(.5f, rig.Adapt(Settings(), 0, 0, false, first));
        AssertClose(.2f, rig.Adapt(Settings(), 0, 0, false, second));
        AssertClose(.5f, rig.Adapt(Settings(), 0, 0, false, first));
        Assert.Equal(firstBytes, first.Bytes.ToArray());
        Assert.Equal(secondBytes, second.Bytes.ToArray());
    }

    /// <summary>Manual mode, empty input, reset and invalid history produce finite coherent exposure.</summary>
    [Fact]
    public void ManualResetAndEmptyHistogramHaveDefinedHistory()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        var settings = Settings() with { ManualEV = 1, Compensation = .5f };
        rig.PublishEmpty();
        AssertClose(-2, rig.Adapt(settings, -2, 10, false));
        AssertClose(1.5f, rig.Adapt(settings, -2, 10, true));
        AssertClose(1.5f, rig.Adapt(settings, float.NaN, 10, false));
        AssertClose(1.5f, rig.Adapt(settings with { Enabled = false }, -2, 10, false));
        AssertClose(1, rig.Adapt(settings with { Enabled = false, MaxEV = 1 }, -2, 10, false));
        rig.PublishTarget(2);
        AssertClose(2.5f, rig.Adapt(settings, -2, 0, true));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Uses broad exposure bounds and uniform full-population metering unless a test supplies another policy.</summary>
    private static CameraExposureParameters Settings() => new(true, 0, 0, -12, 12, .18f, 0, 1, 1, 3, false);
    /// <summary>Builds controlled RGB samples with deliberately varying alpha that metering must ignore.</summary>
    private static float[] Pixels(Func<int, float> luminance)
    {
        var result = new float[64 * 36 * 4];
        for (int i = 0; i < 64 * 36; i++)
        {
            float value = luminance(i);
            result[i * 4] = result[i * 4 + 1] = result[i * 4 + 2] = value;
            result[i * 4 + 3] = (i % 17) / 16f;
        }
        return result;
    }
    /// <summary>Asserts independent floating-point expectations with a narrowly scoped numerical tolerance.</summary>
    private static void AssertClose(float expected, float actual, float tolerance = .0001f)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    /// <summary>Combines production shader owners with retained numerical render targets.</summary>
    private ExposureRig CreateRig() => new(Programs.Create<CameraHistogramShaderProgram>(), Programs.Create<CameraAdaptShaderProgram>(), TestFramework);

    /// <summary>Submits the real histogram and adaptation programs with typed input capture and explicit history images.</summary>
    private sealed class ExposureRig : IDisposable
    {
        private readonly CameraHistogramShaderProgram meter;
        private readonly CameraAdaptShaderProgram adapt;
        private readonly ShaderTestFramework framework;
        private readonly GpuVao vao = GpuVao.Create();
        private readonly GpuFramebuffer output;
        private GpuTexture histogramTexture;
        internal GpuFramebuffer Histogram { get; }

        #region Public API
        /// <summary>Allocates the production-sized histogram and single-pixel adaptation target.</summary>
        internal ExposureRig(CameraHistogramShaderProgram meter, CameraAdaptShaderProgram adapt, ShaderTestFramework framework)
        {
            this.meter = meter; this.adapt = adapt; this.framework = framework;
            Histogram = framework.CreateTestGBuffer(64, 1, PixelInternalFormat.Rg32f);
            output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.R32f);
            histogramTexture = Histogram[0];
        }
        /// <summary>Runs fixed-grid scene metering without modifying its source image.</summary>
        internal void Meter(GpuTexture texture, CameraExposureParameters settings)
        {
            meter.SceneRadiance = texture; meter.Capture(settings, false);
            Histogram.BindWithViewport(); ConfigureDraw();
            using (meter.UseScope()) using (vao.BindScope()) GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            histogramTexture = Histogram[0];
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        /// <summary>Publishes an independent single-bin population whose mean implies the requested exposure target.</summary>
        internal void PublishTarget(float target)
        {
            float[] values = new float[128]; values[64] = 1; values[65] = MathF.Log2(.18f) - target;
            histogramTexture = framework.CreateTexture(64, 1, PixelInternalFormat.Rg32f, values);
        }
        /// <summary>Publishes an empty population to exercise missing scene statistics.</summary>
        internal void PublishEmpty()
        {
            histogramTexture = framework.CreateTexture(64, 1, PixelInternalFormat.Rg32f, new float[128]);
        }
        /// <summary>Runs one production adaptation step with explicit previous EV, frame time and reset semantics.</summary>
        internal float Adapt(CameraExposureParameters settings, float previous, float dt, bool reset, VgeFrameUniformBuffer? sharedFrame = null)
        {
            using var history = framework.CreateTexture(1, 1, PixelInternalFormat.R32f, [previous]);
            // The world publisher normalizes negative elapsed time before the shared snapshot is captured.
            using var camera = sharedFrame is null ? TestFrameCamera.CreateIdentity(1, 1, Math.Max(dt, 0)) : null;
            adapt.FrameInputs = sharedFrame ?? camera!;
            adapt.Histogram = histogramTexture; adapt.PreviousExposure = history; adapt.Capture(settings, reset);
            output.BindWithViewport(); ConfigureDraw();
            using (adapt.UseScope()) using (vao.BindScope()) GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return output[0].ReadPixels()[0];
        }
        /// <summary>Releases the fixture-owned vertex array; the framework owns images and framebuffer targets.</summary>
        public void Dispose() => vao.Dispose();
        #endregion
        #region Private
        /// <summary>Declares the deterministic fullscreen state shared by both exposure passes.</summary>
        private static void ConfigureDraw()
        {
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.FramebufferSrgb);
            GL.ColorMask(true, true, true, true);
            StateCache.Current.InvalidateAll();
        }
        #endregion
    }
    #endregion
}
