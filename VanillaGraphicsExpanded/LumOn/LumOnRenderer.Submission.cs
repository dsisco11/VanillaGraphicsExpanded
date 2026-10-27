using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Submits gather graphics passes independently of the interleaved compute and explicit resource operations.</summary>
public partial class LumOnRenderer
{
    private readonly Dictionary<string, GraphicsPipelineAsset> graphicsPipelines = new();

    #region Private
    /// <summary>Prepares one retained executable realization and restores the incoming engine state around its draw.</summary>
    private bool SubmitFullscreen(GpuProgram shader, RHIRenderPassDescription target, bool clear)
    {
        var description = bufferManager.GetPass(target, clear);
        var signature=RHICommandContext.Current.DescribeRenderTargets(description);
        graphicsPipelines.TryGetValue(shader.PassName, out var pipeline);
        if (pipeline is null || pipeline.Description.Shader != shader.Shader || pipeline.Description.Targets != signature)
        {
            var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, EngineFullscreenGeometry.Layout, signature, DynamicPipelineState.Viewport));
            pipeline?.Dispose();
            graphicsPipelines[shader.PassName] = pipeline = replacement;
        }
        var commands=RHICommandContext.Current;
        if(!commands.TryEnterBoundary(new("LumOn."+shader.PassName,true,graphics:[pipeline.Pipeline],operations:BoundaryOperations.RenderPasses),out var boundary)) return false;
        using(boundary) boundary!.Run(()=>
        {
            commands.BeginRenderPass(description);
            commands.SetPipeline(pipeline.Pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            commands.DrawIndexed(geometry!.Inputs,new(0,6));
            commands.EndRenderPass();
        });
        return true;
    }

    /// <summary>Releases retained realizations without changing shader or attachment ownership.</summary>
    private void DisposeGraphicsPipelines()
    {
        foreach (var pipeline in graphicsPipelines.Values) pipeline.Dispose();
        graphicsPipelines.Clear();
    }
    #endregion
}
