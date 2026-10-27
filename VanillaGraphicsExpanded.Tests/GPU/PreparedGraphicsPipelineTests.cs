using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks prepared graphics ownership against real compiled production executables.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PreparedGraphicsPipelineTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API
    /// <summary>Repeated validation reuses metadata and compatible reload requires an explicit replacement.</summary>
    [Fact]
    public void CompatibleReloadInvalidatesBorrowedRealization()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        var description = Description(shader);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
        var relevance = pipeline.Relevance;
        var metadata = shader.GraphicsInterface!;
#if DEBUG
        using var acquisition = new ProgramAcquisitionCapture();
        int resourceQueries = acquisition.Reads;
#endif
        for (int i = 0; i < 100; i++) pipeline.ValidateTargets(description.Targets);
        Assert.Same(relevance, pipeline.Relevance);
        Assert.Same(metadata, shader.GraphicsInterface);
#if DEBUG
        Assert.Equal(resourceQueries, acquisition.Reads);
        output.WriteLine("Repeated fixed-pipeline validation reuses prepared resource facts.");
#endif
        shader.InvalidateAssets();
        Assert.NotNull(pipeline.RequireShader());
        Assert.True(shader.EnsureReady());
        Assert.ThrowsAny<Exception>(() => pipeline.RequireShader());
        using var replacement = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.NotEqual(pipeline.Description.Shader, replacement.Description.Shader);
        Assert.NotSame(relevance, replacement.Relevance);
        Assert.Equal(relevance.Rasterizer, replacement.Relevance.Rasterizer);
        Assert.Equal(relevance.Depth, replacement.Relevance.Depth);
        Assert.Equal(relevance.Blending, replacement.Relevance.Blending);
        Assert.NotNull(replacement.RequireShader());
    }

    /// <summary>Managed disposal and renderer teardown invalidate use without owning the shader dependency.</summary>
    [Fact]
    public void DisposalAndRendererTeardownRejectUse()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var first = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.RequireShader());
        Assert.True(shader.IsLinked);
        using var second = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        second.Dispose();
        Assert.Throws<ObjectDisposedException>(() => second.RequireShader());
        using var third = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.NotNull(third.RequireShader());
        Assert.True(shader.IsLinked);
    }

    /// <summary>Bad interfaces and target signatures reject without damaging a previously valid realization.</summary>
    [Fact]
    public void InterfaceAndTargetFailuresDoNotPublish()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var valid = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateGraphicsPipeline(Description(shader, 2)).RequireResource());
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateGraphicsPipeline(Description(shader, format: TextureFormat.Rgba8ui)).RequireResource());
        Assert.Throws<InvalidOperationException>(() => valid.ValidateTargets(new([new(TextureFormat.Rgba16f)], samples: 4)));
        var original = valid.Description;
        var accidentalHole = new RHIGraphicsPipelineDescription(original.Shader, original.VertexLayout,
            new([new(null), new(TextureFormat.Rgba16f)]), original.Dynamics);
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateGraphicsPipeline(accidentalHole).RequireResource());
        var intentionalDiscard = new RHIGraphicsPipelineDescription(original.Shader, original.VertexLayout,
            new([new(null, true), new(TextureFormat.Rgba16f)]), original.Dynamics);
        using var discarded = RHICommandContext.Current.CreateGraphicsPipeline(intentionalDiscard).RequireResource();
        Assert.NotNull(discarded.RequireShader());
        Assert.NotNull(valid.RequireShader());
        shader.Dispose();
        Assert.ThrowsAny<Exception>(() => valid.RequireShader());
    }
    /// <summary>Changed structural shader selections cannot reuse a description for the previous executable interface.</summary>
    [Fact]
    public void ChangedShaderSelectionRequiresMatchingDescription()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>();
        var description = Description(shader);
        using var original = RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
        shader.EnableShortRangeAo = !shader.EnableShortRangeAo;
        Assert.True(shader.EnsureReady());
        Assert.ThrowsAny<Exception>(() => original.RequireShader());
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource());
        using var replacement = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.NotNull(replacement.RequireShader());
    }

    /// <summary>Existing linked terrain tessellation metadata refines unknown patch input to verified polygon output.</summary>
    [Fact]
    public void VerifiedTessellationOutputRefinesPrimitiveRelevanceWithoutQueries()
    {
        fixture.MakeCurrent();
        using var stages = new TerrainShaderTestFixture();
        int vertex = stages.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.vsh", 2, 0, 0, 0, 0));
        int fragment = stages.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.fsh", 2, 0, 0, 0, 0));
        var sources = TerrainTessellationTestAssets.Generate(stages.Source(vertex));
        var selectedStages = TerrainTessellationStages.Describe(sources, TerrainTessellationPatches.EnabledDefine);
        using var program = RHICommandContext.Current.OpenGL.CreateEngineTessellationProgram(vertex, fragment,
            selectedStages.Control, selectedStages.Evaluation, null).RequireResource();
        var kinds = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment,
            ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation };
        var contract = new GpuShaderContract("linked-terrain", kinds.Select(kind =>
            new ShaderStageContract(kind.ToString(), kind.ToString(), kind, new())), 1);
        var plan = new ShaderLoadPlan(new ShaderSettings(contract));
        // This test consumes only linked primitive modes, not packaged vertex/output admission.
        var metadata = new GraphicsExecutableInterface(GpuResourceManager.RequireProgramSnapshot(program));
        Assert.Equal(PrimitiveTopology.Triangles, metadata.TessellationOutput);
        using var programs = new ComponentShaderPrograms();
        var reference = programs.Create<RasterDepthReductionShaderProgram>().Shader;
        var description = new RHIGraphicsPipelineDescription(reference, new([]),
            new([new(TextureFormat.Rgba16f)]), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Patches });
        var conservative = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics);
        var prepared = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics, metadata);
        Assert.True(conservative.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.FrontFace));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the production fullscreen shader's explicit vertex and floating color interface.</summary>
    private static RHIGraphicsPipelineDescription Description(GpuProgram shader, int components = 3, TextureFormat format = TextureFormat.Rgba16f) =>
        new(shader.Shader, new([new(0, components, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]),
            new(shader is VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram
                ? [new(format), new(format), new(TextureFormat.R32f)] : [new(format)]), DynamicPipelineState.Viewport);
    #endregion
}

