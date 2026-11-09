using VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;



using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Runs authored relief through the production shader include with physical surface derivatives.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainReliefNumericalTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared headless context.</summary>
    public TerrainReliefNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Neutral or unavailable material data preserves UV; valid relief follows world scale and mirroring.</summary>
    [Theory]
    [InlineData(0, .04f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(2, .04f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(1, 0f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, .5f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, 1f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 0f, 2f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 1f, 20f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 1f, .1f, 0f)]
    [InlineData(1, .02f, 0f, 1f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, 1f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, 2f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, -1f, 1f, 2f, -1f)]
    public void ReliefUsesAuthoredMetric(int mode, float amplitude, float height, float scale, float index, float distance, float direction)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        TerrainReliefProgram program = mode == 1
            ? programs.Create<TerrainReliefEnabledProgram>()
            : programs.Create<TerrainReliefDisabledProgram>();
        using var drawing = new ShaderTestFramework();
        using var heights = drawing.CreateTexture(64, 64, TextureFormat.Rgba32f,
            Enumerable.Range(0, 4096).SelectMany(_ => new[] { .5f, .5f, 1f, height }).ToArray());
        using var indices = drawing.CreateTexture(1, 1, TextureFormat.R32f, [index]);
        using var records = drawing.CreateTexture(2, 1, TextureFormat.Rgba32f, [0f, 0f, 1f, 1f, amplitude, 0f, 0f, 0f]);
        using var target = drawing.CreateTestGBuffer(32, 32, TextureFormat.Rgba32f);
        program.HeightTexture = heights;
        program.DisplacementIndices = indices;
        program.DisplacementRecords = records;
        program.Capture(scale, distance);

        using var frameCamera = TestFrameCamera.CreateIdentity(32, 32);
        program.FrameInputs = frameCamera;
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.TerrainRelief", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));

        // Act: bind the complete captured material and frame state, then draw the catalogued fixture triangle.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });

        // Assert: retain the authored center-pixel metric, signed displacement and full-image predicate.
        float[] pixels = target[0].ReadPixelsRegion(16, 16, 1, 1);
        if (direction == 0) Assert.InRange(pixels[0], -.000001f, .000001f);
        else
        {
            float expected = amplitude * (1f + scale / 64f) / (Math.Abs(scale) * distance);
        // Assert: compare the observed result with the expected contract.
            Assert.InRange(pixels[0] * direction, expected * .92f, expected * 1.02f);
        }
        ImageAssertions.Predicates.Predicate(target[0], GeometryPredicates.TerrainReliefNumericalTests_ReliefUsesAuthoredMetric).AssertEqual();
    }
    #endregion

    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-geometry", 0, 0);
    }
    #endregion
}
