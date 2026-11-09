using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks prepared graphics ownership against real compiled production executables.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PreparedGraphicsPipelineTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API
    /// <summary>Repeated validation reuses metadata and compatible reload requires an explicit replacement.</summary>
    [Fact]
    public void CompatibleReloadInvalidatesBorrowedRealization()
    {
        // Arrange: Prepare a compatible executable and retain its borrowed pipeline realization across reload.
        // Act and assert: Repeat validation, reload, and explicitly replace the realization when required.
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        var description = Description(shader);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
        var realization = Assert.IsType<OpenGLGraphicsPipelineRealization>(pipeline.Realization);
        var relevance = realization.Relevance;
        var metadata = shader.GraphicsInterface!;
#if DEBUG
        using var acquisition = new ProgramAcquisitionCapture();
        int resourceQueries = acquisition.Reads;
#endif
        for (int i = 0; i < 100; i++) pipeline.ValidateTargets(description.Targets);
        Assert.Same(realization, pipeline.Realization);
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
        var replacementState = Assert.IsType<OpenGLGraphicsPipelineRealization>(replacement.Realization);
        Assert.NotSame(realization, replacementState);
        Assert.Equal(relevance.Rasterizer, replacementState.Relevance.Rasterizer);
        Assert.Equal(relevance.Depth, replacementState.Relevance.Depth);
        Assert.Equal(relevance.Blending, replacementState.Relevance.Blending);
        Assert.NotNull(replacement.RequireShader());
    }

    /// <summary>Managed disposal and renderer teardown invalidate use without owning the shader dependency.</summary>
    [Fact]
    public void DisposalAndRendererTeardownRejectUse()
    {
        // Arrange: Prepare a pipeline borrowed from the renderer-owned shader dependency.
        // Act and assert: Dispose the wrapper and tear down its renderer, verifying further use is rejected without owning the shader.
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var first = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        first.Dispose();
#if DEBUG
        Assert.Throws<ObjectDisposedException>(() => first.RequireShader());
#endif
        Assert.True(shader.IsLinked);
        using var second = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        second.Dispose();
#if DEBUG
        Assert.Throws<ObjectDisposedException>(() => second.RequireShader());
#endif
        using var third = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.NotNull(third.RequireShader());
        Assert.True(shader.IsLinked);
    }

    /// <summary>Bad interfaces and target signatures reject without damaging a previously valid realization.</summary>
    [Fact]
    public void InterfaceAndTargetFailuresDoNotPublish()
    {
        // Arrange: Retain one valid realization and prepare incompatible shader-interface and target descriptions.
        // Act and assert: Reject each candidate while preserving the previously published realization.
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
        // Arrange: Prepare a structural shader selection and a description for its previous executable.
        // Act and assert: Change the selection and verify stale description reuse is rejected.
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>();
        var description = Description(shader);
        using var original = RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
        shader.EnableShortRangeAo = !shader.EnableShortRangeAo;
        Assert.True(shader.EnsureReady());
        Assert.ThrowsAny<Exception>(() => original.RequireShader());
        Assert.Throws<ObjectDisposedException>(() => RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource());
        using var replacement = RHICommandContext.Current.CreateGraphicsPipeline(Description(shader)).RequireResource();
        Assert.NotNull(replacement.RequireShader());
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

