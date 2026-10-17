using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Owns the two-output receiver and SSAO graphics realizations without owning their shaders.</summary>
internal sealed partial class SceneColorParticleCapture
{
    private readonly GraphicsPipelineLifetime pipelineLifetime = new();
    private GraphicsPipeline? resolvePipeline, ssaoPipeline;

    #region Private
    /// <summary>Validates borrowed target metadata and atomically replaces an obsolete executable realization.</summary>
    private GraphicsPipeline PreparePipeline(ref GraphicsPipeline? slot, GpuProgram shader, RenderPassDesc pass)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        using var metadata = new RenderPassTargets(pass);
        if (slot is not null && ReferenceEquals(slot.Shader, shader)
            && slot.ExecutableRevision == shader.ExecutableRevision && slot.Description.Targets == metadata.Signature) return slot;
        var replacement = new GraphicsPipeline(pipelineLifetime,
            new(shader.GraphicsIdentity!, EngineFullscreenGeometry.Layout, metadata.Signature, DynamicPipelineState.Viewport), shader);
        slot?.Dispose();
        return slot = replacement;
    }

    /// <summary>Preserves the borrowed outputs outside the fullscreen resolve and restores shader ownership after submission.</summary>
    private void Submit(GraphicsCommandContext commands, GraphicsPipeline pipeline, RenderPassDesc pass)
    {
        commands.BeginPass(pass);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.Draw(geometry, new(0, 6));
        commands.EndPass();
    }
    #endregion
}
