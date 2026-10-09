using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks owned final composition against independent radiometric and display expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FinalDisplayShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Already lit scene radiance receives additive effects, then one exposure and display transform.</summary>
    [Theory]
    [InlineData(-2f, false)]
    [InlineData(0f, false)]
    [InlineData(2f, true)]
    public void CompositionResolvesOnceAndPreservesInputs(float ev, bool automatic)
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<FinalDisplayShaderProgram>();
        using var scene = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[4f,4f,4f,.37f]);
        using var bloom = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1f,1f,1f,1f]);
        using var shafts = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[2f,2f,2f,1f]);
        using var history = TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[ev]);
        using var target = TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        shader.FrameInputs = camera;
        shader.SceneImage=scene; shader.BloomImage=bloom; shader.ShaftImage=shafts;
        shader.ExposureImage=history;
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),new(automatic?-ev:ev,automatic?1:0,0,0));
        Draw(shader,target);
        float radiance=(4f+3f)*MathF.Pow(2,ev);
        float expected=Display(radiance);
        float[] actual=target[0].ReadPixels();
        for(int i=0;i<3;i++) Assert.InRange(actual[i],expected-.00001f,expected+.00001f);
        Assert.Equal(1,actual[3]);
        Assert.Equal(new[]{4f,4f,4f,.37f},scene.ReadPixels());
    }
    /// <summary>Measures a bounded synthetic final-pass workload without claiming live frame cost or physical bandwidth.</summary>
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(2560,1440)]
    public void SyntheticFinalPassTiming(int width,int height)
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[4f,2f,1f,.5f]);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(width,height,PixelInternalFormat.Rgba16f);
        using var camera = TestFrameCamera.CreateIdentity(width, height);
        shader.FrameInputs = camera;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureImage=zero;
        shader.Capture(true,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        Draw(shader,target);
        var samples=new List<double>();
        for(int i=0;i<5;i++)
        {
            using var timer=GpuTimerQuery.Create();
            timer.Begin();Draw(shader,target);timer.End();samples.Add(timer.GetResultNanoseconds()/1e6);
        }
        output.WriteLine($"Synthetic owned final {width}x{height}, uniform 1x1 inputs, native antialias setting enabled, five warm samples: min={samples.Min():F4}ms median={samples.Order().ElementAt(2):F4}ms max={samples.Max():F4}ms; target payload={(long)width*height*8} bytes. Not live cost or physical bandwidth.");
    }
    /// <summary>Independent grading and screen treatments respond to native controls while preserving opaque output and source radiance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisplayControlsAffectOutputWithoutChangingScene(int treatment)
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.18f,.18f,.18f,.3f]);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(17,9,PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(17, 9);
        shader.FrameInputs = camera;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureImage=zero;
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);Draw(shader,target);
        float[] baseline=target[0].ReadPixels();
        var controls=treatment==0?new FinalDisplayParameters(new(2,1,1,0),Vector4.Zero,Vector4.Zero):
            treatment==1?new FinalDisplayParameters(new(1,1,1,0),Vector4.Zero,new(1,.5f,0,0)):
            new FinalDisplayParameters(new(1,1,1,0),new(0,2,.5f,0),new(0,0,1,0));
        shader.Capture(false,controls,Vector4.Zero);Draw(shader,target);
        float[] changed=target[0].ReadPixels();
        Assert.Contains(Enumerable.Range(0,changed.Length),i=>i%4!=3&&Math.Abs(changed[i]-baseline[i])>.02f);
        for(int i=0;i<changed.Length;i++)
        {
            Assert.True(float.IsFinite(changed[i]));Assert.InRange(changed[i],0,1);
            if(i%4==3)Assert.Equal(1,changed[i]);
        }
        if(treatment==0)Assert.True(changed[0]>baseline[0]);
        Assert.Equal(new[]{.18f,.18f,.18f,.3f},source.ReadPixels());
    }
    #endregion

    #region Private
    /// <summary>Independently computes the shared display shoulder, sRGB encoding and first-pixel dither quantization.</summary>
    private static float Display(float radiance)
    {
        float linear=radiance/(1+radiance);
        float encoded=linear<=.0031308f?12.92f*linear:1.055f*MathF.Pow(linear,1f/2.4f)-.055f;
        return MathF.Floor(Math.Clamp(encoded-.4921875f/255f,0,1)*255f+.5f)/255f;
    }

    /// <summary>Draws the production procedural triangle after readback with deterministic raster state.</summary>
    private static void Draw(FinalDisplayShaderProgram shader,GpuFramebuffer target)
    {
        using var draw = new PostprocessDraw();
        var pipeline = draw.Prepare(shader, target);
        Assert.True(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsCommandContext.TryRun("Tests.FinalDisplay", [pipeline], true,
            commands => draw.Submit(commands, pipeline, target)));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
