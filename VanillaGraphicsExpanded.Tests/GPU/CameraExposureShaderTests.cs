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
        var supported=GpuSupport.Graphics with {MaxComputeWorkGroupInvocations=256,MaxComputeWorkGroupSize=System.Collections.Immutable.ImmutableArray.Create(256,1,1),MaxComputeWorkGroupCount=System.Collections.Immutable.ImmutableArray.Create(CameraExposureTargets.HistogramGroupCount,1,1),MaxComputeSharedMemorySize=2560,MaxShaderStorageBufferBindings=3,MaxFragmentShaderStorageBlocks=1,MaxComputeShaderStorageBlocks=3,MaxShaderStorageBlockSize=CameraExposureTargets.HistogramGroupCount*512};
        Assert.True(CameraExposureDispatch.Supports(supported));
        GraphicsCapabilities[] insufficient=[supported with {SupportsArbComputeShader=false},supported with {SupportsArbShaderImageLoadStore=false},supported with {SupportsArbShaderStorageBufferObject=false},supported with {SupportsArbGlSpirv=false},supported with {MaxComputeWorkGroupInvocations=255},supported with {MaxComputeWorkGroupSize=System.Collections.Immutable.ImmutableArray.Create(255,1,1)},supported with {MaxComputeWorkGroupCount=System.Collections.Immutable.ImmutableArray.Create(CameraExposureTargets.HistogramGroupCount-1,1,1)},supported with {MaxComputeSharedMemorySize=2559},supported with {MaxShaderStorageBufferBindings=2},supported with {MaxFragmentShaderStorageBlocks=0},supported with {MaxComputeShaderStorageBlocks=2},supported with {MaxShaderStorageBlockSize=CameraExposureTargets.HistogramGroupCount*512-1}];
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
        AssertClose(1, bins.Where((_, i) => i % 2 == 0).Sum(), .001f);
        AssertClose(5, bins.Where((_, i) => i % 2 == 1).Sum(), .06f);
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
        using var source = TestFramework.CreateTexture(128, 72, PixelInternalFormat.Rgba32f, pixels);
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
        using var source=TestFramework.CreateTexture(128,72,PixelInternalFormat.Rgba32f,pixels);
        rig.Meter(source,Settings() with {CenterWeighted=weighted});
        float[] actual=rig.ReadHistogram();
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
        EnsureShaderTestAvailable();using var rig=CreateRig();
        float value=MathF.Pow(2,39.99f);
        using var source=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,
            Enumerable.Range(0,width*height).SelectMany(_=>new[]{value,value,value,1f}).ToArray());
        rig.Meter(source,Settings());float[] bins=rig.ReadHistogram();
        int samples=((width+1)/2)*((height+1)/2);
        AssertClose(samples,bins.Where((_,i)=>i%2==0).Sum(),.01f);
        AssertClose(39.99f,bins.Where((_,i)=>i%2==1).Sum()/samples,.001f);
        for(int group=0;group<CameraExposureTargets.HistogramGroupCount;group++)
            for(int bin=0;bin<62;bin++){Assert.Equal(0,bins[(group*64+bin)*2]);Assert.Equal(0,bins[(group*64+bin)*2+1]);}
        AssertClose(-12,rig.Adapt(Settings(),0,0,true));
        using var empty=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,1f]);
        rig.Meter(empty,Settings());
        Assert.All(rig.ReadHistogram(),value=>Assert.Equal(0,value));
    }
    /// <summary>Percentile trimming removes sparse sun/dark outliers and retains the geometric scene mean.</summary>
    [Fact]
    public void PercentilesRejectSolarOutliersAndWeightPartialBins()
    {
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        using var outliers = TestFramework.CreateTexture(128, 72, PixelInternalFormat.Rgba32f,
            Pixels(i => i < 216 ? .001f : i >= 2304 - 216 ? 4096 : .18f));
        var trimmed = Settings() with { LowPercentile = .1f, HighPercentile = .9f };
        rig.Meter(outliers, trimmed);
        AssertClose(0, rig.Adapt(trimmed, 0, 0, true));
        Assert.True(MathF.Abs(rig.Adapt(Settings(), 0, 0, true)) > .25f);
        using var partial = TestFramework.CreateTexture(128, 72, PixelInternalFormat.Rgba32f, Pixels(i => i < 576 ? .125f : 8));
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
        using var equal = TestFramework.CreateTexture(128, 72, PixelInternalFormat.Rgba32f, Pixels(i => i < 1152 ? 1 : 4));
        rig.Meter(equal, Settings());
        AssertClose(MathF.Log2(.18f) - 1, rig.Adapt(Settings(), 0, 0, true));
        using var central = TestFramework.CreateTexture(128, 72, PixelInternalFormat.Rgba32f,
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
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        var pixels=Pixels(_=>.18f);
        pixels[0]=pixels[1]=pixels[2]=64;
        using var scene=TestFramework.CreateTexture(128,72,PixelInternalFormat.Rgba32f,pixels);
        rig.Meter(scene,Settings());
        var histogram=rig.ReadHistogram();
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
        using var scene=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,pixels);
        rig.Meter(scene,Settings() with {CenterWeighted=weighted},publication.Buffer);
        var actual=rig.ReadHistogram();
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
        EnsureShaderTestAvailable();
        using var rig=CreateRig();
        double[] coordinates=[.001,.01,31.98,31.99,32,32.01,32.02,62.99,62.999];
        double[]? previous=null;double previousCoordinate=0;
        foreach(double coordinate in coordinates)
        {
            float value=(float)Math.Pow(2,-16+coordinate*56/63);
            double level=Math.Log2(value),position=(level+16)/56*63;
            int low=(int)Math.Floor(position);double fraction=position-low;
            using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[value,value,value,1f]);
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
            using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[value,value,value,1f]);
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
        EnsureShaderTestAvailable();
        using var rig = CreateRig();
        rig.PublishTarget(target);
        var settings = new CameraExposureSettings().Snapshot() with { MinEV = -20, MaxEV = 20 };
        // Eleven stops use the bounded linear response; the last stop settles exponentially.
        float actual = rig.Adapt(settings, 0, seconds, false);
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
                decoded[i] = encoded[i] / histogramScale;
                decoded[i + 1] = encoded[i + 1] / (histogramScale * 128f) * 56 - 16 * decoded[i];
            }
            return decoded;
        }
    }
    #endregion
}
