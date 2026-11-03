using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production displacement functions with controlled atlas samples and projection inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainDisplacementNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainDisplacementNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    /// <summary>The production angular metric is symmetric, bounded near the eye and fades with camera distance.</summary>
    [Theory]
    [InlineData(.01f)]
    [InlineData(8f)]
    [InlineData(16f)]
    [InlineData(24f)]
    public void ProductionAngularMetricMatchesCameraDistance(float distance)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<TerrainDisplacementMetricProgram>();
        using var camera = TestFrameCamera.CreateIdentity(128, 128);
        program.FrameInputs = camera;
        program.CaptureMetric(distance);
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var geometry = CreateProceduralTriangle();
        Submit(program, target, geometry);

        float[] result = target[0].ReadPixels();
        float radial = MathF.Sqrt(distance * distance + .25f), t = Math.Clamp((radial - 8f) / 16f, 0f, 1f);
        float expected = 1f + (Math.Clamp(128f / Math.Max(.05f, radial), 1f, 7f) - 1f) * (1f - t * t * (3f - 2f * t));
        Assert.InRange(result[0], expected - .00001f, expected + .00001f);
        Assert.Equal(result[0], result[1]);
        Assert.Equal(1f, result[2]);
        Assert.InRange(result[0], 1f, 7f);
    }

    /// <summary>Shadow subdivision stays bounded without a world camera when its frozen angular metric is unavailable.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void ShadowWithoutAngularMetricUsesConservativeCap(float focalPixels)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<TerrainDisplacementShadowProgram>();
        program.CaptureShadow(focalPixels);
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        var commands = RHICommandContext.Current;
        using var vertices = commands.CreateVertexBuffer(new RHIBufferDescription
        { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 24, Usage = BufferUsage.StaticDraw,
            Label = "Tests.DisplacementShadowVertices" }).RequireResource();
        // Match the framework triangle's original vec2 position stream and its three indexed vertices.
        vertices.UploadData(new float[] { -1f, -1f, 3f, -1f, -1f, 3f });
        var layout = new RHIVertexLayout(
            [new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 8)]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = vertices });

        // Preserve the native interface oracle: the pre-world shadow executable has no camera block.
        OpenGLBufferBindings.BindBufferBase(OpenGlStateCache.Current, BufferRangeTarget.UniformBuffer,
            GpuBindingRegistry.Ubo.Frame, 0);
        Assert.Equal(-1, GL.GetUniformBlockIndex(program.ProgramId, "VgeFrameUBO"));
        GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniformBlocks, out int blockCount);
        Assert.Equal(1, blockCount);
        GL.GetActiveUniformBlock(program.ProgramId, 0, ActiveUniformBlockParameter.UniformBlockBinding,
            out int blockBinding);
        Assert.Equal(GpuBindingRegistry.Ubo.ShaderInputs, blockBinding);

        Submit(program, target, geometry, clearColor: true);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.Equal(new float[] { 7f, 7f, 1f, 1f }, target[0].ReadPixels());
    }

    /// <summary>Height mapping is signed and bounded; neutral, boundary and invalid samples remain undisplaced.</summary>
    [Theory]
    [InlineData(.5f, .25f, .5f, .04f, 0f)]
    [InlineData(1f, .25f, .5f, .04f, .04f)]
    [InlineData(0f, .25f, .5f, .04f, -.04f)]
    [InlineData(1f, 0f, .5f, .04f, 0f)]
    [InlineData(1f, .5f, .5f, .04f, 0f)]
    [InlineData(1f, .25f, .5f, .5f, .05f)]
    [InlineData(float.NaN, .25f, .5f, .04f, 0f)]
    [InlineData(float.PositiveInfinity, .25f, .5f, .04f, 0f)]
    [InlineData(1f, .49f, .5f, .04f, 0f)]
    [InlineData(1f, .4f, .5f, .04f, .00864f)]
    [InlineData(1f, float.NaN, .5f, .04f, 0f)]
    [InlineData(1f, .25f, .5f, float.NaN, 0f)]
    public void HeightAndSharedEdgeRespectBounds(float height, float u, float v, float amplitude, float expected)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<TerrainDisplacementHeightProgram>();
        using var camera = TestFrameCamera.CreateIdentity(128, 128);
        program.FrameInputs = camera;
        using var framework = new ShaderTestFramework();
        var data = new float[16 * 16 * 4];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++) data[(y * 16 + x) * 4 + 3] = x < 8 ? height : 1 - height;
        using var atlas = framework.CreateTexture(16, 16, TextureFormat.Rgba32f, data);
        program.NormalDepthAtlas = atlas;
        program.CaptureHeight(u, v, amplitude);
        using var target = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var geometry = CreateProceduralTriangle();
        Submit(program, target, geometry);

        float[] actual = target[0].ReadPixels();
        Assert.InRange(actual[0], expected - .00001f, expected + .00001f);
        Assert.Equal(4f, actual[1]);
        Assert.Equal(actual[1], actual[2]);
        Assert.Equal(1f, actual[3]);
    }
    #endregion

    #region Private
    /// <summary>Creates the original gl_VertexID triangle used by complete-state fixture draws.</summary>
    private static ArrayGraphicsGeometry CreateProceduralTriangle() => new(new RHIVertexLayout([]),
        PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3);

    /// <summary>Submits captured displacement inputs through a complete typed pipeline and render pass.</summary>
    private static void Submit(TerrainDisplacementNumericalProgram program, GpuFramebuffer target,
        ArrayGraphicsGeometry geometry, bool clearColor = false)
    {
        var commands = RHICommandContext.Current;
        IEnumerable<RHIColorAttachment>? colors = clearColor
            ? [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]
            : null;
        var pass = FramebufferTestSetup.DescribeAttachments(target, colors);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, geometry.Inputs.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.TerrainDisplacementNumerical", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });
    }
    #endregion
}
