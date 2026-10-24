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
        using var lifetime = new GraphicsPipelineLifetime();
        var description = Description(shader);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        using var pipeline = new GraphicsPipeline(lifetime, description, shader);
        var relevance = pipeline.Relevance;
        double preparation = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var metadata = shader.GraphicsInterface!;
#if DEBUG
        int queries = metadata.ReflectionQueries, resourceQueries = pipeline.Bindings.ReflectionQueries;
#endif
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++) pipeline.ValidateTargets(description.Targets);
        Assert.Same(relevance, pipeline.Relevance);
        double validation = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Assert.Same(metadata, shader.GraphicsInterface);
#if DEBUG
        Assert.Equal(queries, metadata.ReflectionQueries);
        Assert.Equal(resourceQueries, pipeline.Bindings.ReflectionQueries);
        output.WriteLine($"Preparation: {preparation:F3} ms; graphics queries={queries}; resource queries={resourceQueries}; " +
            $"100 retained validations: {validation:F3} ms; additional reflection queries=0.");
#endif
        shader.InvalidateAssets();
        Assert.Throws<InvalidOperationException>(pipeline.Validate);
        Assert.True(shader.EnsureReady());
        Assert.Throws<InvalidOperationException>(pipeline.Validate);
        using var replacement = new GraphicsPipeline(lifetime, description, shader);
        Assert.True(replacement.ExecutableRevision > pipeline.ExecutableRevision);
        Assert.NotSame(relevance, replacement.Relevance);
        Assert.Equal(relevance.Rasterizer, replacement.Relevance.Rasterizer);
        Assert.Equal(relevance.Depth, replacement.Relevance.Depth);
        Assert.Equal(relevance.Blending, replacement.Relevance.Blending);
        replacement.Validate();
    }

    /// <summary>Managed disposal and renderer teardown invalidate use without owning the shader dependency.</summary>
    [Fact]
    public void DisposalAndRendererTeardownRejectUse()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var first = new GraphicsPipeline(lifetime, Description(shader), shader);
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(first.Validate);
        Assert.True(shader.IsLinked);
        using var second = new GraphicsPipeline(lifetime, Description(shader), shader);
        lifetime.Dispose();
        Assert.Throws<ObjectDisposedException>(second.Validate);
        Assert.Throws<ObjectDisposedException>(() => new GraphicsPipeline(lifetime, Description(shader), shader));
        Assert.True(shader.IsLinked);
    }

    /// <summary>Bad interfaces and target signatures reject without damaging a previously valid realization.</summary>
    [Fact]
    public void InterfaceAndTargetFailuresDoNotPublish()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var valid = new GraphicsPipeline(lifetime, Description(shader), shader);
        Assert.Throws<InvalidOperationException>(() => new GraphicsPipeline(lifetime, Description(shader, 2), shader));
        Assert.Throws<InvalidOperationException>(() => new GraphicsPipeline(lifetime, Description(shader, format: PixelInternalFormat.Rgba8ui), shader));
        Assert.Throws<InvalidOperationException>(() => valid.ValidateTargets(new([new(PixelInternalFormat.Rgba16f)], samples: 4)));
        var original = valid.Description;
        var accidentalHole = new GraphicsPipelineDesc(original.Shader, original.VertexLayout,
            new([new(null), new(PixelInternalFormat.Rgba16f)]), original.Dynamics);
        Assert.Throws<InvalidOperationException>(() => new GraphicsPipeline(lifetime, accidentalHole, shader));
        var intentionalDiscard = new GraphicsPipelineDesc(original.Shader, original.VertexLayout,
            new([new(null, true), new(PixelInternalFormat.Rgba16f)]), original.Dynamics);
        using var discarded = new GraphicsPipeline(lifetime, intentionalDiscard, shader);
        discarded.Validate();
        valid.Validate();
        shader.Dispose();
        Assert.Throws<InvalidOperationException>(valid.Validate);
    }
    /// <summary>Changed structural shader selections cannot reuse a description for the previous executable interface.</summary>
    [Fact]
    public void ChangedShaderSelectionRequiresMatchingDescription()
    {
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        var description = Description(shader);
        using var original = new GraphicsPipeline(lifetime, description, shader);
        shader.EnableShortRangeAo = !shader.EnableShortRangeAo;
        Assert.True(shader.EnsureReady());
        Assert.Throws<InvalidOperationException>(original.Validate);
        Assert.Throws<InvalidOperationException>(() => new GraphicsPipeline(lifetime, description, shader));
        using var replacement = new GraphicsPipeline(lifetime, Description(shader), shader);
        replacement.Validate();
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
        Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, sources, TerrainTessellationPatches.EnabledDefine,
            out int linked, out string error), error);
        using var program = GpuProgramObject.Adopt(linked);
        var kinds = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment,
            ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation };
        var contract = new GpuShaderContract("linked-terrain", kinds.Select(kind =>
            new ShaderStageContract(kind.ToString(), kind.ToString(), kind, new())), 1);
        var plan = new ShaderLoadPlan(new ShaderSettings(contract));
        // This test consumes only linked primitive modes, not packaged vertex/output admission.
        var metadata = new GraphicsExecutableInterface(linked, plan, _ => new([], [], []));
        Assert.Equal(PrimitiveType.Triangles, metadata.TessellationOutput);
        var description = new GraphicsPipelineDesc(new("fixture", plan), new([]),
            new([new(PixelInternalFormat.Rgba16f)]), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveType.Patches });
#if DEBUG
        int queries = metadata.ReflectionQueries;
#endif
        var conservative = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics);
        var prepared = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics, metadata);
        Assert.True(conservative.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.FrontFace));
#if DEBUG
        Assert.Equal(queries, metadata.ReflectionQueries);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the production fullscreen shader's explicit vertex and floating color interface.</summary>
    private static GraphicsPipelineDesc Description(GpuProgram shader, int components = 3, PixelInternalFormat format = PixelInternalFormat.Rgba16f) =>
        new(shader.GraphicsIdentity!, new([new(0, components, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12)]),
            new(shader is VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram
                ? [new(format), new(format), new(PixelInternalFormat.R32f)] : [new(format)]), DynamicPipelineState.Viewport);
    #endregion
}

