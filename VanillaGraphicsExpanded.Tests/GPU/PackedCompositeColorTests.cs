using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises packed HDR composition alongside full-precision receiver metadata and final display sampling.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PackedCompositeColorTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Preserves HDR RGB, water eligibility and depth while the display pass samples packed scene color.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackedCompositePreservesReceiverAndDisplayContracts(bool lumon)
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<PBRCompositeShaderProgram>(p => { p.LumOnEnabled = lumon; p.EnablePbrComposite = lumon; p.EnableShortRangeAo = false; });
        using var direct = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1.13f,2.27f,4.39f,1f]);
        using var specular = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,1f]);
        using var emission = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,1f]);
        using var indirect = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,1f]);
        using var albedo = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,.5f,.5f,1f]);
        float encodedX=.5f, encodedZ=1f, receiverX=0f;
        using var normal = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[encodedX,.5f,encodedZ,1f]);
        using var material = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[.5f,0f,0f,0f]);
        using var position = TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[receiverX,0f,-2f,1f]);
        using var depth = TestFramework.CreateTexture(1,1,PixelInternalFormat.R32f,[.75f]);
        using var lighting = LayeredTestTexture.Create(direct,specular,emission);
        using var surface = LayeredTestTexture.Create(normal,material,indirect);
        using var target = TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.R11fG11fB10f,PixelInternalFormat.Rgba16f,PixelInternalFormat.R32f);
        using var positions = GpuVbo.Create(); positions.UploadData(new float[] {-1,-1,0, 3,-1,0, -1,3,0});
        using var uv = GpuVbo.Create(); uv.UploadData(new float[] {0,0, 2,0, 0,2});
        using var geometry = new ArrayGraphicsGeometry(EngineFullscreenGeometry.Layout, PrimitiveType.Triangles,
            new Dictionary<int,GpuVbo> { [0]=positions, [1]=uv });
        using var lifetime = new GraphicsPipelineLifetime();
        var passDescription = new RenderPassDesc(target,[new(0),new(1),new(2)]);
        using var pass = new RenderPassTargets(passDescription);
        shader.DirectLighting=lighting;shader.IndirectDiffuse=indirect;shader.GBufferAlbedo=albedo.TextureId;
        shader.GBufferSurface=surface;shader.GBufferPosition=position.TextureId;shader.PrimaryDepth=depth.TextureId;
        using var frameCamera = TestFrameCamera.Create([1,0,0,0,0,1,0,0,0,0,-4,0,receiverX,0,0,1],
            [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
        shader.FrameInputs = frameCamera;

        shader.IndirectTint=new(1,1,1);shader.IndirectIntensity=0;shader.DiffuseAOStrength=1;shader.SpecularAOStrength=1;
        shader.SetAtmosphere(null);shader.SetWaterVolume(null);shader.SetUnderwater(false);
        using var pipeline = new GraphicsPipeline(lifetime,new(shader.GraphicsIdentity!,EngineFullscreenGeometry.Layout,pass.Signature,DynamicPipelineState.Viewport),shader);

        shader.RefractionSourceEnabled=true;
        shader.SetAmbientOcclusion(null);
        Assert.True(GraphicsCommandContext.TryRun("Tests.PackedComposite",[pipeline],true,commands=> {
            commands.BeginPass(passDescription);commands.SetPipeline(pipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
        }));
        // Check each channel against its own mantissa precision; blue has one fewer bit.
        float[] expected=[1.13f,2.27f,4.39f];
        float[] packed=target[0].ReadPixels();
        Assert.Equal(3,packed.Length);
        for(int channel=0;channel<3;channel++)
            Assert.InRange(packed[channel],expected[channel]*(channel==2?.97f:.985f),expected[channel]*(channel==2?1.03f:1.015f));
        float[] receiver=target[1].ReadPixels();
        for(int channel=0;channel<3;channel++) Assert.InRange(receiver[channel],expected[channel]-.005f,expected[channel]+.005f);
        Assert.Equal(1f,receiver[3]);
        Assert.Equal(.75f,target[2].ReadPixels()[0]);

        // The immediate scene-linear handoff samples missing RGB alpha as one.
        var resolve=Programs.Create<PBRDisplayResolveShaderProgram>();
        using var handoff=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        resolve.PrimaryScene=target[0].TextureId;resolve.PrimaryDepth=depth.TextureId;
        resolve.SceneLinear=1;resolve.ParticleLayerEnabled=0;
        var handoffDescription=new RenderPassDesc(handoff,[new(0)]);
        using var handoffPass=new RenderPassTargets(handoffDescription);
        using var handoffPipeline=new GraphicsPipeline(lifetime,new(resolve.GraphicsIdentity!,EngineFullscreenGeometry.Layout,handoffPass.Signature,DynamicPipelineState.Viewport),resolve);
        Assert.True(GraphicsCommandContext.TryRun("Tests.PackedCompositeHandoff",[handoffPipeline],true,commands=> {
            commands.BeginPass(handoffDescription);commands.SetPipeline(handoffPipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
        }));
        Assert.Equal(new[]{packed[0],packed[1],packed[2],1f},handoff[0].ReadPixels());

        // The production display shader must accept RGB scene storage and still publish opaque RGBA.
        var display=Programs.Create<FinalDisplayShaderProgram>();
        using var output=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        display.FrameInputs=frameCamera;display.SceneImage=target[0];display.BloomImage=zero;display.ShaftImage=zero;display.ExposureImage=zero;
        display.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        using var draw=new PostprocessDraw();
        var displayPipeline=draw.Prepare(display,output);
        Assert.True(GraphicsCommandContext.TryRun("Tests.PackedCompositeDisplay",[displayPipeline],true,
            commands=>draw.Submit(commands,displayPipeline,output)));
        float[] resolved=output[0].ReadPixels();
        for(int channel=0;channel<3;channel++) Assert.InRange(resolved[channel],Display(packed[channel],packed.Max())-.00001f,Display(packed[channel],packed.Max())+.00001f);
        Assert.Equal(1f,resolved[3]);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Computes the display shoulder and sRGB response including the deterministic first-pixel dither.</summary>
    private static float Display(float radiance,float peak)
    {
        float linear=radiance/(1+peak);
        float encoded=linear<=.0031308f?12.92f*linear:1.055f*MathF.Pow(linear,1f/2.4f)-.055f;
        return MathF.Floor(Math.Clamp(encoded-.4921875f/255f,0,1)*255f+.5f)/255f;
    }
    #endregion
}
