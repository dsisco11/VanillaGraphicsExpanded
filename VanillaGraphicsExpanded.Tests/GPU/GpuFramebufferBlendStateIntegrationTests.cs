using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies that complete pipelines own indexed output blending and channel writes independently of framebuffer storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferBlendStateIntegrationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Opposite incoming global blend states cannot override the pipeline's distinct MRT policies.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PipelineIndexedBlendOverridesIncomingGlobalState(bool incomingBlend)
    {
        DrawAndAssert(incomingBlend, maskOnly: false);
    }

    /// <summary>A pipeline's indexed write mask preserves disabled destination channels during a real draw.</summary>
    [Fact]
    public void IndexedWriteMaskPreservesRenderedChannels()
    {
        DrawAndAssert(incomingBlend: false, maskOnly: true);
    }
    #endregion

    #region Private
    /// <summary>Draws the packaged MRT shader through the authoritative submission owner and checks both independent outputs.</summary>
    private void DrawAndAssert(bool incomingBlend, bool maskOnly)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<BlendProgram>();
        using var t0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var t1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var framebuffer = DynamicGpuFramebuffer.CreateMRT([t0, t1])!;
        var first = maskOnly ? new ColorBlendDesc { WriteGreen = false, WriteBlue = false, WriteAlpha = false }
            : new ColorBlendDesc
            {
                Enabled = true,
                SourceRgb = BlendFactor.SourceAlpha,
                DestinationRgb = BlendFactor.OneMinusSourceAlpha,
                SourceAlpha = BlendFactor.SourceAlpha,
                DestinationAlpha = BlendFactor.OneMinusSourceAlpha
            };
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, new([]),
            new([new(TextureFormat.Rgba8), new(TextureFormat.Rgba8)]), DynamicPipelineState.Viewport,
            blending: [first, new()])).RequireResource();
        using var geometry = new ArrayGraphicsGeometry(new([]), PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), 3);
        var cache = OpenGlStateCache.Current;
        cache.SetBlendEnabled(incomingBlend);
        try
        {
            {
    var commands=RHICommandContext.Current;
    using var scope46=commands.EnterBoundary(new("IndexedBlend",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope46.Run(()=>{
                var clear = ColorClearValue.Float(0, 0, 1, 1);
                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(framebuffer, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: clear), new(AttachmentIndex:1, Load:AttachmentLoad.Clear, Clear: clear)]));
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(geometry.Inputs, new(0, 3));
                commands.EndRenderPass();
            });
}
            Assert.Equal(incomingBlend, GL.IsEnabled(IndexedEnableCap.Blend, 0));
            Assert.Equal(incomingBlend, GL.IsEnabled(IndexedEnableCap.Blend, 1));
            using var bindings = cache.BindFramebufferScope();
            float[] firstPixel = t0.ReadPixels(), secondPixel = t1.ReadPixels();
            Assert.InRange(firstPixel[0], maskOnly ? .99f : .48f, maskOnly ? 1f : .52f);
            Assert.InRange(firstPixel[2], maskOnly ? .99f : .48f, maskOnly ? 1f : .52f);
            if (maskOnly) Assert.Equal(1f, firstPixel[3]);
            Assert.InRange(secondPixel[0], .99f, 1f);
            Assert.Equal(0f, secondPixel[2]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.SetBlendEnabled(false); }
    }

    /// <summary>Uses the existing shared MRT fixture contract without a second shader compilation path.</summary>
    private sealed class BlendProgram : GpuProgram
    {
        /// <summary>Creates the test program for the standard component shader owner.</summary>
        public BlendProgram() { }
        /// <summary>The constant-color fixture has no resource or uniform inputs.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        /// <summary>Publishes the packaged fixture's exact executable declaration.</summary>
        internal override GpuShaderContract ProgramContract => FramebufferBlendShaderProgram.Contract;
    }
    #endregion
}
