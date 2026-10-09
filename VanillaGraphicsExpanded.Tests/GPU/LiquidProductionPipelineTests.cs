using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Prepares the production liquid executables against their actual native image formats and pooled vertex layout.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidProductionPipelineTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Includes narrow native revealage images, depth-only dummy-output discard and both volume accumulators.</summary>
    [Theory]
    [InlineData("surface")]
    [InlineData("depth")]
    [InlineData("volume")]
    public void ProductionInterfaceMatchesPublishedTargets(string route)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        try
        {
            GpuProgram shader = route == "depth"
                ? GpuShaderPrograms.Get<LiquidDepthShaderProgram>(assets.Api, "pbr_liquid_depth")!
                : GpuShaderPrograms.Get<LiquidShaderProgram>(assets.Api,
                    route == "surface" ? "pbr_liquid" : LiquidShaderProgram.VolumePassName)!;
            if (shader is LiquidDepthShaderProgram depthShader) depthShader.FrameInputs = frameCamera;
            else ((LiquidShaderProgram)shader).FrameInputs = frameCamera;
            Assert.Equal(12, GpuShaderContracts.Create(route == "depth" ? "pbr_liquid_depth"
                : "pbr_liquid")
                .UniformBlocks["VgeFrameUBO"].Slot);
            Assert.True(shader.EnsureReady(), string.Join("\n", assets.Logs));
            using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent24);
            // These are the final installed engine OIT formats after its BeforeOIT attachment replacement.
            using var target = route == "depth" ? GpuFramebuffer.CreateDepthOnly(depth)!
                : route == "surface" ? CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgb8,
                    PixelInternalFormat.R16f, PixelInternalFormat.Rgba8, PixelInternalFormat.Rgba16f,
                    PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f)
                : CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
            var outputs = route == "depth" ? LiquidPipelineStates.DepthOutputs
                : route == "surface" ? LiquidPipelineStates.SurfaceOutputs : LiquidPipelineStates.VolumeOutputs;
            using var targets = new RenderPassTargets(new(target, outputs));
            using var lifetime = new GraphicsPipelineLifetime();
            using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, EngineLiquidPoolGeometry.Layout,
                targets.Signature, DynamicPipelineState.Viewport,
                depthStencil: route == "depth" ? LiquidPipelineStates.Depth : new(),
                blending: route == "depth" ? LiquidPipelineStates.DepthBlending
                    : route == "surface" ? LiquidPipelineStates.SurfaceBlending : LiquidPipelineStates.VolumeBlending), shader);
            pipeline.ValidateTargets(targets.Signature);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuShaderPrograms.Dispose(assets.Api); }
    }
    #endregion
}
