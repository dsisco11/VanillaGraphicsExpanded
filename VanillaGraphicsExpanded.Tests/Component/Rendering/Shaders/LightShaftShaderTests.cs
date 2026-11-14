using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Checks independently extracted HDR solar glare and distance visibility through production GPU passes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LightShaftShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    private PostprocessDraw? draw;
    private VgeFrameUniformBuffer? camera;

    #region Public API
    /// <summary>Exposed peak limits preserve HDR source color and manual or automatic camera exposure.</summary>
    [Theory]
    [InlineData(-2f,false,1f)]
    [InlineData(0f,false,1f)]
    [InlineData(2f,true,1f)]
    [InlineData(2f,true,.25f)]
    public void ExtractionBoundsExposedDiskAndPreservesColor(float ev,bool automatic,float scale)
    {
        // Arrange: Select camera EV/mode and storage scale for a controlled exposed solar disk.
        // Act and assert: Run extraction and verify the bounded disk preserves source color at the selected exposure.
        EnsureShaderTestAvailable();
        var shader=Programs.Create<LightShaftShaderProgram>();
        using var publication = new TestFrameLightingPublication(); publication.Capture(scale);
        shader.FrameLightingInputs = publication.Buffer;
        float[] radiance=Enumerable.Repeat(new[]{1000f*scale,500f*scale,250f*scale,.37f},65*33).SelectMany(x=>x).ToArray();
        using var scene=TestFramework.CreateTexture(65,33,TextureFormat.Rgba32f,radiance);
        using var mask=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,1000f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,TextureFormat.R32f,[1f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var history = ExposureTestStorage.Create(ev);
        using var target=TestFramework.CreateTestGBuffer(65,33,TextureFormat.Rgba32f);
        shader.SourceImage=scene;shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureBuffer=history;
        shader.CaptureExtraction(false, automatic, 1, 2, .5f, automatic ? -ev : ev,
            new(new(.5f, .5f), 1, 65f / 33, Vector3.One, 10));
        Draw(shader,target);
        float[] actual=target[0].ReadPixelsRegion(32, 16, 1, 1);float expected=MathF.Pow(2,-ev)*scale;
        Assert.InRange(actual[0],expected-.0001f,expected+.0001f);
        Assert.InRange(actual[1]/actual[0],.49999f,.50001f);
        Assert.InRange(actual[2]/actual[0],.24999f,.25001f);
        Assert.Equal(1,actual[3]);ImageAssertions.Comparisons.Compare(scene,radiance).AssertEqual();
    }

    /// <summary>Near geometry occludes distance visibility and solar extraction while publication keeps RGB and visibility independent.</summary>
    [Theory]
    [InlineData(.5f,1f)]
    [InlineData(1f,0f)]
    [InlineData(1f,1f)]
    public void DistanceVisibilityAndBloomPublishIndependently(float depthValue,float daylight)
    {
        // Arrange: Select scene depth and daylight while supplying distinct visibility and bloom source inputs.
        // Act and assert: Execute publication and verify distance visibility remains independent from RGB glare.
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var scene=TestFramework.CreateTexture(65,33,TextureFormat.Rgba32f,Enumerable.Repeat(new[]{1000f,500f,250f,1f},65*33).SelectMany(x=>x).ToArray());
        using var mask=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,1f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,TextureFormat.R32f,[depthValue]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var extracted=TestFramework.CreateTestGBuffer(65,33,TextureFormat.Rgba32f);
        using var bloom=TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        using var visibility=TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        shader.SourceImage=scene;shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureBuffer=neutralExposure;
        var sun=new LightShaftSource(new(.5f,.5f),daylight,65f/33,Vector3.One,10);
        shader.CaptureExtraction(false,false,1,2,.5f,0,sun);Draw(shader,extracted);
        float[] source=extracted[0].ReadPixelsRegion(32, 16, 1, 1);
        if(depthValue==1&&daylight==1)Assert.True(source[0]>.9f);
        if(daylight==0)Assert.Equal(0,source[0]);
        if(depthValue<1) {Assert.Equal(0,source[0]);Assert.InRange(source[3],0,.01f);} else if(daylight>0) Assert.Equal(1,source[3]);
        shader.SourceImage=extracted[0];shader.CaptureResolve(false,sun);Draw(shader,bloom);
        shader.CaptureResolve(true,sun);Draw(shader,visibility);
        float[] light=bloom[0].ReadPixels(),maskResult=visibility[0].ReadPixels();
        Assert.Equal(source[..3],light[..3]);Assert.Equal(1,light[3]);
        float expected=daylight==0?0:1-source[3];
        for(int i=0;i<3;i++)Assert.InRange(maskResult[i],expected-.00001f,expected+.00001f);
        Assert.Equal(1,maskResult[3]);Assert.NotEqual(bloom[0].TextureId,visibility[0].TextureId);
    }

    /// <summary>Normalized radial samples preserve constant fields and assign clear visibility to offscreen samples.</summary>
    [Fact]
    public void RadialFilterNormalizesAndHandlesOutsideScreen()
    {
        // Arrange: Supply constant radial input plus samples that project outside the screen.
        // Act and assert: Filter the field and verify normalized constant response and clear offscreen visibility.
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[4f,2f,1f,.25f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var target=TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        shader.SourceImage=source;shader.VisibilityImage=source;shader.DepthImage=source;shader.ExposureBuffer=neutralExposure;
        shader.CaptureFilter(3,1,new(new(.5f,.5f),1,2,Vector3.One));Draw(shader,target);
        Assert.Equal(new[]{4f,2f,1f,.25f},target[0].ReadPixels());
        shader.CaptureFilter(3,1,new(new(3,.5f),1,2,Vector3.One));Draw(shader,target);
        float[] outside=target[0].ReadPixels();Assert.InRange(outside[0],4f/3-.00001f,4f/3+.00001f);Assert.Equal(.75f,outside[3]);
    }

    /// <summary>Radial traversal spreads a finite HDR source symmetrically on a nonsquare viewport without increasing its peak.</summary>
    [Fact]
    public void RadialFilterProducesBoundedSpatialHalo()
    {
        // Arrange: Place finite HDR energy at one point on a nonsquare viewport.
        // Act and assert: Run radial traversal and verify a symmetric bounded halo without peak amplification.
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        const int width=65,height=33;float[] pixels=new float[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y*width+x)*4;pixels[i+3]=1;if(Math.Abs(x-32)<=2&&Math.Abs(y-16)<=2)pixels[i]=8;}
        using var source=TestFramework.CreateTexture(width,height,TextureFormat.Rgba32f,pixels);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var target=TestFramework.CreateTestGBuffer(width,height,TextureFormat.Rgba32f);
        shader.SourceImage=source;shader.VisibilityImage=source;shader.DepthImage=source;shader.ExposureBuffer=neutralExposure;
        shader.CaptureFilter(32,.75f,new(new(.5f,.5f),1,(float)width/height,Vector3.One));Draw(shader,target);
        Assert.True(target[0].ReadPixelsRegion(38,16,1,1)[0]>0);
        Assert.True(target[0].ReadPixelsRegion(32,22,1,1)[0]>0);
        ImageAssertions.Predicates.Predicate(target[0], LightingPredicates.LightShaftShaderTests_RadialFilterProducesBoundedSpatialHalo).AssertEqual();
    }
    /// <summary>Measures a bounded four-pass shaft extraction/filter/publication workload without bloom or live-frame claims.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void SyntheticLightShaftPassTiming()
    {
        // Arrange: Prepare fixed synthetic input and the four production shaft passes before timing.
        // Act and report: Warm and measure the bounded workload without bloom or live-frame claims.
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[1000f,500f,250f,1f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var mask=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,1f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,TextureFormat.R32f,[1f]);
        using var a=TestFramework.CreateTestGBuffer(320,180,TextureFormat.Rgba16f);
        using var b=TestFramework.CreateTestGBuffer(320,180,TextureFormat.Rgba16f);
        using var published=TestFramework.CreateTestGBuffer(320,180,TextureFormat.Rgba16f);
        shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureBuffer=neutralExposure;
        var sun=new LightShaftSource(new(.5f,.5f),1,16f/9,Vector3.One,128);var timings=new List<double>();
        for(int sample=-1;sample<5;sample++)
        {
            using var timer=GpuTimerQuery.Create();timer.Begin();
            shader.SourceImage=source;shader.CaptureExtraction(false,false,1,2,.5f,0,sun);Draw(shader,a);
            shader.SourceImage=a[0];shader.CaptureFilter(16,.12f,sun);Draw(shader,b);
            shader.SourceImage=b[0];shader.CaptureFilter(16,.36f,sun);Draw(shader,a);
            shader.SourceImage=a[0];shader.CaptureResolve(false,sun);Draw(shader,published);
            timer.End();double ms=timer.GetResultNanoseconds()/1e6;if(sample>=0)timings.Add(ms);
        }
        // Report the warmed synthetic pass timings and payload; no live-frame performance threshold is asserted.
        output.WriteLine($"Synthetic light shafts 320x180 (quarter 1280x720), four production shader passes, 16 radial samples, uniform 1x1 sources: min={timings.Min():F4}ms median={timings.Order().ElementAt(2):F4}ms max={timings.Max():F4}ms. Three RGBA16F target payload={320*180*8*3}bytes. Not live frame cost or bandwidth.");
    }
    #endregion
    #region Private
    /// <summary>Submits a production procedural triangle with deterministic state after readback.</summary>
    private void Draw(LightShaftShaderProgram shader,GpuFramebuffer target)
    {
        if (camera is null)
        {
            float[] projection = Vintagestory.API.MathTools.Mat4f.Create();
            projection[10] = -1.002f; projection[14] = -.2002f; projection[11] = -1; projection[15] = 0;
            camera = TestFrameCamera.CreateFromProjection(projection, target.Width, target.Height, .1f, 100);
        }
        shader.FrameInputs = camera;
        draw??=new PostprocessDraw();
        var pipeline=draw.Prepare(shader,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope59=commands.EnterBoundary(new("Tests.Postprocess",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope59.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Protected API
    /// <summary>Retires the retained graphics pipeline and geometry before the test shader owners.</summary>
    protected override void Dispose(bool disposing)
    {
        if(disposing){draw?.Dispose();draw=null;camera?.Dispose();camera=null;}
        base.Dispose(disposing);
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-lighting", 0, 0);
    }
    #endregion
}
