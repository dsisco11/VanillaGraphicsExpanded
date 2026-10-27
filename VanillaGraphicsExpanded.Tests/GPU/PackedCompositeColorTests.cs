using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
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
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var lighting = LayeredTestTexture.Create(direct,specular,emission);
        using var surface = LayeredTestTexture.Create(normal,material,indirect);
        using var target = TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.R11fG11fB10f,PixelInternalFormat.Rgba16f,PixelInternalFormat.R32f);
        using var positions = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); positions.UploadData(new float[] {-1,-1,0, 3,-1,0, -1,3,0});
        using var uv = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); uv.UploadData(new float[] {0,0, 2,0, 0,2});
        using var geometry = new ArrayGraphicsGeometry(EngineFullscreenGeometry.Layout, PrimitiveTopology.Triangles,
            new Dictionary<int,GpuVbo> { [0]=positions, [1]=uv });
        var passDescription = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0),new(AttachmentIndex:1),new(AttachmentIndex:2)]);
        var pass = RHICommandContext.Current.DescribeRenderTargets(passDescription);
        shader.DirectLighting=lighting;shader.IndirectDiffuse=indirect;shader.GBufferAlbedo=albedo;
        shader.GBufferSurface=surface;shader.GBufferPosition=position;shader.PrimaryDepth=depth;
        using var frameCamera = TestFrameCamera.Create([1,0,0,0,0,1,0,0,0,0,-4,0,receiverX,0,0,1],
            [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
        shader.FrameInputs = frameCamera;

        shader.IndirectTint=new(1,1,1);shader.IndirectIntensity=0;shader.DiffuseAOStrength=1;shader.SpecularAOStrength=1;
        shader.SetAtmosphere(null);shader.SetWaterVolume(null);shader.SetUnderwater(false);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,EngineFullscreenGeometry.Layout,pass,DynamicPipelineState.Viewport)).RequireResource();

        shader.RefractionSourceEnabled=true;
        shader.SetAmbientOcclusion(null);
        {
    var commands=RHICommandContext.Current;
    using var scope69=commands.EnterBoundary(new("Tests.PackedComposite",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope69.Run(()=>{
            commands.BeginRenderPass(passDescription);commands.SetPipeline(pipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry.Inputs,new(0,3));commands.EndRenderPass();
        });
}
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
        resolve.PrimaryScene=target[0];resolve.PrimaryDepth=depth;
        resolve.SceneLinear=1;resolve.ParticleLayerEnabled=0;
        var handoffDescription=FramebufferTestSetup.DescribeAttachments(handoff, [new(AttachmentIndex:0)]);
        var handoffPass = RHICommandContext.Current.DescribeRenderTargets(handoffDescription);
        using var handoffPipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(resolve.Shader,EngineFullscreenGeometry.Layout,handoffPass,DynamicPipelineState.Viewport)).RequireResource();
        {
    var commands=RHICommandContext.Current;
    using var scope70=commands.EnterBoundary(new("Tests.PackedCompositeHandoff",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([handoffPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope70.Run(()=>{
            commands.BeginRenderPass(handoffDescription);commands.SetPipeline(handoffPipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry.Inputs,new(0,3));commands.EndRenderPass();
        });
}
        Assert.Equal(new[]{packed[0],packed[1],packed[2],1f},handoff[0].ReadPixels());

        // The production display shader must accept RGB scene storage and still publish opaque RGBA.
        var display=Programs.Create<FinalDisplayShaderProgram>();
        using var grading=new ColorGradingLut(); display.GradingLookup=grading.Prepare(Programs.Api,new(1,1,1,0));
        using var output=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        using var zero=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,0f,0f,0f]);
        display.FrameInputs=frameCamera;display.SceneImage=target[0];display.BloomImage=zero;display.ShaftImage=zero;display.ExposureBuffer=neutralExposure;
        display.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        using var draw=new PostprocessDraw();
        var displayPipeline=draw.Prepare(display,output.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope71=commands.EnterBoundary(new("Tests.PackedCompositeDisplay",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([displayPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope71.Run(()=>draw.Submit(commands,displayPipeline,output.PostprocessPass(), display.CaptureBindings()));
}
        float[] resolved=output[0].ReadPixels();
        Vector3 encoded=FilmicGradingReference.Lookup(new(packed[0],packed[1],packed[2]),new(1,1,1,0));
        for(int channel=0;channel<3;channel++)
        {
            float expectedDisplay=FilmicGradingReference.Quantize(encoded[channel]);
            Assert.InRange(resolved[channel],expectedDisplay-1.001f/255,expectedDisplay+1.001f/255);
        }
        Assert.Equal(1f,resolved[3]);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

}
