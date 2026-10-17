using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Establishes complete observable graphics intent while preserving dormant native parameters.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Uses the same prepared relevance and native write groups declared at boundary entry.</summary>
    internal void ApplyGraphicsState(GraphicsPipeline pipeline, GraphicsDynamicState dynamics)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        pipeline.Validate();
        ApplyGraphicsState(pipeline.Description, dynamics, pipeline.Relevance, pipeline.Coverage);
    }

    /// <summary>Derives conservative emitted-primitive relevance for low-level callers without a prepared executable.</summary>
    internal void ApplyGraphicsState(GraphicsPipelineDesc pipeline, GraphicsDynamicState dynamics)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var relevance = GraphicsStateRelevance.Derive(pipeline, GpuSupport.Graphics);
        ApplyGraphicsState(pipeline, dynamics, relevance, PipelineStateCoverage.From(relevance));
    }
    #endregion

    #region Private
    #region Application
    /// <summary>Validates the whole request before category owners apply relevant groups through normal cached setters.</summary>
    private void ApplyGraphicsState(GraphicsPipelineDesc pipeline, GraphicsDynamicState dynamics,
        GraphicsStateRelevance relevance, PipelineStateCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(dynamics);
        PipelineDescriptionValidation.Validate(pipeline, GpuSupport.Graphics);
        ValidateGraphicsDynamics(pipeline, dynamics);
        ValidateBoundaryMutation(coverage);
        EnsureViewportLimits();
        // Skipped groups keep their actual cached values and knowledge, never the dormant declaration.
        ApplyPipelineDepthStencil(pipeline, dynamics, relevance);
        ApplyPipelineRasterizer(pipeline, relevance.Rasterizer);
        ApplyPipelineBlending(pipeline, relevance);
        ApplyPipelineSampling(pipeline.Sampling, relevance);
        ApplyPipelineAssembly(pipeline.Assembly, relevance.Assembly);
        ApplyPipelineOutput(pipeline.Output, relevance.Output);
        ApplyPipelineDynamics(pipeline, dynamics, relevance.Dynamic);
    }

    /// <summary>Applies only consumed dynamic tuples; undeclared values retain concrete defaults when relevant.</summary>
    private void ApplyPipelineDynamics(GraphicsPipelineDesc pipeline, GraphicsDynamicState dynamics,
        DynamicDrawStateKnowledge relevant)
    {
        if (relevant.HasFlag(DynamicDrawStateKnowledge.Viewport)) ApplyDynamic(dynamics.Viewport!.Value);
        if (relevant.HasFlag(DynamicDrawStateKnowledge.Scissor))
        {
            var scissor = pipeline.Dynamics.HasFlag(DynamicPipelineState.Scissor)
                ? dynamics.Scissor!.Value : (X: 0, Y: 0, Width: 0, Height: 0);
            SetScissor(scissor.X, scissor.Y, scissor.Width, scissor.Height);
        }
        if (relevant.HasFlag(DynamicDrawStateKnowledge.BlendConstant))
        {
            var constant = pipeline.Dynamics.HasFlag(DynamicPipelineState.BlendConstant)
                ? dynamics.BlendConstant!.Value : (R: 0f, G: 0f, B: 0f, A: 0f);
            SetBlendConstant(constant.R, constant.G, constant.B, constant.A);
        }
    }
    #endregion

    #region Validation
    /// <summary>Retains explicit dynamic-value requirements even when their static feature is dormant.</summary>
    private static void ValidateGraphicsDynamics(GraphicsPipelineDesc pipeline, GraphicsDynamicState value)
    {
        if (value.Viewport is not { } viewport || viewport.Width < 0 || viewport.Height < 0)
            throw new ArgumentException("A valid viewport is required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.Scissor)
            && (value.Scissor is not { } scissor || scissor.Width < 0 || scissor.Height < 0))
            throw new ArgumentException("A valid scissor rectangle is required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.StencilReference)
            && (value.StencilReference is not { } references || references.Front < 0 || references.Front > 255 || references.Back < 0 || references.Back > 255))
            throw new ArgumentException("Stencil references are required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.BlendConstant)
            && (value.BlendConstant is not { } blend || !float.IsFinite(blend.R) || !float.IsFinite(blend.G) || !float.IsFinite(blend.B) || !float.IsFinite(blend.A)))
            throw new ArgumentException("A finite blend constant is required.", nameof(value));
    }
    #endregion
    #endregion
}
