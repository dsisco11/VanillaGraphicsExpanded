using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks actual solar raster radiance feeding owned bloom without engine bloom metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereSunBloomTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Extinction, horizon coverage and opaque occlusion govern solar bloom through scene RGB alone.</summary>
    [Fact]
    public void OwnedBloomRetainsSolarRadianceAndCoverageWithoutRedGlowMarker()
    {
        EnsureShaderTestAvailable();
        // Arrange: route production solar RGB into bloom with a separately cleared glow marker.
        var sun=Programs.Create<SunProgram>();
        using var camera = TestFrameCamera.CreateSolar(64,64);
        sun.Frame = camera;
        var bloom=Programs.Create<BloomShaderProgram>();
        using var color=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 64), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 64), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth=new DepthTexture(64,64,TextureFormat.DepthComponent32f);
        using var scene=DynamicGpuFramebuffer.CreateMRT([color,glow],depth)!;
        using var extracted=TestFramework.CreateTestGBuffer(32,32,TextureFormat.Rgba32f);
        using var horizontal=TestFramework.CreateTestGBuffer(32,32,TextureFormat.Rgba32f);
        using var halo=TestFramework.CreateTestGBuffer(32,32,TextureFormat.Rgba32f);
        
        var layout=new RHIVertexLayout([]);
        using var geometry=new ArrayGraphicsGeometry(layout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        var metadata = RHICommandContext.Current.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(scene, [new(AttachmentIndex:0),new(AttachmentIndex:1)]));
        var solarBlend=new ColorBlendDesc {Enabled=true,SourceRgb=BlendFactor.One,DestinationRgb=BlendFactor.One,
            SourceAlpha=BlendFactor.Zero,DestinationAlpha=BlendFactor.One};
        using var solarPipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(sun.Shader,layout,metadata,DynamicPipelineState.Viewport,
            // This fixture's far-plane output equals clear depth one; production SunDrawPass disables depth.
            depthStencil:new(){DepthTest=true,DepthWrite=true,DepthComparison=ComparisonFunction.LessOrEqual},
            blending:[solarBlend,solarBlend])).RequireResource();
        using var draw=new PostprocessDraw();
        double baseline=0;
        // Act: vary radiance, horizon, visibility and exposure through the owned bloom stages.
        // Native sky attenuation has already been applied to disk RGB. Bloom never adds another solar source.
        foreach(var (radiance,horizon,blocked,ev,ratio) in new[]{(100f,-1f,false,0f,1f),(25f,-1f,false,0f,.25f),(100f,0f,false,0f,.5f),(.01f,-1f,false,0f,0f),(100f,-1f,true,0f,0f),(0f,-1f,false,0f,0f),(100f,-1f,false,-20f,0f)})
        {
            Vector3 diskRadiance = new(radiance, radiance * .5f, radiance * .25f);
            sun.DrawInputs.Capture(SolarRasterLighting.Create(diskRadiance, horizon),
                AtmosphereSolarDiskStorage.MaximumScale(diskRadiance, 1, 1));
            var pass=FramebufferTestSetup.DescribeAttachments(scene, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(AttachmentIndex:1, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))], new(DepthLoad:AttachmentLoad.Clear,ClearDepth:blocked ? .5f : 1f));
            {
    var commands=RHICommandContext.Current;
    using var scope9=commands.EnterBoundary(new("Tests.SolarBloomSource",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([solarPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope9.Run(()=>{
                commands.BeginRenderPass(pass);commands.SetPipeline(solarPipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
                commands.BindResources(sun.CaptureBindings()); commands.Draw(geometry.Inputs,new(0,3));commands.EndRenderPass();
            });
}
            // Assert: energy follows only the admitted scene radiance and preserves source pixels.
            using var solar = new Assertions.GpuImageSnapshot(color);
            ImageAssertions.Comparisons.CompareConstant(glow, 0, channelMask: 1).AssertEqual();
            bloom.SourceImage=color;bloom.SecondaryImage=color;bloom.CaptureExtraction(true,false,1,0,1,ev);
            SubmitBloom(draw,bloom,extracted);
            bloom.SourceImage=extracted[0];bloom.CaptureGaussian(4,false);SubmitBloom(draw,bloom,horizontal);
            bloom.SourceImage=horizontal[0];bloom.CaptureGaussian(4,true);SubmitBloom(draw,bloom,halo);
            var result = ImageAssertions.Statistics.Statistics(halo[0], channelMask: 1);
            result.AssertFiniteRange(); double energy = result.Sum(0);
            if(baseline==0) { baseline=energy;Assert.True(energy>1); }
            Assert.InRange(energy,baseline*ratio-baseline*.04,baseline*ratio+baseline*.04);
            if(ratio==0)Assert.Equal(0,energy);
            else ImageAssertions.Comparisons.CompareChannelRatios(halo[0], new(0, .5f, .25f, 0), .0001f).AssertEqual();
            ImageAssertions.Comparisons.CompareHistory(color, solar).AssertEqual();

        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Far solar geometry respects opaque depths, and the installed late-query color policy emits no scene or glow pixels.</summary>
    [Theory]
    [InlineData(.5f, false, false)]
    [InlineData(.99f, false, false)]
    [InlineData(.9999f, false, false)]
    [InlineData(.999999f, false, false)]
    [InlineData(1f, false, true)]
    [InlineData(.5f, true, false)]
    [InlineData(1f, true, false)]
    public void SolarDepthAndLateQueryMasksRejectOccludedContribution(float wallDepth, bool queryOnly, bool visible)
    {
        EnsureShaderTestAvailable();
        // Arrange: clear the actual scene depth and select the late-query color mask policy.
        var sun = Programs.Create<SunProgram>();
        using var camera = TestFrameCamera.CreateSolar(32,32);
        sun.Frame = camera;
        Vector3 diskRadiance = new(100, 50, 25);
        sun.DrawInputs.Capture(SolarRasterLighting.Create(diskRadiance, -1),
            AtmosphereSolarDiskStorage.MaximumScale(diskRadiance, 1, 1));
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 32), Height = System.Math.Max(1, 32), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 32), Height = System.Math.Max(1, 32), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = new DepthTexture(32, 32, TextureFormat.DepthComponent32f);
        using var scene = DynamicGpuFramebuffer.CreateMRT([color, glow], depth)!;
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var metadata = RHICommandContext.Current.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(scene, [new(AttachmentIndex:0), new(AttachmentIndex:1)]));
        var mask = new ColorBlendDesc { Enabled = true, SourceRgb = BlendFactor.One, DestinationRgb = BlendFactor.One,
            SourceAlpha = BlendFactor.Zero, DestinationAlpha = BlendFactor.One, WriteRed = !queryOnly,
            WriteGreen = !queryOnly, WriteBlue = !queryOnly, WriteAlpha = !queryOnly };
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(sun.Shader, layout, metadata, DynamicPipelineState.Viewport,
            // Only the test's cleared-depth witness uses this comparison; production SunDrawPass disables depth.
            depthStencil: new() { DepthTest = true, DepthWrite = !queryOnly, DepthComparison = ComparisonFunction.LessOrEqual },
            blending: [mask, mask])).RequireResource();
        // The clear stands for already-rasterized opaque geometry; the query's color masks must also hold on clear sky.
        var pass = FramebufferTestSetup.DescribeAttachments(scene, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)), new(AttachmentIndex:1, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))], new(DepthLoad: AttachmentLoad.Clear, ClearDepth: wallDepth));
        // Act: submit the production solar fragment against the selected wall depth.
        {
    var commands=RHICommandContext.Current;
    using var scope10=commands.EnterBoundary(new("Tests.SolarOcclusion",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope10.Run(()=>{
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(sun.CaptureBindings()); commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });
}
        // Assert: scene and glow support agree with the independently supplied visibility outcome.
        Assert.Equal(visible, ImageAssertions.Statistics.Inspect(color).Positive > 0);
        Assert.Equal(visible, ImageAssertions.Statistics.Inspect(glow).Positive > 0);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
    #region Private
    /// <summary>Submits each owned bloom stage through a prepared pipeline after any preceding image readback.</summary>
    private static void SubmitBloom(PostprocessDraw draw,BloomShaderProgram shader,GpuFramebuffer target)
    {
        var pipeline=draw.Prepare(shader,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope11=commands.EnterBoundary(new("Tests.SolarBloom",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope11.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), shader.CaptureBindings()));
}
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
}
