using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Exercises production histogram and exposure adaptation binaries against independent numerical expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CameraExposureShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Metering
    /// <summary>Frozen storage scaling leaves physical luminance statistics and source pixels unchanged.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.25f)]
    [InlineData(.0009765625f)]
    public void MeteringDecodesPhysicalLuminanceIndependentOfStorageScale(float scale)
    {
        // Arrange: Scale a known 32-nit sample and publish the matching frozen frame-lighting scale.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var publication = new TestFrameLightingPublication();
        publication.Capture(scale);
        float stored = 32 * scale;
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [stored, stored, stored, .37f]);
        // Act: Run production metering and adaptation against the scaled sample.
        rig.Meter(source, Settings(), publication.Buffer);
        var bins = rig.ReadHistogram();
        // Assert: Physical statistics are scale-invariant and the source including alpha is unchanged.
        AssertClose(1, bins.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(5, bins.Where((_, i) => i % 2 == 1).Sum(), .06f);
        AssertClose(MathF.Log2(.18f) - 5, rig.Adapt(Settings(), 0, 0, true));
        Assert.Equal(new[] { stored, stored, stored, .37f }, source.ReadPixels());
    }

    /// <summary>Invalid and out-of-range RGB never contaminate the valid scene population or its log luminance.</summary>
    [Fact]
    public void HistogramRejectsInvalidSamplesAndPreservesSource()
    {
        // Arrange: Mix one valid luminance with zero, NaN, infinity, and values outside the supported range.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        float[] levels = [.18f, 0, float.NaN, float.PositiveInfinity, MathF.Pow(2, 40), MathF.Pow(2, -17)];
        float[] pixels = Pixels(index => levels[index % levels.Length]);
        using var source = TestFramework.CreateTexture(128, 72, TextureFormat.Rgba32f, pixels);
        // Act: Meter the mixed image, then an entirely empty image.
        rig.Meter(source, Settings());
        float[] histogram = rig.ReadHistogram();
        // Assert: Only valid samples contribute, invalid input leaves the source untouched, and empty input publishes no bins.
        AssertClose(384, histogram.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(384 * MathF.Log2(.18f), histogram.Where((_, i) => i % 2 == 1).Sum(), .06f);
        // The exact checker retains numeric equality and the authored NaN expectations over all 128x72 texels.
        ImageAssertions.Comparisons.Compare(source,pixels).AssertEqual();
        AssertClose(0, rig.Adapt(Settings(), 3, 0, true));
        using var empty=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,0f,0f,1f]);
        rig.Meter(empty,Settings());
        Assert.All(rig.ReadHistogram(),value=>Assert.Equal(0,value));
    }

    /// <summary>Every weighted bin agrees with an independent double-precision scene population within declared encoding bounds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistogramMatchesIndependentWeightedPopulation(bool weighted)
    {
        // Arrange: Generate a deterministic luminance field and independently model weighted or uniform box samples.
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        float[] pixels=Pixels(i=>MathF.Pow(2,-10+(i%61)*.4f));
        using var source=TestFramework.CreateTexture(128,72,TextureFormat.Rgba32f,pixels);
        // Act: Dispatch the production histogram with the selected center-weighting policy.
        rig.Meter(source,Settings() with {CenterWeighted=weighted});
        float[] actual=rig.ReadHistogram();
        // Assert: Every bin's count and log moment stays within its population-derived encoding bound.
        double[] counts=new double[64],sums=new double[64];int[] population=new int[64];
        for(int i=0;i<2304;i++)
        {
            int sourceIndex=((i/64)*2*128+(i%64)*2)*4; double level=Math.Log2(pixels[sourceIndex]);
            double x=2*(i%64+.5)/64-1,y=2*(i/64+.5)/36-1;
            double weight=weighted?.25+.75*Math.Exp(-2*(x*x+y*y)):1;
            AccumulateInterpolated(counts,sums,population,level,weight);
        }
        for(int bin=0;bin<64;bin++)
        {
            double count=0,sum=0;
            for(int group=0;group<CameraExposureTargets.HistogramGroupCount;group++){count+=actual[(group*64+bin)*2];sum+=actual[(group*64+bin)*2+1];}
            double weightBound=population[bin]/4096.0+.0001;
            Assert.InRange(count,counts[bin]-weightBound,counts[bin]+weightBound);
            double sumBound=population[bin]*.005+.001;
            Assert.InRange(sum,sums[bin]-sumBound,sums[bin]+sumBound);
        }
    }
    /// <summary>Maximum valid luminance filling a local bin cannot overflow integer atomics or contaminate adjacent bins.</summary>
    [Theory]
    [InlineData(1,1)]
    [InlineData(801,401)]
    public void UpperRangeUniformPopulationDoesNotOverflow(int width,int height)
    {
        // Arrange: Fill supported maximum luminance across small and large footprints to stress one-bin atomics.
        EnsureShaderTestAvailable();using var rig=CreateRig();
        float value=MathF.Pow(2,39.99f);
        using var source=TestFramework.CreateTexture(width,height,TextureFormat.Rgba32f,
            Enumerable.Range(0,width*height).SelectMany(_=>new[]{value,value,value,1f}).ToArray());
        // Act: Meter the full population, adapt it, then replace it with an empty image.
        rig.Meter(source,Settings());float[] bins=rig.ReadHistogram();
        // Assert: Counts and mean remain finite, unrelated bins stay empty, and the empty replacement clears history.
        int samples=((width+1)/2)*((height+1)/2);
        AssertClose(samples,bins.Where((_,i)=>i%2==0).Sum(),.01f);
        AssertClose(39.99f,bins.Where((_,i)=>i%2==1).Sum()/samples,.001f);
        for(int group=0;group<CameraExposureTargets.HistogramGroupCount;group++)
            for(int bin=0;bin<62;bin++){Assert.Equal(0,bins[(group*64+bin)*2]);Assert.Equal(0,bins[(group*64+bin)*2+1]);}
        AssertClose(-12,rig.Adapt(Settings(),0,0,true));
        using var empty=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,0f,0f,1f]);
        rig.Meter(empty,Settings());
        Assert.All(rig.ReadHistogram(),value=>Assert.Equal(0,value));
    }
    /// <summary>Percentile trimming removes sparse sun/dark outliers and retains the geometric scene mean.</summary>
    [Fact]
    public void PercentilesRejectSolarOutliersAndWeightPartialBins()
    {
        // Arrange: Build sparse extreme outliers and a second population split across partial histogram bins.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var outliers = TestFramework.CreateTexture(128, 72, TextureFormat.Rgba32f,
            Pixels(i => i < 216 ? .001f : i >= 2304 - 216 ? 4096 : .18f));
        var trimmed = Settings() with { LowPercentile = .1f, HighPercentile = .9f };
        // Act: Compare trimmed and untrimmed exposure, then meter the partial-bin population.
        rig.Meter(outliers, trimmed);
        // Assert: Trimming retains the geometric scene mean despite the outliers.
        AssertClose(0, rig.Adapt(trimmed, 0, 0, true));
        Assert.True(MathF.Abs(rig.Adapt(Settings(), 0, 0, true)) > .25f);
        using var partial = TestFramework.CreateTexture(128, 72, TextureFormat.Rgba32f, Pixels(i => i < 576 ? .125f : 8));
        var settings = Settings() with { LowPercentile = .125f, HighPercentile = .875f };
        rig.Meter(partial, settings);
        // Retained population is one sixth log2(.125) and five sixths log2(8), giving mean log2=2.
        AssertClose(MathF.Log2(.18f) - 2, rig.Adapt(settings, 0, 0, true));
    }

    /// <summary>Equal luminance populations use a geometric mean and central highlights gain weight only when requested.</summary>
    [Fact]
    public void MeteringUsesGeometricMeanAndOptionalCenterWeight()
    {
        // Arrange: Use equal populations at 1 and 4, then place a bright patch at image center.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var equal = TestFramework.CreateTexture(128, 72, TextureFormat.Rgba32f, Pixels(i => i < 1152 ? 1 : 4));
        // Act: Meter equal populations and then the same central-highlight field with each weighting policy.
        rig.Meter(equal, Settings());
        // Assert: Equal populations produce their geometric mean and center weighting favors the bright patch.
        AssertClose(MathF.Log2(.18f) - 1, rig.Adapt(Settings(), 0, 0, true));
        using var central = TestFramework.CreateTexture(128, 72, TextureFormat.Rgba32f,
            Pixels(i => Math.Abs(i % 64 - 32) < 8 && Math.Abs(i / 64 - 18) < 8 ? 16 : .18f));
        rig.Meter(central, Settings());
        float uniform = rig.Adapt(Settings(), 0, 0, true);
        var weighted = Settings() with { CenterWeighted = true };
        rig.Meter(central, weighted);
        float center = rig.Adapt(weighted, 0, 0, true);
        Assert.True(center < uniform - .15f);
    }

    /// <summary>Dense footprints include a one-pixel lamp missed by the former isolated grid samples.</summary>
    [Fact]
    public void DenseCoverageIncludesLampBetweenFormerSampleLocations()
    {
        // Arrange: Place one bright texel among otherwise uniform gray at a location omitted by the former sparse grid.
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        var pixels=Pixels(_=>.18f);
        pixels[0]=pixels[1]=pixels[2]=64;
        using var scene=TestFramework.CreateTexture(128,72,TextureFormat.Rgba32f,pixels);
        // Act: Run dense production coverage over the complete half-resolution footprint.
        rig.Meter(scene,Settings());
        var histogram=rig.ReadHistogram();
        // Assert: All boxes contribute and the isolated lamp shifts the measured mean by the independent amount.
        AssertClose(2304,histogram.Where((_,i)=>i%2==0).Sum(),.001f);
        double expected=(2303*Math.Log2(.18)+(Math.Log2((64+3*.18)/4)))/2304;
        AssertClose((float)(Math.Log2(.18)-expected),rig.Adapt(Settings(),0,0,true),.0003f);
        Assert.True(rig.Adapt(Settings(),0,0,true)<-.002f);
    }

    /// <summary>Odd edges, invalid footprints and centre weighting match an independent double-precision box reduction.</summary>
    [Theory]
    [InlineData(5,3,false,1f)]
    [InlineData(131,75,true,1f)]
    [InlineData(131,75,true,.0009765625f)]
    [InlineData(801,401,true,1f)]
    public void DenseOddFootprintsMatchIndependentReduction(int width,int height,bool weighted,float scale)
    {
        // Arrange: Create odd-sized scaled luminance data with invalid edge samples and an independent box oracle.
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        using var publication=new TestFrameLightingPublication();publication.Capture(scale);
        var pixels=new float[width*height*4];
        for(int i=0;i<width*height;i++)
        {
            float level=MathF.Pow(2,-5+(i%19)*.5f)*scale;
            pixels[i*4]=pixels[i*4+1]=pixels[i*4+2]=level;pixels[i*4+3]=1;
        }
        pixels[0]=float.NaN;
        pixels[((height-1)*width+width-1)*4]=float.PositiveInfinity;
        using var scene=TestFramework.CreateTexture(width,height,TextureFormat.Rgba32f,pixels);
        // Act: Dispatch production metering with the selected scale and weighting policy.
        rig.Meter(scene,Settings() with {CenterWeighted=weighted},publication.Buffer);
        var actual=rig.ReadHistogram();
        // Assert: Every encoded bin count and moment matches the oracle within its population-specific bound.
        var counts=new double[64];var sums=new double[64];var populations=new int[64];
        for(int y=0;y<height;y+=2)for(int x=0;x<width;x+=2)
        {
            double total=0;int count=0;bool valid=true;
            for(int dy=0;dy<2&&y+dy<height;dy++)for(int dx=0;dx<2&&x+dx<width;dx++)
            {
                float value=pixels[((y+dy)*width+x+dx)*4];
                valid &= float.IsFinite(value);total+=Math.Max(value,0);count++;
            }
            if(!valid)continue;
            double level=Math.Log2(total/count/scale);
            double cx=2*(x+Math.Min(2,width-x)*.5)/width-1;
            double cy=2*(y+Math.Min(2,height-y)*.5)/height-1;
            double weight=weighted?.25+.75*Math.Exp(-2*(cx*cx+cy*cy)):1;
            AccumulateInterpolated(counts,sums,populations,level,weight);
        }
        for(int bin=0;bin<64;bin++)
        {
            double count=0,sum=0;
            for(int group=0;group<CameraExposureTargets.HistogramGroupCount;group++){count+=actual[(group*64+bin)*2];sum+=actual[(group*64+bin)*2+1];}
            int samples=((width+1)/2)*((height+1)/2);
            double encodingScale=Math.Min(4096,Math.Floor(1073741824d/(((samples+CameraExposureTargets.HistogramGroupCount*64-1)/(CameraExposureTargets.HistogramGroupCount*64))*64*128d)));
            double weightBound=populations[bin]/encodingScale+.001;
            double sumBound=populations[bin]*.005+.001;
            Assert.InRange(count,counts[bin]-weightBound,counts[bin]+weightBound);
            Assert.InRange(sum,sums[bin]-sumBound,sums[bin]+sumBound);
        }
    }
    #endregion
    /// <summary>Fractional populations move continuously across a bin boundary while preserving count and actual log moment.</summary>
    [Fact]
    public void AdjacentBinSweepConservesPopulationAndActualLogMoment()
    {
        // Arrange: Sweep luminance values on, between, and beyond histogram bin boundaries.
        EnsureShaderTestAvailable();

        using var rig=CreateRig();
        double[] coordinates=[.001,.01,31.98,31.99,32,32.01,32.02,62.99,62.999];
        double[]? previous=null;double previousCoordinate=0;
        // Act and assert: Meter each value and verify interpolation conserves count and log moment across adjacent bins.
        foreach(double coordinate in coordinates)
        {
            float value=(float)Math.Pow(2,-16+coordinate*56/63);
            double level=Math.Log2(value),position=(level+16)/56*63;
            int low=(int)Math.Floor(position);double fraction=position-low;
            using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[value,value,value,1f]);
            rig.Meter(source,Settings());
            float[] actual=rig.ReadHistogram();
            var counts=new double[64];double sum=0;
            for(int group=0;group<CameraExposureTargets.HistogramGroupCount;group++)for(int bin=0;bin<64;bin++)
            {
                counts[bin]+=actual[(group*64+bin)*2];sum+=actual[(group*64+bin)*2+1];
            }
            Assert.InRange(counts.Sum(),.999999,1.000001);
            Assert.All(counts,value=>Assert.InRange(value,0,1));
            Assert.InRange(sum,level-.00015,level+.00015);
            Assert.InRange(counts[low],1-fraction-.0003,1-fraction+.0003);
            Assert.InRange(counts[Math.Min(low+1,63)],fraction-.0003,fraction+.0003);
            for(int bin=0;bin<64;bin++)
                if(bin!=low&&bin!=low+1)Assert.Equal(0,counts[bin]);
            if(previous is not null && coordinate-previousCoordinate<.02)
                Assert.InRange(counts.Zip(previous,(a,b)=>Math.Abs(a-b)).Sum(),0,.041);
            previous=counts;previousCoordinate=coordinate;
        }
        foreach(float level in new[]{-16f,40f})
        {
            float value=MathF.Pow(2,level);

            using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[value,value,value,1f]);
            rig.Meter(source,Settings());
            Assert.All(rig.ReadHistogram(),entry=>Assert.Equal(0,entry));
        }
    }
    #region Adaptation
    /// <summary>Physical-lighting defaults settle a twelve-stop transition without a long exposure wait.</summary>
    [Theory]
    [InlineData(12f, 3.5f)]
    [InlineData(-12f, 1.75f)]
    public void DefaultRatesSettleLargeLightingTransitions(float target, float seconds)
    {
        // Arrange: Publish a twelve-stop target using the default bounded response rates.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        rig.PublishTarget(target);
        var settings = new CameraExposureSettings { MinEV100 = -20, MaxEV100 = 20 }.Snapshot();
        // Eleven stops use the bounded linear response; the last stop settles exponentially.
        // Act: Adapt once over the full duration and again over one hundred equal subframes.
        float actual = rig.Adapt(settings, 0, seconds, false);
        // Assert: Both paths settle near the target and agree despite different frame partitioning.
        Assert.InRange(MathF.Abs(target - actual), 0, .051f);
        float split = 0;
        for (int frame = 0; frame < 100; frame++)
            split = rig.Adapt(settings, split, seconds / 100, false);
        AssertClose(actual, split, .00005f);
    }

    /// <summary>Separate response rates bound distant changes and converge exponentially within one stop.</summary>
    [Theory]
    [InlineData(2f, .5f, .5f)]
    [InlineData(-2f, .2f, -.6f)]
    public void AdaptationUsesSeparateBoundedRates(float target, float dt, float expected)
    {
        // Arrange: Publish distant target values selected to exercise brighten, darken, and exponential response.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        rig.PublishTarget(target);
        // Act and assert: Check the bounded first transition and the one-stop exponential convergence case.
        AssertClose(expected, rig.Adapt(Settings(), 0, dt, false));
        rig.PublishTarget(.5f);
        AssertClose(.5f * (1 - MathF.Exp(-1)), rig.Adapt(Settings(), 0, 1, false));
    }

    /// <summary>Splitting elapsed time preserves the same result even across the linear-to-exponential transition.</summary>
    [Fact]
    public void AdaptationIsInvariantToFramePartitionAndHonorsBounds()
    {
        // Arrange: Set distinct brighten/darken rates and explicit minimum/maximum gain limits.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        var settings = new CameraExposureSettings { MinEV100 = -12, MaxEV100 = 12, BrightenRate = 2, DarkenRate = 3, LowPercentile = 0, HighPercentile = 1, CenterWeighted = false }.Snapshot();
        rig.PublishTarget(4);
        // Act: Compare one three-second update with split updates, then exercise negative time and both clamps.
        float full = rig.Adapt(settings, -2, 3, false);
        // Assert: Time partitioning preserves the response and invalid elapsed time leaves exposure unchanged.
        float split = -2;
        for (int i = 0; i < 30; i++) split = rig.Adapt(settings, split, .1f, false);
        AssertClose(4 - MathF.Exp(-1), full);
        AssertClose(full, split, .00003f);
        AssertClose(-2, rig.Adapt(settings, -2, -1, false));
        rig.PublishTarget(8);
        AssertClose(3, rig.Adapt(settings with { MaxLogGain = 3 }, 0, 0, true));
        rig.PublishTarget(-8);
        AssertClose(-3, rig.Adapt(settings with { MinLogGain = -3 }, 0, 0, true));
    }

    /// <summary>One production program consumes independent shared camera durations without mutating either view snapshot.</summary>
    [Fact]
    public void AlternateSharedFramesKeepIndependentAdaptationTime()
    {
        // Arrange: Create two distinct camera frame snapshots and retain their original bytes.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var first = TestFrameCamera.CreateIdentity(1920, 1080, .5f);
        using var second = TestFrameCamera.CreateIdentity(640, 360, .2f);
        byte[] firstBytes = first.Bytes.ToArray(), secondBytes = second.Bytes.ToArray();
        rig.PublishTarget(2);
        // Act and assert: Alternate one shared adaptation owner between views without mutating either snapshot.
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
        // Arrange: Publish an empty histogram and configure manual gain, compensation, and disabled variants.
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        var settings = Settings() with { ManualLogGain = 1, Compensation = .5f };
        rig.PublishEmpty();
        // Act and assert: Check empty-history retention, manual/reset behavior, disabled bounds, and later valid metering.
        AssertClose(-2, rig.Adapt(settings, -2, 10, false));
        AssertClose(1.5f, rig.Adapt(settings, -2, 10, true));
        AssertClose(1.5f, rig.Adapt(settings, float.NaN, 10, false));
        AssertClose(1.5f, rig.Adapt(settings with { Enabled = false }, -2, 10, false));
        AssertClose(1, rig.Adapt(settings with { Enabled = false, MaxLogGain = 1 }, -2, 10, false));
        rig.PublishTarget(2);
        AssertClose(2.5f, rig.Adapt(settings, -2, 0, true));
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion

    #region Private
    /// <summary>Distributes ideal double-precision population and actual log moments between adjacent bins.</summary>
    private static void AccumulateInterpolated(double[] counts,double[] sums,int[] populations,double level,double weight)
    {
        double coordinate=Math.Clamp((level+16)/56*63,0,63);
        int low=(int)Math.Floor(coordinate),high=Math.Min(low+1,63);
        double fraction=coordinate-low;
        counts[low]+=weight*(1-fraction);sums[low]+=weight*(1-fraction)*level;populations[low]++;
        counts[high]+=weight*fraction;sums[high]+=weight*fraction*level;populations[high]++;
    }
    /// <summary>Uses broad exposure bounds and uniform full-population metering unless a test supplies another policy.</summary>
    private static CameraExposureParameters Settings() => new(true, 0, 0, -12, 12, .18f, 0, 1, 1, 3, false);
    /// <summary>Builds controlled RGB samples with deliberately varying alpha that metering must ignore.</summary>
    private static float[] Pixels(Func<int, float> luminance)
    {
        var result = new float[128 * 72 * 4];
        for (int i = 0; i < 128 * 72; i++)
        {
            float value = luminance((i/128/2)*64+(i%128/2));
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
        private float histogramScale=4096;

        #region Public API
        /// <summary>Releases all retained histogram and temporal storage.</summary>
        public void Dispose() => targets.Dispose();
        #endregion

        #region Internal API
        /// <summary>Allocates the production-sized histogram and scalar adaptation storage.</summary>
        internal ExposureRig(CameraHistogramShaderProgram meter, CameraAdaptShaderProgram adapt)
        {
            this.meter = meter; this.adapt = adapt;

        }
        /// <summary>Runs dense downscaled scene metering without modifying its source image.</summary>
        internal void Meter(GpuTexture texture, CameraExposureParameters settings, GpuShaderStorageBuffer? publication = null)
        {
            int samples=((texture.Width+1)/2)*((texture.Height+1)/2);
            int groupBound=((samples+CameraExposureTargets.HistogramGroupCount*64-1)/(CameraExposureTargets.HistogramGroupCount*64))*64;
            histogramScale=Math.Min(4096,MathF.Floor(1073741824f/(groupBound*128f)));
            if (publication is not null) meter.FrameLightingInputs = publication;
            meter.SceneRadiance = texture; meter.Capture(settings, false);
            meter.Histogram = targets.Histogram;
            meter.Dispatch(CameraExposureTargets.HistogramGroupCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        /// <summary>Publishes an independent single-bin population whose mean implies the requested exposure target.</summary>
        internal void PublishTarget(float target)
        {
            uint[] values = new uint[CameraExposureTargets.HistogramGroupCount * 128];
            values[64] = 4096; values[65] = (uint)MathF.Round((MathF.Log2(.18f) - target + 16) / 56 * 4096 * 128);
            targets.Histogram.UploadData(values);
        }
        /// <summary>Publishes an empty population to exercise missing scene statistics.</summary>
        internal void PublishEmpty()
        {
            targets.Histogram.UploadData(new uint[CameraExposureTargets.HistogramGroupCount * 128]);
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
        /// <summary>Decodes every workgroup bin for independent population and log-sum checks.</summary>
        internal float[] ReadHistogram()
        {
            uint[] encoded = ExposureTestStorage.Read<uint>(targets.Histogram);
            float[] decoded = new float[encoded.Length];
            for (int i = 0; i < encoded.Length; i += 2)
            {
                decoded[i] = encoded[i] / histogramScale;
                decoded[i + 1] = encoded[i + 1] / (histogramScale * 128f) * 56 - 16 * decoded[i];
            }
            return decoded;
        }
        #endregion
    }
    #endregion

}
