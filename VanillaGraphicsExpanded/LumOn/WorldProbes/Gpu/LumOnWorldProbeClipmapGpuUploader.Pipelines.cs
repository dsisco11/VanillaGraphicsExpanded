using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares point-stream layouts and complete state for ordered atlas publication.</summary>
internal sealed partial class LumOnWorldProbeClipmapGpuUploader
{
    private static readonly RHIVertexLayout ProbeLayout = new([
        new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 40),
        new(1, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 40),
        new(2, 1, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 20, 40),
        new(3, 1, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 24, 40),
        new(4, 1, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 28, 40),
        new(5, 1, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 32, 40),
        new(6, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 0, 36, 40)]);
    private static readonly RHIVertexLayout TileLayout = new([
        new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 24),
        new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 24)]);
    private GraphicsPipelineAsset? tilePipeline, probePipeline;

    #region Private
    /// <summary>Revalidates executable and exact target interfaces before beginning either publication pass.</summary>
    private RHIGraphicsPipeline PreparePipeline(ref GraphicsPipelineAsset? slot, GpuProgram shader,
        RHIVertexLayout layout, RHIRenderPassDescription target)
    {
        var signature=RHICommandContext.Current.DescribeRenderTargets(target);
        if (slot is not null && slot.Description.Shader == shader.Shader && slot.Description.Targets == signature) return slot.Pipeline;
        var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, layout, signature, DynamicPipelineState.Viewport,
                assembly: new() { Topology = PrimitiveTopology.Points }, rasterizer: new() { PointSize = 1 }));
        slot?.Dispose();
        slot = replacement;
        return replacement.Pipeline;
    }

    /// <summary>Writes admitted points without clearing other resident probes or changing their storage ownership.</summary>
    private static void Submit(RHICommandContext commands, RHIGraphicsPipeline pipeline,
        ArrayGraphicsGeometry geometry, RHIRenderPassDescription target, int count, RHIResourceBindings inputs)
    {
        commands.BeginRenderPass(target);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.BindResources(inputs);
        commands.Draw(geometry.Inputs, new(0, count));
        commands.EndRenderPass();
    }
    #endregion
}
