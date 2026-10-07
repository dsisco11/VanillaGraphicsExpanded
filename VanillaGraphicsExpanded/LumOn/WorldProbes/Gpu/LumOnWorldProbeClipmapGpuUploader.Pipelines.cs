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
    private static readonly VertexLayoutDesc ProbeLayout = new([
        new(0, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 40),
        new(1, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 8, 40),
        new(2, 1, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 20, 40),
        new(3, 1, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 24, 40),
        new(4, 1, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 28, 40),
        new(5, 1, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 32, 40),
        new(6, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 0, 36, 40)]);
    private static readonly VertexLayoutDesc TileLayout = new([
        new(0, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 24),
        new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 8, 24)]);
    private static readonly RenderPassColor[] TileOutputs = [new(0)];
    private static readonly RenderPassColor[] ProbeOutputs = [new(0), new(1), new(2)];
    private readonly GraphicsPipelineLifetime pipelineLifetime = new();
    private GraphicsPipeline? tilePipeline, probePipeline;

    #region Private
    /// <summary>Revalidates executable and exact target interfaces before beginning either publication pass.</summary>
    private GraphicsPipeline PreparePipeline(ref GraphicsPipeline? slot, GpuProgram shader,
        VertexLayoutDesc layout, GpuFramebuffer target, RenderPassColor[] outputs)
    {
        using var metadata = new RenderPassTargets(new(target, outputs));
        if (slot is not null && ReferenceEquals(slot.Shader, shader)
            && slot.ExecutableRevision == shader.ExecutableRevision && slot.Description.Targets == metadata.Signature) return slot;
        var replacement = new GraphicsPipeline(pipelineLifetime,
            new(shader.GraphicsIdentity!, layout, metadata.Signature, DynamicPipelineState.Viewport,
                assembly: new() { Topology = PrimitiveType.Points }, rasterizer: new() { PointSize = 1 }), shader);
        slot?.Dispose();
        return slot = replacement;
    }

    /// <summary>Writes admitted points without clearing other resident probes or changing their storage ownership.</summary>
    private static void Submit(GraphicsCommandContext commands, GraphicsPipeline pipeline,
        ArrayGraphicsGeometry geometry, GpuFramebuffer target, RenderPassColor[] outputs, int count)
    {
        commands.BeginPass(new(target, outputs));
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.Draw(geometry, new(0, count));
        commands.EndPass();
    }
    #endregion
}
