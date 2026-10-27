using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
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
    /// <summary>Luma preparation reads buffer EV only for automatic mode and preserves scene RGB.</summary>
    [Theory]
    [InlineData(-2f,false)]
    [InlineData(2f,true)]
    public void LumaPreparationPreservesRadianceAndUsesCameraBuffer(float ev,bool automatic)
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<PostLumaShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.18f,.18f,.18f,.3f]);
        using var exposure=ExposureTestStorage.Create(ev);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var draw=new PostprocessDraw();
        shader.SourceImage=source;shader.ExposureBuffer=exposure;
        shader.Capture(new(0,0,1,automatic?1:0),new(0,0,0,automatic?-8:ev));
        var pipeline=draw.Prepare(shader,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope16=commands.EnterBoundary(new("Tests.ExposureLuma",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope16.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
        float[] actual=target[0].ReadPixels();
        Assert.Equal(new[]{.18f,.18f,.18f},actual[..3]);
        float radiance=.18f*MathF.Pow(2,ev),linear=radiance/(1+radiance);
        float srgb=linear<=.0031308f?12.92f*linear:1.055f*MathF.Pow(linear,1f/2.4f)-.055f;
        Assert.InRange(actual[3],srgb-.00001f,srgb+.00001f);
        shader.Capture(Vector4.Zero,Vector4.Zero);
        {
    var commands=RHICommandContext.Current;
    using var scope17=commands.EnterBoundary(new("Tests.ExposureLumaBypass",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope17.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
        Assert.Equal(new[]{.18f,.18f,.18f,.3f},target[0].ReadPixels());
    }
    /// <summary>Native defaults select the neutral scene-linear grade and one cached filmic display transfer.</summary>
    [Fact]
    public void NativeDefaultGradingPreservesSingleDisplayTransfer()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms {DropShadowIntensity=0,ExtraContrastLevel=0,SepiaLevel=0,ExtraSepia=0,GlitchStrength=0,DamageVignetting=0,FrostVignetting=0});
        var native=FinalDisplayParameters.Capture(api.Object);
        using var grading=new ColorGradingLut();
        shader.GradingLookup=grading.Prepare(Programs.Api,new(1,1,1,0));
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.18f,.018f,.0018f,1f]);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var draw=new PostprocessDraw();
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var neutralExposure = ExposureTestStorage.Create(0);
        shader.FrameInputs = camera;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureBuffer=neutralExposure;
        var pipeline=draw.Prepare(shader,target.PostprocessPass());
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        {
    var commands=RHICommandContext.Current;
    using var scope18=commands.EnterBoundary(new("Tests.ExposureNeutral",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope18.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
        float[] neutral=target[0].ReadPixels();
        shader.GradingLookup=grading.Prepare(Programs.Api,native.Grading);
        shader.Capture(false,native,Vector4.Zero);
        {
    var commands=RHICommandContext.Current;
    using var scope19=commands.EnterBoundary(new("Tests.ExposureNative",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope19.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
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
                shader.GradingLookup=grading.Prepare(Programs.Api,adjusted.Grading);
                shader.Capture(false,adjusted,Vector4.Zero);
                {
    var commands=RHICommandContext.Current;
    using var scope20=commands.EnterBoundary(new("Tests.NativeGammaSlider",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope20.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
                float[] graded=target[0].ReadPixels();
                Assert.True(gamma<3?graded.Take(3).Sum()<neutral.Take(3).Sum():graded.Take(3).Sum()>neutral.Take(3).Sum());
                Vector3 reference=FilmicGradingReference.Lookup(new(.18f,.018f,.0018f),adjusted.Grading);
                for(int i=0;i<3;i++)Assert.InRange(Math.Abs(graded[i]-FilmicGradingReference.Quantize(reference[i])),0,1.001f/255);
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
        var meter=Programs.CreateCompute(CameraHistogramShaderProgram.Create);var adapt=Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[luminance,luminance,luminance,1f]);
        using var targets = new CameraExposureTargets();
        using var draw=new PostprocessDraw();
        var settings=new CameraExposureSettings().Snapshot();
        meter.SceneRadiance=source;meter.Capture(settings,true);
        meter.Histogram=targets.Histogram; meter.Dispatch(36);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        uint count=ExposureTestStorage.Read<uint>(targets.Histogram).Where((_,i)=>i%2==0).Aggregate(0u,(a,v)=>a+v);
        using var adaptationCamera = TestFrameCamera.CreateIdentity(1, 1);
        adapt.FrameInputs = adaptationCamera;
        adapt.Histogram=targets.Histogram;adapt.PreviousExposure=targets.Exposure;adapt.OutputExposure=targets.WriteTarget;adapt.Capture(settings,true);
        adapt.Dispatch(1); GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit); targets.Publish();
        float ev=ExposureTestStorage.Read<float>(targets.Exposure)[0];
        var final=Programs.Create<FinalDisplayShaderProgram>();
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        using var display=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        final.FrameInputs = camera;
        final.SceneImage=source;final.BloomImage=zero;final.ShaftImage=zero;final.ExposureBuffer=targets.Exposure;
        var finalPipeline=draw.Prepare(final,display.PostprocessPass());
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms {DropShadowIntensity=0});
        var native=FinalDisplayParameters.Capture(api.Object);
        using var grading=new ColorGradingLut(); final.GradingLookup=grading.Prepare(Programs.Api,native.Grading);
        final.Capture(false,native,new(0,1,0,0));
        {
    var commands=RHICommandContext.Current;
    using var scope21=commands.EnterBoundary(new("Tests.ExposureFinal",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([finalPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope21.Run(()=>draw.Submit(commands,finalPipeline,display.PostprocessPass(), final.CaptureBindings()));
}
        float expectedEv=Math.Clamp(MathF.Log2(settings.MiddleGray/luminance),settings.MinLogGain,settings.MaxLogGain);
        Assert.InRange(ev,expectedEv-.0003f,expectedEv+.0003f);
        Assert.True(count>0);
        float exposed=luminance*MathF.Pow(2,expectedEv);
        float expected=FilmicGradingReference.Quantize(FilmicGradingReference.Lookup(new(exposed),native.Grading).X);
        float[] actual=display[0].ReadPixels();
        for(int i=0;i<3;i++)Assert.InRange(actual[i],expected-1.001f/255,expected+1.001f/255);
        Assert.Equal(1,actual[3]);
        Assert.Equal(new[]{luminance,luminance,luminance,1f},source.ReadPixels());
    }
    #endregion
}
