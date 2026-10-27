using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Opt-in full-resolution correctness and warm component cost for dense exposure metering.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CameraExposureMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks a maximally contended bin before timing the production histogram and adaptation pair.</summary>
    [Theory]
    [InlineData(64,36,false)]
    [InlineData(320,180,false)]
    [InlineData(1280,720,false)]
    [InlineData(1920,1080,false)]
    [InlineData(3840,2160,false)]
    [InlineData(1280,720,true)]
    [InlineData(1920,1080,true)]
    [InlineData(3840,2160,true)]
    [InlineData(4097,2161,true)]
    public void DenseHistogramWarmCost(int width,int height,bool weighted)
    {
        if(Environment.GetEnvironmentVariable("VGE_RUN_EXPOSURE_MEASUREMENTS")!="1")return;
        EnsureShaderTestAvailable();
        var meter=Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adapt=Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        using var targets=new CameraExposureTargets();
        var pixels=new float[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            // Exact binary fractions avoid source-half quantization in the independent expectation.
            float level=weighted?MathF.Pow(2,((x/2+y/2)%17)-8):32;
            int i=(y*width+x)*4;
            pixels[i]=pixels[i+1]=pixels[i+2]=level;pixels[i+3]=1;
        }
        using var source=TestFramework.CreateTexture(width,height,TextureFormat.Rgba16f,pixels);
        using var camera=TestFrameCamera.CreateIdentity(width,height,.1f);
        var settings=new CameraExposureSettings().Snapshot() with {CenterWeighted=weighted,LowPercentile=0,HighPercentile=1};
        meter.SceneRadiance=source;meter.Histogram=targets.Histogram;meter.Capture(settings,true);
        adapt.Histogram=targets.Histogram;adapt.PreviousExposure=targets.Exposure;adapt.OutputExposure=targets.WriteTarget;adapt.FrameInputs=camera;adapt.Capture(settings,true);
        /// <summary>Submits the dependent production pair through its resource boundary.</summary>
        void Compute()=>Assert.True(CameraExposureDispatch.TryRun(meter,adapt));
        Compute();
        uint[] bins=ExposureTestStorage.Read<uint>(targets.Histogram);
        int samples=((width+1)/2)*((height+1)/2),groupBound=((samples+CameraExposureTargets.HistogramGroupCount*64-1)/(CameraExposureTargets.HistogramGroupCount*64))*64;
        uint weightScale=(uint)Math.Min(4096,Math.Floor(1073741824d/(groupBound*128d)));
        ulong count=0;for(int i=0;i<bins.Length;i+=2)count+=bins[i];
        double referenceSum=0;ulong referenceCount=0;
        for(int y=0;y<height;y+=2)for(int x=0;x<width;x+=2)
        {
            double cx=2d*(x+Math.Min(2,width-x)*.5)/width-1;
            double cy=2d*(y+Math.Min(2,height-y)*.5)/height-1;
            double weight=weighted?.25+.75*Math.Exp(-2*(cx*cx+cy*cy)):1;
            uint encodedWeight=(uint)Math.Round(weight*weightScale);
            referenceCount+=encodedWeight;
            referenceSum+=encodedWeight*Math.Log2(pixels[(y*width+x)*4]);
        }
        if(!weighted)Assert.Equal(referenceCount,count);
        else Assert.InRange((double)count,(double)referenceCount-samples,(double)referenceCount+samples);
        float actual=ExposureTestStorage.Read<float>(targets.WriteTarget)[0];
        float expected=Math.Clamp(MathF.Log2(.18f)-(float)(referenceSum/referenceCount),settings.MinLogGain,settings.MaxLogGain);
        Assert.InRange(actual,expected-.0005f,expected+.0005f);
        for(int i=0;i<3;i++)Compute();
        var gpu=new List<double>();var cpu=new List<double>();
        for(int i=0;i<8;i++)Measure(Compute,gpu,cpu);
        var histogramGpu=new List<double>();var adaptationGpu=new List<double>();var stageCpu=new List<double>();
        for(int i=0;i<8;i++)
        {
            Measure(()=>{meter.Dispatch(CameraExposureTargets.HistogramGroupCount);GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);},histogramGpu,stageCpu);
            Measure(()=>{adapt.Dispatch(1);GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);},adaptationGpu,stageCpu);
        }
        /// <summary>Reports the middle pair of eight warm samples.</summary>
        double Median(List<double> values)=>values.Order().Skip(3).Take(2).Average();
        log.WriteLine($"Dense exposure {width}x{height}, weighted={weighted}, {samples} half-resolution boxes, weightScale={weightScale}: pair GPU={Median(gpu):F4}ms CPU submission={Median(cpu):F4}ms histogram GPU={Median(histogramGpu):F4}ms adaptation GPU={Median(adaptationGpu):F4}ms; histogram={CameraExposureTargets.HistogramGroupCount*512}bytes two history buffers=64bytes; no intermediate texture. Synthetic component timing, not live frame cost. GPU={GL.GetString(StringName.Renderer)}.");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Separates completed GPU execution from CPU submission without timing uploads or readbacks.</summary>
    private static void Measure(Action submit,List<double> gpu,List<double> cpu)
    {
        using var timer=GpuTimerQuery.Create();timer.Begin();
        long start=Stopwatch.GetTimestamp();submit();cpu.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        timer.End();gpu.Add(timer.GetResultNanoseconds()/1e6);
    }
    #endregion
}
