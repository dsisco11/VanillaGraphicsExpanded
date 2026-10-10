using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Compares the single dispatch with the retired raster chain at identical synthetic workloads.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class DepthHierarchyMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Measures warm GPU work and CPU submissions after verifying identical complete mip chains.</summary>
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(3840,2160)]
    public void MatchedRasterAndComputeWorkloads(int width,int height)
    {
        EnsureShaderTestAvailable();
        using var compute=new DepthHierarchyPass();
        using var raster=new RasterDepthHierarchyReference();
        using var draw=new PostprocessDraw();
        using var source=TestFramework.CreateTexture(width,height,PixelInternalFormat.R32f,
            Enumerable.Range(0,width*height).Select(i=>.1f+.8f*((i*31)%101)/101f).ToArray());
        compute.Prepare(width,height,Programs.CreateDepthHierarchy());
        var pipelines=raster.Prepare(draw,width,height,Programs.Create<RasterDepthCopyShaderProgram>(),Programs.Create<RasterDepthReductionShaderProgram>());
        /// <summary>Submits the former chain through its graphics boundary.</summary>
        void RenderRaster() {
            Assert.True(GraphicsCommandContext.TryRun("Tests.HzbRasterReference",pipelines,true,
                commands=>raster.Render(commands,draw,source)));
        }
        compute.Render(source); RenderRaster();
        for(int level=0;level<compute.Texture!.MipLevels;level++)
            Assert.Equal(raster.Texture!.ReadPixels(level),compute.Texture.ReadPixels(level));
        var rasterGpu=new List<double>(); var computeGpu=new List<double>();
        var rasterCpu=new List<double>(); var computeCpu=new List<double>();
        // Alternate ordering to avoid attributing a consistent first-run bias to either path.
        for(int sample=0;sample<8;sample++) {
            if((sample&1)==0) { Measure(RenderRaster,rasterGpu,rasterCpu); Measure(()=>compute.Render(source),computeGpu,computeCpu); }
            else { Measure(()=>compute.Render(source),computeGpu,computeCpu); Measure(RenderRaster,rasterGpu,rasterCpu); }
        }
        /// <summary>Reports the midpoint of the eight warm samples.</summary>
        double Median(List<double> samples)=>samples.Order().Skip(3).Take(2).Average();
        log.WriteLine($"HZB {width}x{height}: raster GPU median={Median(rasterGpu):F4}ms CPU submission median={Median(rasterCpu):F4}ms; compute GPU median={Median(computeGpu):F4}ms CPU submission median={Median(computeCpu):F4}ms; hierarchy bytes={compute.StorageBytes}; scratch bytes={compute.ScratchBytes}; raster draws/framebuffers={compute.Texture.MipLevels}; compute dispatches=1 framebuffer targets=0 tail transfers={Math.Max(0,compute.Texture.MipLevels-7)}; GPU={GL.GetString(StringName.Renderer)}.");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Separates CPU submission duration from the completed GPU elapsed query.</summary>
    private static void Measure(Action render,List<double> gpu,List<double> cpu)
    {
        using var timer=GpuTimerQuery.Create(); timer.Begin();
        long start=Stopwatch.GetTimestamp(); render();
        cpu.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        timer.End(); gpu.Add(timer.GetResultNanoseconds()/1e6);
    }
    #endregion
}
