using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Establishes complete observable graphics intent while preserving dormant native parameters.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Derives conservative emitted-primitive relevance for low-level callers without a prepared executable.</summary>
    internal void ApplyGraphicsState(RHIGraphicsPipelineDescription pipeline, RHIDynamicState dynamics)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var relevance = GraphicsStateRelevance.Derive(pipeline, GpuSupport.Graphics);
        ApplyGraphicsState(pipeline, dynamics, relevance, PipelineStateCoverage.From(relevance));
    }
    /// <summary>Validates the whole request before category owners apply relevant groups through normal cached setters.</summary>
    internal void ApplyGraphicsState(RHIGraphicsPipelineDescription pipeline, RHIDynamicState dynamics,
        GraphicsStateRelevance relevance, PipelineStateCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(dynamics);
        PipelineDescriptionValidation.Validate(pipeline, GpuSupport.Graphics);
        dynamics.ValidateFor(pipeline);
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

    #endregion

    #region Private
    /// <summary>Applies only consumed dynamic tuples; undeclared values retain concrete defaults when relevant.</summary>
    private void ApplyPipelineDynamics(RHIGraphicsPipelineDescription pipeline, RHIDynamicState dynamics,
        DynamicDrawStateKnowledge relevant)
    {
        if (relevant.HasFlag(DynamicDrawStateKnowledge.Viewport))
        {
            var viewport = dynamics.Viewport!.Value;
            ApplyDynamic(new DynamicDrawState { X = viewport.X, Y = viewport.Y, Width = viewport.Width, Height = viewport.Height });
        }
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
}
