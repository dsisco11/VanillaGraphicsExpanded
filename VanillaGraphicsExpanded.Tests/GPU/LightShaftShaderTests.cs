using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks independently extracted HDR solar glare and distance visibility through production GPU passes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LightShaftShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    private PostprocessDraw? draw;
    private VgeFrameUniformBuffer? camera;

    #region Public API
    /// <summary>Exposed peak limits preserve HDR source color and manual or automatic camera exposure.</summary>
    [Theory]
    [InlineData(-2f,false)]
    [InlineData(0f,false)]
    [InlineData(2f,true)]
    public void ExtractionBoundsExposedDiskAndPreservesColor(float ev,bool automatic)
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<LightShaftShaderProgram>();
        float[] radiance=Enumerable.Repeat(new[]{1000f,500f,250f,.37f},65*33).SelectMany(x=>x).ToArray();
        using var scene=TestFramework.CreateTexture(65,33,PixelInternalFormat.Rgba32f,radiance);
        using var mask=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,1000f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[1f]);
        using var history=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[ev]);
        using var target=TestFramework.CreateTestGBuffer(65,33,PixelInternalFormat.Rgba32f);
        shader.SourceImage=scene;shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureImage=history;
        shader.Capture(new(0,0,0,automatic?1:0),new(1,2,.5f,automatic?-ev:ev),new(.5f,.5f,1,65f/33),new(1,1,1,10));
        Draw(shader,target);
        float[] actual=target[0].ReadPixels().Skip((16*65+32)*4).Take(4).ToArray();float expected=MathF.Pow(2,-ev);
        Assert.InRange(actual[0],expected-.0001f,expected+.0001f);
        Assert.InRange(actual[1]/actual[0],.49999f,.50001f);
        Assert.InRange(actual[2]/actual[0],.24999f,.25001f);
        Assert.Equal(1,actual[3]);Assert.Equal(radiance,scene.ReadPixels());
    }

    /// <summary>Near geometry occludes distance visibility and solar extraction while publication keeps RGB and visibility independent.</summary>
    [Theory]
    [InlineData(.5f,1f)]
    [InlineData(1f,0f)]
    [InlineData(1f,1f)]
    public void DistanceVisibilityAndBloomPublishIndependently(float depthValue,float daylight)
    {
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var scene=TestFramework.CreateTexture(65,33,PixelInternalFormat.Rgba32f,Enumerable.Repeat(new[]{1000f,500f,250f,1f},65*33).SelectMany(x=>x).ToArray());
        using var mask=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,1f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[depthValue]);
        using var extracted=TestFramework.CreateTestGBuffer(65,33,PixelInternalFormat.Rgba32f);
        using var bloom=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var visibility=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        shader.SourceImage=scene;shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureImage=depth;
        var sun=new Vector4(.5f,.5f,daylight,65f/33);
        shader.Capture(new(0,0,0,0),new(1,2,.5f,0),sun,new(1,1,1,10));Draw(shader,extracted);
        float[] source=extracted[0].ReadPixels().Skip((16*65+32)*4).Take(4).ToArray();
        if(depthValue==1&&daylight==1)Assert.True(source[0]>.9f);
        if(daylight==0)Assert.Equal(0,source[0]);
        if(depthValue<1) {Assert.Equal(0,source[0]);Assert.InRange(source[3],0,.01f);} else if(daylight>0) Assert.Equal(1,source[3]);
        shader.SourceImage=extracted[0];shader.Capture(new(0,0,2,0),Vector4.Zero,sun);Draw(shader,bloom);
        shader.Capture(new(0,0,3,0),Vector4.Zero,sun);Draw(shader,visibility);
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
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[4f,2f,1f,.25f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source;shader.VisibilityImage=source;shader.DepthImage=source;shader.ExposureImage=source;
        shader.Capture(new(0,0,1,3),new(1,0,0,0),new(.5f,.5f,1,2));Draw(shader,target);
        Assert.Equal(new[]{4f,2f,1f,.25f},target[0].ReadPixels());
        shader.Capture(new(0,0,1,3),new(1,0,0,0),new(3,.5f,1,2));Draw(shader,target);
        float[] outside=target[0].ReadPixels();Assert.InRange(outside[0],4f/3-.00001f,4f/3+.00001f);Assert.Equal(.75f,outside[3]);
    }

    /// <summary>Radial traversal spreads a finite HDR source symmetrically on a nonsquare viewport without increasing its peak.</summary>
    [Fact]
    public void RadialFilterProducesBoundedSpatialHalo()
    {
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        const int width=65,height=33;float[] pixels=new float[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y*width+x)*4;pixels[i+3]=1;if(Math.Abs(x-32)<=2&&Math.Abs(y-16)<=2)pixels[i]=8;}
        using var source=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,pixels);
        using var target=TestFramework.CreateTestGBuffer(width,height,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source;shader.VisibilityImage=source;shader.DepthImage=source;shader.ExposureImage=source;
        shader.Capture(new(0,0,1,32),new(.75f,0,0,0),new(.5f,.5f,1,(float)width/height));Draw(shader,target);
        float[] result=target[0].ReadPixels();Assert.True(result[(16*width+38)*4]>0);Assert.True(result[(22*width+32)*4]>0);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){float value=result[(y*width+x)*4];Assert.InRange(value,0,8);Assert.InRange(result[(y*width+64-x)*4],value-.0001f,value+.0001f);Assert.InRange(result[((32-y)*width+x)*4],value-.0001f,value+.0001f);}
    }
    /// <summary>Reports the active fragment sampler requirement and verifies the executing device supports it.</summary>
    [Fact]
    public void CompositeSamplerFootprintFitsDeviceCapacity()
    {
        EnsureShaderTestAvailable();var shader=Programs.Create<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>();
        GL.GetProgram(shader.ProgramId,GetProgramParameterName.ActiveUniforms,out int count);
        int samplers = 0;
        var units = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            string name = GL.GetActiveUniform(shader.ProgramId, index, out int size, out ActiveUniformType type);
            if (type is not (ActiveUniformType.Sampler2D or ActiveUniformType.Sampler3D or ActiveUniformType.Sampler2DArray)) continue;
            samplers += size;
            Assert.True(shader.ProgramLayout.TryGetContractSamplerUnit(name, out int unit));
            Assert.InRange(unit, 0, 12);
            Assert.True(units.Add(unit));
            int location = shader.ProgramLayout.BinaryInterface!.GetUniformLocation(name);
            GL.GetUniform(shader.ProgramId, location, out int actualUnit);
            Assert.Equal(unit, actualUnit);
        }
        int driverLimit = GpuSupport.MaxTextureImageUnits;
        output.WriteLine($"Composite active samplers={samplers}; driver fragment limit={driverLimit}; required fragment sampler capacity={samplers}.");
        output.WriteLine($"Device array layers={GpuSupport.MaxArrayTextureLayers}; color attachments={GpuSupport.MaxColorAttachments}; draw buffers={GpuSupport.MaxDrawBuffers}.");
        Assert.True(GpuSupport.MaxArrayTextureLayers >= 7);
        Assert.True(GpuSupport.MaxColorAttachments >= 8);
        Assert.Equal(13, samplers);
        Assert.InRange(samplers, 0, driverLimit);
        Assert.Equal(Enumerable.Range(0, 13), units.Order());
    }
    /// <summary>Measures a bounded four-pass shaft extraction/filter/publication workload without bloom or live-frame claims.</summary>
    [Fact]
    public void SyntheticLightShaftPassTiming()
    {
        EnsureShaderTestAvailable();var shader=Programs.Create<LightShaftShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1000f,500f,250f,1f]);
        using var mask=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,1f,0f,1f]);
        using var depth=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[1f]);
        using var a=TestFramework.CreateTestGBuffer(320,180,PixelInternalFormat.Rgba16f);
        using var b=TestFramework.CreateTestGBuffer(320,180,PixelInternalFormat.Rgba16f);
        using var published=TestFramework.CreateTestGBuffer(320,180,PixelInternalFormat.Rgba16f);
        shader.VisibilityImage=mask;shader.DepthImage=depth;shader.ExposureImage=depth;
        var sun=new Vector4(.5f,.5f,1,16f/9);var solar=new Vector4(1,1,1,128);var timings=new List<double>();
        for(int sample=-1;sample<5;sample++)
        {
            using var timer=GpuTimerQuery.Create();timer.Begin();
            shader.SourceImage=source;shader.Capture(new(0,0,0,0),new(1,2,.5f,0),sun,solar);Draw(shader,a);
            shader.SourceImage=a[0];shader.Capture(new(0,0,1,16),new(.12f,0,0,0),sun,solar);Draw(shader,b);
            shader.SourceImage=b[0];shader.Capture(new(0,0,1,16),new(.36f,0,0,0),sun,solar);Draw(shader,a);
            shader.SourceImage=a[0];shader.Capture(new(0,0,2,0),Vector4.Zero,sun,solar);Draw(shader,published);
            timer.End();double ms=timer.GetResultNanoseconds()/1e6;if(sample>=0)timings.Add(ms);
        }
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
        var pipeline=draw.Prepare(shader,target);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Postprocess",[pipeline],true,
            commands=>draw.Submit(commands,pipeline,target)));
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
}
