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
        using var t0 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        using var t1 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        using var framebuffer = GpuFramebuffer.CreateMRT([t0, t1])!;
        using var lifetime = new GraphicsPipelineLifetime();
        var first = maskOnly ? new ColorBlendDesc { WriteGreen = false, WriteBlue = false, WriteAlpha = false }
            : new ColorBlendDesc
            {
                Enabled = true,
                SourceRgb = BlendingFactorSrc.SrcAlpha,
                DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha,
                SourceAlpha = BlendingFactorSrc.SrcAlpha,
                DestinationAlpha = BlendingFactorDest.OneMinusSrcAlpha
            };
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, new([]),
            new([new(PixelInternalFormat.Rgba8), new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport,
            blending: [first, new()]), shader);
        using var geometry = new ArrayGraphicsGeometry(new([]), PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), 3);
        var cache = StateCache.Current;
        cache.SetBlendEnabled(incomingBlend);
        try
        {
            Assert.True(GraphicsCommandContext.TryRun("IndexedBlend", [pipeline], true, commands =>
            {
                var clear = ColorClearValue.Float(0, 0, 1, 1);
                commands.BeginPass(new(framebuffer, [new(0, AttachmentLoad.Clear, Clear: clear), new(1, AttachmentLoad.Clear, Clear: clear)]));
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(geometry, new(0, 3));
                commands.EndPass();
            }));
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
        protected override void Submit() { }
        /// <summary>Publishes the packaged fixture's exact executable declaration.</summary>
        internal override GpuShaderContract ProgramContract => FramebufferBlendShaderProgram.Contract;
    }
    #endregion
}
