using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks prepared graphics ownership against real compiled production executables.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PreparedGraphicsPipelineTests(HeadlessGLFixture fixture)
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
        using var pipeline = new GraphicsPipeline(lifetime, description, shader);
        var metadata = shader.GraphicsInterface!;
        int queries = metadata.ReflectionQueries, resourceQueries = pipeline.Bindings.ReflectionQueries;
        for (int i = 0; i < 100; i++) pipeline.ValidateTargets(description.Targets);
        Assert.Same(metadata, shader.GraphicsInterface);
        Assert.Equal(queries, metadata.ReflectionQueries);
        Assert.Equal(resourceQueries, pipeline.Bindings.ReflectionQueries);
        shader.InvalidateAssets();
        Assert.Throws<InvalidOperationException>(pipeline.Validate);
        Assert.True(shader.EnsureReady());
        Assert.Throws<InvalidOperationException>(pipeline.Validate);
        using var replacement = new GraphicsPipeline(lifetime, description, shader);
        Assert.True(replacement.ExecutableRevision > pipeline.ExecutableRevision);
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
    #endregion

    #region Private
    /// <summary>Declares the production fullscreen shader's explicit vertex and floating color interface.</summary>
    private static GraphicsPipelineDesc Description(GpuProgram shader, int components = 3, PixelInternalFormat format = PixelInternalFormat.Rgba16f) =>
        new(shader.GraphicsIdentity!, new([new(0, components, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12)]),
            new(shader is VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram
                ? [new(format), new(format), new(PixelInternalFormat.R32f)] : [new(format)]), DynamicPipelineState.Viewport);
    #endregion
}

