using System.Numerics;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies native grading and configured camera exposure through complete owned GPU submissions.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CameraExposureDisplayTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Native default grading must retain the already encoded single display transfer.</summary>
    [Fact]
    public void NativeDefaultGradingPreservesSingleDisplayTransfer()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms {DropShadowIntensity=0,ExtraContrastLevel=0,SepiaLevel=0,ExtraSepia=0,GlitchStrength=0,DamageVignetting=0,FrostVignetting=0});
        var native=FinalDisplayParameters.Capture(api.Object);
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.18f,.018f,.0018f,1f]);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var draw=new PostprocessDraw();
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        shader.FrameInputs = camera;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureImage=zero;
        var pipeline=draw.Prepare(shader,target);
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        Assert.True(GraphicsCommandContext.TryRun("Tests.ExposureNeutral",[pipeline],true,commands=>draw.Submit(commands,pipeline,target)));
        float[] neutral=target[0].ReadPixels();
        shader.Capture(false,native,Vector4.Zero);
        Assert.True(GraphicsCommandContext.TryRun("Tests.ExposureNative",[pipeline],true,commands=>draw.Submit(commands,pipeline,target)));
        float[] actual=target[0].ReadPixels();
        for(int i=0;i<3;i++)Assert.InRange(actual[i],neutral[i]-.00001f,neutral[i]+.00001f);
        float originalGamma=ClientSettings.GammaLevel;
        try
        {
            foreach(float gamma in new[]{1.5f,6f})
            {
                ClientSettings.GammaLevel=gamma;
                var adjusted=FinalDisplayParameters.Capture(api.Object);
                Assert.Equal(gamma/3,adjusted.Grading.X);
                shader.Capture(false,adjusted,Vector4.Zero);
                Assert.True(GraphicsCommandContext.TryRun("Tests.NativeGammaSlider",[pipeline],true,commands=>draw.Submit(commands,pipeline,target)));
                float[] graded=target[0].ReadPixels();
                for(int i=0;i<3;i++)Assert.True(gamma<3?graded[i]<neutral[i]:graded[i]>neutral[i]);
                Assert.Equal(1,graded[3]);
            }
        }
        finally {ClientSettings.GammaLevel=originalGamma;}
        Assert.Equal(new[]{.18f,.018f,.0018f,1f},source.ReadPixels());
    }

    /// <summary>Meters finite HDR input, respects configured exposure limits and performs the final display transform once.</summary>
    [Theory]
    [InlineData(.18f)]
    [InlineData(1f)]
    [InlineData(16f)]
    public void DefaultMeteringAndNativeDisplayRespectConfiguredBounds(float luminance)
    {
        EnsureShaderTestAvailable();
        var meter=Programs.Create<CameraHistogramShaderProgram>();var adapt=Programs.Create<CameraAdaptShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[luminance,luminance,luminance,1f]);
        using var previous=TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[0f]);
        using var histogram=TestFramework.CreateTestGBuffer(64,1,PixelInternalFormat.Rg32f);
        using var result=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.R32f);
        using var draw=new PostprocessDraw();
        var settings=new CameraExposureSettings().Snapshot();
        meter.SceneRadiance=source;meter.Capture(settings,true);
        var meterPipeline=draw.Prepare(meter,histogram);
        Assert.True(GraphicsCommandContext.TryRun("Tests.ExposureMeter",[meterPipeline],true,commands=>draw.Submit(commands,meterPipeline,histogram)));
        float count=histogram[0].ReadPixels().Where((_,i)=>i%2==0).Sum();
        using var adaptationCamera = TestFrameCamera.CreateIdentity(1, 1);
        adapt.FrameInputs = adaptationCamera;
        adapt.Histogram=histogram[0];adapt.PreviousExposure=previous;adapt.Capture(settings,true);
        var adaptPipeline=draw.Prepare(adapt,result);
        Assert.True(GraphicsCommandContext.TryRun("Tests.ExposureAdapt",[adaptPipeline],true,commands=>draw.Submit(commands,adaptPipeline,result)));
        float ev=result[0].ReadPixels()[0];
        var final=Programs.Create<FinalDisplayShaderProgram>();
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var display=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        final.FrameInputs = camera;
        final.SceneImage=source;final.BloomImage=zero;final.ShaftImage=zero;final.ExposureImage=result[0];
        var finalPipeline=draw.Prepare(final,display);
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms {DropShadowIntensity=0});
        final.Capture(false,FinalDisplayParameters.Capture(api.Object),new(0,1,0,0));
        Assert.True(GraphicsCommandContext.TryRun("Tests.ExposureFinal",[finalPipeline],true,commands=>draw.Submit(commands,finalPipeline,display)));
        float expectedEv=Math.Clamp(MathF.Log2(settings.MiddleGray/luminance),settings.MinEV,settings.MaxEV);
        Assert.InRange(ev,expectedEv-.00001f,expectedEv+.00001f);
        Assert.True(count>0);
        float exposed=luminance*MathF.Pow(2,expectedEv);
        float linear=exposed/(1+exposed);
        float srgb=linear<=.0031308f?linear*12.92f:1.055f*MathF.Pow(linear,1f/2.4f)-.055f;
        float expected=MathF.Floor(Math.Clamp(srgb-.4921875f/255f,0,1)*255+.5f)/255;
        float[] actual=display[0].ReadPixels();
        for(int i=0;i<3;i++)Assert.InRange(actual[i],expected-.00001f,expected+.00001f);
        Assert.Equal(1,actual[3]);
        Assert.Equal(new[]{luminance,luminance,luminance,1f},source.ReadPixels());
    }
    #endregion
}
