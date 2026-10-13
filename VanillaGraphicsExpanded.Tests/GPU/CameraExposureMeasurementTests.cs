using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Compares retained raster exposure against compute at identical fixed-grid workloads.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CameraExposureMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Verifies numerical agreement before measuring warm submission and completed GPU work.</summary>
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(3840,2160)]
    public void MatchedRasterAndComputeWorkloads(int width,int height)
    {
        EnsureShaderTestAvailable();
        var meter=Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adapt=Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        var rasterMeter=Programs.Create<RasterCameraExposureShader>();
        var rasterAdapt=Programs.Create<RasterCameraAdaptShader>();
        using var targets=new CameraExposureTargets();
        using var histogram=TestFramework.CreateTestGBuffer(64,1,PixelInternalFormat.Rg32f);
        using var result=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.R32f);
        using var previous=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[0f]);
        using var source=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,
            Enumerable.Range(0,width*height).SelectMany(i=>new[]{.02f+((i*31)%101)*.03f,.04f+((i*7)%89)*.1f,.01f+((i*13)%67)*.02f,1f}).ToArray());
        using var camera=TestFrameCamera.CreateIdentity(width,height,.1f);
        using var draw=new PostprocessDraw();
        var settings=new CameraExposureSettings().Snapshot();
        meter.SceneRadiance=source;meter.Histogram=targets.Histogram;meter.Capture(settings,true);
        adapt.Histogram=targets.Histogram;adapt.PreviousExposure=targets.Exposure;adapt.OutputExposure=targets.WriteTarget;adapt.FrameInputs=camera;adapt.Capture(settings,true);
        rasterMeter.Source=source;rasterMeter.Capture(settings,true);
        rasterAdapt.Source=histogram[0];rasterAdapt.Previous=previous;rasterAdapt.Frame=camera;rasterAdapt.Capture(settings,true);
        var meterPipeline=draw.Prepare(rasterMeter,histogram);var adaptPipeline=draw.Prepare(rasterAdapt,result);
        /// <summary>Submits the retained production raster passes through the graphics boundary.</summary>
        void Raster()=>Assert.True(GraphicsCommandContext.TryRun("Tests.RasterExposure",[meterPipeline,adaptPipeline],true,commands=>{draw.Submit(commands,meterPipeline,histogram);draw.Submit(commands,adaptPipeline,result);}));
        /// <summary>Submits the dependent compute pair through its production resource boundary.</summary>
        void Compute()=>Assert.True(CameraExposureDispatch.TryRun(meter,adapt));
        Raster();Compute();
        float[] rasterBins=histogram[0].ReadPixels();uint[] computeBins=ExposureTestStorage.Read<uint>(targets.Histogram);
        for(int bin=0;bin<64;bin++)
        {
            float weight=0,shifted=0;
            for(int group=0;group<36;group++){int index=(group*64+bin)*2;weight+=computeBins[index]/4096f;shifted+=computeBins[index+1]/1048576f;}
            // Conservative accumulated encoding bounds include weight rounding and its shifted-log contribution.
            Assert.InRange(weight,rasterBins[bin*2]-.282f,rasterBins[bin*2]+.282f);
            float sum=shifted-16*weight;
            Assert.InRange(sum,rasterBins[bin*2+1]-3.4f,rasterBins[bin*2+1]+3.4f);
        }
        float expected=result[0].ReadPixels()[0],actual=ExposureTestStorage.Read<float>(targets.WriteTarget)[0];
        Assert.InRange(actual,expected-.0003f,expected+.0003f);
        var rasterGpu=new List<double>();var rasterCpu=new List<double>();var computeGpu=new List<double>();var computeCpu=new List<double>();
        for(int i=0;i<8;i++)
            if((i&1)==0){Measure(Raster,rasterGpu,rasterCpu);Measure(Compute,computeGpu,computeCpu);}
            else {Measure(Compute,computeGpu,computeCpu);Measure(Raster,rasterGpu,rasterCpu);}
        var histogramGpu=new List<double>();var adaptationGpu=new List<double>();var stageCpu=new List<double>();
        for(int i=0;i<8;i++)
        {
            Measure(()=>{meter.Dispatch(36);GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);},histogramGpu,stageCpu);
            Measure(()=>{adapt.Dispatch(1);GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);},adaptationGpu,stageCpu);
        }
        /// <summary>Reports the middle pair of eight alternating samples.</summary>
        double Median(List<double> samples)=>samples.Order().Skip(3).Take(2).Average();
        log.WriteLine($"Exposure {width}x{height}, fixed 2304 samples: raster GPU median={Median(rasterGpu):F4}ms CPU submission median={Median(rasterCpu):F4}ms; compute GPU median={Median(computeGpu):F4}ms CPU submission median={Median(computeCpu):F4}ms; compute histogram GPU median={Median(histogramGpu):F4}ms adaptation GPU median={Median(adaptationGpu):F4}ms; raster storage=520bytes compute storage=18440bytes; raster draws=2 compute dispatches=2 framebuffer targets=0; EV error={actual-expected:G5}; GPU={GL.GetString(StringName.Renderer)}. Synthetic timings, not live frame cost.");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Separates synchronous elapsed GPU work from host submission duration.</summary>
    private static void Measure(Action submit,List<double> gpu,List<double> cpu)
    {
        using var timer=GpuTimerQuery.Create();timer.Begin();
        long start=Stopwatch.GetTimestamp();submit();cpu.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        timer.End();gpu.Add(timer.GetResultNanoseconds()/1e6);
    }
    #endregion
}
