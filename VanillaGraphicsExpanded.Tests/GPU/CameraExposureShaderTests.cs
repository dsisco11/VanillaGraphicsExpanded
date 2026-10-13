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
    /// <summary>Admission accepts the exact minimum contract and rejects every missing capability or insufficient limit.</summary>
    [Fact]
    public void CapabilityAdmissionRequiresCompleteComputeAndConsumerContract()
    {
        EnsureShaderTestAvailable();
        var supported=GpuSupport.Graphics with {MaxComputeWorkGroupInvocations=64,MaxComputeWorkGroupSize=System.Collections.Immutable.ImmutableArray.Create(64,1,1),MaxComputeWorkGroupCount=System.Collections.Immutable.ImmutableArray.Create(36,1,1),MaxComputeSharedMemorySize=512,MaxShaderStorageBufferBindings=3,MaxFragmentShaderStorageBlocks=1,MaxComputeShaderStorageBlocks=3,MaxShaderStorageBlockSize=18432};
        Assert.True(CameraExposureDispatch.Supports(supported));
        GraphicsCapabilities[] insufficient=[supported with {SupportsArbComputeShader=false},supported with {SupportsArbShaderImageLoadStore=false},supported with {SupportsArbShaderStorageBufferObject=false},supported with {SupportsArbGlSpirv=false},supported with {MaxComputeWorkGroupInvocations=63},supported with {MaxComputeWorkGroupSize=System.Collections.Immutable.ImmutableArray.Create(63,1,1)},supported with {MaxComputeWorkGroupCount=System.Collections.Immutable.ImmutableArray.Create(35,1,1)},supported with {MaxComputeSharedMemorySize=511},supported with {MaxShaderStorageBufferBindings=2},supported with {MaxFragmentShaderStorageBlocks=0},supported with {MaxComputeShaderStorageBlocks=2},supported with {MaxShaderStorageBlockSize=18431}];
        Assert.All(insufficient,value=>Assert.False(CameraExposureDispatch.Supports(value)));
    }
    /// <summary>Generic borrowed metadata still rejects a 3D texture at a production 2D sampler boundary.</summary>
    [Fact]
    public void BorrowedTextureTargetMismatchIsRejected()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.CreateCompute(CameraHistogramShaderProgram.Create);
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
    /// <summary>Compute interruptions restore all borrowed storage slots and leave publication unchanged when admission fails.</summary>
    [Fact]
    public void ComputeBoundaryRestoresStorageAndRejectsUnknownProgram()
    {
        EnsureShaderTestAvailable();
        var meter=Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adapt=Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        using var targets=new CameraExposureTargets();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.18f,.18f,.18f,1f]);
        using var camera=TestFrameCamera.CreateIdentity(1,1);
        using var external=ExposureTestStorage.Create(7);
        meter.SceneRadiance=source;meter.Histogram=targets.Histogram;meter.Capture(Settings(),true);
        adapt.Histogram=targets.Histogram;adapt.PreviousExposure=targets.Exposure;adapt.OutputExposure=targets.WriteTarget;adapt.FrameInputs=camera;adapt.Capture(Settings(),true);
        int published=targets.Exposure.BufferId;
        for(int slot=0;slot<3;slot++)external.BindBase(slot);
        try
        {
            Assert.True(CameraExposureDispatch.TryRun(meter,adapt));
            for(int slot=0;slot<3;slot++)
            {
                GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding,slot,out int selected);
                Assert.Equal(external.BufferId,selected);
            }
            AssertClose(0,ExposureTestStorage.Read<float>(targets.WriteTarget)[0]);
            StateCache.Current.UseProgram(meter.ProgramId);
            Assert.False(CameraExposureDispatch.TryRun(meter,adapt));
            Assert.Equal(published,targets.Exposure.BufferId);
            Assert.Equal(meter.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
        }
        finally { StateCache.Current.UnbindProgram();for(int slot=0;slot<3;slot++)GpuShaderStorageBuffer.UnbindBase(slot); }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #region Metering
    /// <summary>Frozen storage scaling leaves physical luminance statistics and source pixels unchanged.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.25f)]
    [InlineData(.0009765625f)]
    public void MeteringDecodesPhysicalLuminanceIndependentOfStorageScale(float scale)
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var publication = new TestFrameLightingPublication();
        publication.Capture(scale);
        float stored = 32 * scale;
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [stored, stored, stored, .37f]);
        rig.Meter(source, Settings(), publication.Buffer);
        var bins = rig.ReadHistogram();
        AssertClose(2304, bins.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(2304 * 5, bins.Where((_, i) => i % 2 == 1).Sum(), .06f);
        AssertClose(MathF.Log2(.18f) - 5, rig.Adapt(Settings(), 0, 0, true));
        Assert.Equal(new[] { stored, stored, stored, .37f }, source.ReadPixels());
    }

    /// <summary>Invalid and out-of-range RGB never contaminate the valid scene population or its log luminance.</summary>
    [Fact]
    public void HistogramRejectsInvalidSamplesAndPreservesSource()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        float[] levels = [.18f, 0, float.NaN, float.PositiveInfinity, MathF.Pow(2, 40), MathF.Pow(2, -17)];
        float[] pixels = Pixels(index => levels[index % levels.Length]);
        using var source = TestFramework.CreateTexture(64, 36, PixelInternalFormat.Rgba32f, pixels);
        rig.Meter(source, Settings());
        float[] histogram = rig.ReadHistogram();
        AssertClose(384, histogram.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(384 * MathF.Log2(.18f), histogram.Where((_, i) => i % 2 == 1).Sum(), .06f);
        float[] unchanged = source.ReadPixels();
        for (int i = 0; i < pixels.Length; i++)
            if (float.IsNaN(pixels[i])) Assert.True(float.IsNaN(unchanged[i]));
            else Assert.Equal(pixels[i], unchanged[i]);
        AssertClose(0, rig.Adapt(Settings(), 3, 0, true));
        using var empty=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,1f]);
        rig.Meter(empty,Settings());
        Assert.All(rig.ReadHistogram(),value=>Assert.Equal(0,value));
    }

    /// <summary>Every weighted bin agrees with an independent double-precision scene population within declared encoding bounds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistogramMatchesIndependentWeightedPopulation(bool weighted)
    {
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        float[] pixels=Pixels(i=>MathF.Pow(2,-10+(i%61)*.4f));
        using var source=TestFramework.CreateTexture(64,36,PixelInternalFormat.Rgba32f,pixels);
        rig.Meter(source,Settings() with {CenterWeighted=weighted});
        float[] actual=rig.ReadHistogram();
        double[] counts=new double[64],sums=new double[64];int[] population=new int[64];
        for(int i=0;i<2304;i++)
        {
            double level=Math.Log2(pixels[i*4]);int bin=Math.Clamp((int)((level+16)*64/56),0,63);
            double x=2*(i%64+.5)/64-1,y=2*(i/64+.5)/36-1;
            double weight=weighted?.25+.75*Math.Exp(-2*(x*x+y*y)):1;
            counts[bin]+=weight;sums[bin]+=weight*level;population[bin]++;
        }
        for(int bin=0;bin<64;bin++)
        {
            double count=0,sum=0;
            for(int group=0;group<36;group++){count+=actual[(group*64+bin)*2];sum+=actual[(group*64+bin)*2+1];}
            double weightBound=population[bin]/8192.0+.0001;
            Assert.InRange(count,counts[bin]-weightBound,counts[bin]+weightBound);
            double sumBound=population[bin]*.002+.001;
            Assert.InRange(sum,sums[bin]-sumBound,sums[bin]+sumBound);
        }
    }
    /// <summary>Maximum valid luminance filling a local bin cannot overflow integer atomics or contaminate adjacent bins.</summary>
    [Fact]
    public void UpperRangeUniformPopulationDoesNotOverflow()
    {
        EnsureShaderTestAvailable();using var rig=CreateRig();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[MathF.Pow(2,39.99f),MathF.Pow(2,39.99f),MathF.Pow(2,39.99f),1f]);
        rig.Meter(source,Settings());float[] bins=rig.ReadHistogram();
        for(int group=0;group<36;group++)
        {
            int last=(group*64+63)*2;Assert.Equal(64,bins[last]);
            AssertClose(64*39.99f,bins[last+1],.002f);
            for(int bin=0;bin<63;bin++){Assert.Equal(0,bins[(group*64+bin)*2]);Assert.Equal(0,bins[(group*64+bin)*2+1]);}
        }
        AssertClose(-12,rig.Adapt(Settings(),0,0,true));
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
        rig.PublishTarget(8);
        AssertClose(3, rig.Adapt(settings with { MaxEV = 3 }, 0, 0, true));
        rig.PublishTarget(-8);
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
    private static void AssertClose(float expected, float actual, float tolerance = .0003f)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    /// <summary>Combines production shader owners with retained numerical buffers.</summary>
    private ExposureRig CreateRig() => new(Programs.CreateCompute(CameraHistogramShaderProgram.Create), Programs.CreateCompute(CameraAdaptShaderProgram.Create));

    /// <summary>Submits the real histogram and adaptation programs with typed input capture and explicit history buffers.</summary>
    private sealed class ExposureRig : IDisposable
    {
        private readonly CameraHistogramShaderProgram meter;
        private readonly CameraAdaptShaderProgram adapt;
        private readonly CameraExposureTargets targets = new();

        #region Public API
        /// <summary>Allocates the production-sized histogram and scalar adaptation storage.</summary>
        internal ExposureRig(CameraHistogramShaderProgram meter, CameraAdaptShaderProgram adapt)
        {
            this.meter = meter; this.adapt = adapt;

        }
        /// <summary>Runs fixed-grid scene metering without modifying its source image.</summary>
        internal void Meter(GpuTexture texture, CameraExposureParameters settings, GpuShaderStorageBuffer? publication = null)
        {
            if (publication is not null) meter.FrameLightingInputs = publication;
            meter.SceneRadiance = texture; meter.Capture(settings, false);
            meter.Histogram = targets.Histogram;
            meter.Dispatch(36);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        /// <summary>Publishes an independent single-bin population whose mean implies the requested exposure target.</summary>
        internal void PublishTarget(float target)
        {
            uint[] values = new uint[36 * 128];
            values[64] = 4096; values[65] = (uint)MathF.Round((MathF.Log2(.18f) - target + 16) * 1048576);
            targets.Histogram.UploadData(values);
        }
        /// <summary>Publishes an empty population to exercise missing scene statistics.</summary>
        internal void PublishEmpty()
        {
            targets.Histogram.UploadData(new uint[36 * 128]);
        }
        /// <summary>Runs one production adaptation step with explicit previous EV, frame time and reset semantics.</summary>
        internal float Adapt(CameraExposureParameters settings, float previous, float dt, bool reset, VgeFrameUniformBuffer? sharedFrame = null)
        {
            using var history = ExposureTestStorage.Create(previous);
            // The world publisher normalizes negative elapsed time before the shared snapshot is captured.
            using var camera = sharedFrame is null ? TestFrameCamera.CreateIdentity(1, 1, Math.Max(dt, 0)) : null;
            adapt.FrameInputs = sharedFrame ?? camera!;
            adapt.Histogram = targets.Histogram; adapt.PreviousExposure = history; adapt.OutputExposure = targets.WriteTarget; adapt.Capture(settings, reset);
            adapt.Dispatch(1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return ExposureTestStorage.Read<float>(targets.WriteTarget)[0];
        }
        /// <summary>Releases all retained histogram and temporal storage.</summary>
        public void Dispose() => targets.Dispose();
        #endregion
        /// <summary>Decodes every workgroup bin for independent population and log-sum checks.</summary>
        internal float[] ReadHistogram()
        {
            uint[] encoded = ExposureTestStorage.Read<uint>(targets.Histogram);
            float[] decoded = new float[encoded.Length];
            for (int i = 0; i < encoded.Length; i += 2)
            {
                decoded[i] = encoded[i] / 4096f;
                decoded[i + 1] = encoded[i + 1] / 1048576f - 16 * decoded[i];
            }
            return decoded;
        }
    }
    #endregion
}
