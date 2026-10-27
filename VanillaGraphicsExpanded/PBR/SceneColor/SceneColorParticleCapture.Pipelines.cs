using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    private GraphicsPipelineAsset? resolvePipeline, ssaoPipeline;

    #region Private
    /// <summary>Validates borrowed target metadata and atomically replaces an obsolete executable realization.</summary>
    private RHIGraphicsPipeline PreparePipeline(ref GraphicsPipelineAsset? slot, GpuProgram shader, RHIRenderPassDescription pass)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        var signature=RHICommandContext.Current.DescribeRenderTargets(pass);
        if (slot is not null && slot.Description.Shader == shader.Shader && slot.Description.Targets == signature) return slot.Pipeline;
        var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, EngineFullscreenGeometry.Layout, signature, DynamicPipelineState.Viewport));
        slot?.Dispose();
        slot = replacement;
        return replacement.Pipeline;
    }

    /// <summary>Preserves the borrowed outputs outside the fullscreen resolve and restores shader ownership after submission.</summary>
    private void Submit(RHICommandContext commands, RHIGraphicsPipeline pipeline, RHIRenderPassDescription pass,RHIResourceBindings inputs)
    {
        commands.BeginRenderPass(pass);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.BindResources(inputs);
        commands.DrawIndexed(geometry.Inputs,new(0,6));
        commands.EndRenderPass();
    }
    #endregion
}
