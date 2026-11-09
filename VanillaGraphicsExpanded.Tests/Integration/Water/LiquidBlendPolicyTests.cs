using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Checks liquid output equations against installed engine accumulation, revealage and glow policy.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
        // Arrange production blending equations and the packaged test shader against a two-target native framebuffer.
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<BlendProgram>();
        using var target = CreateMRTRenderTarget(1, 1, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, new([]),
            new([new(TextureFormat.Rgba32f), new(TextureFormat.Rgba32f)]), DynamicPipelineState.Viewport,
            blending: [output < 6 ? LiquidPipelineStates.SurfaceBlending[output] : LiquidPipelineStates.VolumeBlending[output - 6], new()])).RequireResource();
        using var geometry = new ArrayGraphicsGeometry(new([]), PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), 3);
        {
            var commands = RHICommandContext.Current;
            using var scope = commands.EnterBoundary(new("LiquidBlendPolicy", true,
                graphics: System.Linq.Enumerable.Select<RHIGraphicsPipeline, RHIGraphicsPipelineRef>([pipeline], value => value),
                operations: BoundaryOperations.RenderPasses));
            // Act by rendering one fixture fragment through the selected liquid blend equation.
            scope.Run(() =>
            {
            var clear = ColorClearValue.Float(.2f, .4f, .6f, .8f);
                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target,
                    [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: clear), new(AttachmentIndex: 1)]));
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(shader.CaptureBindings());
                commands.Draw(geometry.Inputs, new(0, 3));
            });
        }
        // Assert the observed channels match the engine accumulation/revealage contract and leave GL error-free.
        var actual = target[0].ReadPixelsRegion(0, 0, 1, 1);
        var expected = new[] { red, green, blue, alpha };
        for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .0001f, expected[channel] + .0001f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Uses the shared build's existing MRT fixture with no alternate shader compiler.</summary>
    private sealed class BlendProgram : GpuProgram
    {
        #region Public API
        /// <summary>Creates the component shader owner.</summary>
        public BlendProgram() { }
        #endregion

        #region Protected
        /// <summary>The fixture has no resource or parameter inputs to publish.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        #endregion

        #region Internal
        /// <summary>Declares the already packaged red/half-alpha outputs.</summary>
        internal override GpuShaderContract ProgramContract => FramebufferBlendShaderProgram.Contract;
        #endregion
    }
    #endregion
}
