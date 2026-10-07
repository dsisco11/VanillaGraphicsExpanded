using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks liquid output equations against installed engine accumulation, revealage and glow policy.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidBlendPolicyTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The packaged red/half-alpha fixture exposes RGB and alpha differences in each liquid blend equation.</summary>
    [Theory]
    [InlineData(0, .2f, 0f, 0f, .4f)]
    [InlineData(1, .2f, 0f, 0f, .4f)]
    [InlineData(2, .6f, .2f, .3f, .65f)]
    [InlineData(3, 1.2f, .4f, .6f, 1.3f)]
    [InlineData(4, 1.2f, .4f, .6f, 1.3f)]
    [InlineData(5, 1.2f, .4f, .6f, 1.3f)]
    [InlineData(6, 1.2f, .4f, .6f, 1.3f)]
    [InlineData(7, 1.2f, .4f, .6f, 1.3f)]
    public void SurfaceOutputMatchesEngineEquation(int output, float red, float green, float blue, float alpha)
    {
        EnsureContextValid(); using var programs = new ComponentShaderPrograms(); var shader = programs.Create<BlendProgram>();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, new([]),
            new([new(PixelInternalFormat.Rgba32f), new(PixelInternalFormat.Rgba32f)]), DynamicPipelineState.Viewport,
            blending: [output < 6 ? LiquidPipelineStates.SurfaceBlending[output] : LiquidPipelineStates.VolumeBlending[output - 6], new()]), shader);
        using var geometry = new ArrayGraphicsGeometry(new([]), PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), 3);
        Assert.True(GraphicsCommandContext.TryRun("LiquidBlendPolicy", [pipeline], true, commands =>
        {
            var clear = ColorClearValue.Float(.2f, .4f, .6f, .8f);
            commands.BeginPass(new(target, [new(0, AttachmentLoad.Clear, Clear: clear), new(1)]));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
        }));
        var actual = target[0].ReadPixels(); var expected = new[] { red, green, blue, alpha };
        for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .0001f, expected[channel] + .0001f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Uses the shared build's existing MRT fixture with no alternate shader compiler.</summary>
    private sealed class BlendProgram : GpuProgram
    {
        /// <summary>Creates the component shader owner.</summary>
        public BlendProgram() { }
        /// <summary>The fixture has no resource or parameter inputs to publish.</summary>
        protected override void Submit() { }
        /// <summary>Declares the already packaged red/half-alpha outputs.</summary>
        internal override GpuShaderContract ProgramContract => FramebufferBlendShaderProgram.Contract;
    }
    #endregion
}
