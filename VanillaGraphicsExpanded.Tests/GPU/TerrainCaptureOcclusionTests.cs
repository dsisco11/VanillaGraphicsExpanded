using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks terrain material capture against the HDR solar background that exposed fractional-alpha leakage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainCaptureOcclusionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Surviving cutouts replace the background completely; rejected fragments retain it unchanged.</summary>
    [Theory]
    [InlineData(false, .99999994f, .5f)]
    [InlineData(true, .99999994f, .5f)]
    [InlineData(false, .9995f, .5f)]
    [InlineData(true, .9995f, .5f)]
    [InlineData(false, .25f, .1f)]
    [InlineData(true, .25f, .1f)]
    [InlineData(false, .25f, .5f)]
    [InlineData(true, .25f, .5f)]
    public void PatchedTerrainResolvesCoverageBeforeAlphaBlending(bool topsoil, float alpha, float cutoff)
    {
        EnsureShaderTestAvailable();
        TerrainCaptureProgram shader = topsoil ? Programs.Create<TopsoilTerrainCaptureProgram>() : Programs.Create<OpaqueTerrainCaptureProgram>();
        shader.Capture(new(.45f, .5f, .537f, alpha), cutoff);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var lifetime = new GraphicsPipelineLifetime();
        var layout = new VertexLayoutDesc([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var metadata = new RenderPassTargets(new RenderPassDesc(target, [new(0)]));
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendingFactorSrc.SrcAlpha, DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha };
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, layout, metadata.Signature, DynamicPipelineState.Viewport, blending: [blend]), shader);
        // These are the captured HDR sun channels before opaque terrain. Alpha blending must not retain any fraction.
        float[] background = [804, 732, 449.25f, 1];
        var pass = new RenderPassDesc(target, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(background[0], background[1], background[2], 1))]);
        Assert.True(GraphicsCommandContext.TryRun("Tests.TerrainCaptureOcclusion", [pipeline], true, commands =>
        {
            commands.BeginPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            commands.EndPass();
        }));
        float[] actual = target[0].ReadPixels();
        float[] material = [Linear(.45f), Linear(.5f), Linear(.537f), 1];
        float[] expected = alpha < cutoff ? background : material;
        for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .0003f, expected[channel] + .0003f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Computes the independent material decode without exposure or display conversion.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
}
