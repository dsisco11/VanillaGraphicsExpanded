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
    private readonly GraphicsPipelineLifetime graphicsLifetime = new();
    private readonly Dictionary<string, GraphicsPipeline> graphicsPipelines = new();

    #region Private
    /// <summary>Prepares one retained executable realization and restores the incoming engine state around its draw.</summary>
    private bool SubmitFullscreen(GpuProgram shader, GpuFramebuffer target, bool clear)
    {
        // Target metadata resolves mip dimensions and exact MRT formats without native draw-time reflection.
        var slots = target.AttachmentImages
            .Where(pair => pair.Key >= FramebufferAttachment.ColorAttachment0 && pair.Key <= FramebufferAttachment.ColorAttachment15)
            .Select(pair => (int)pair.Key - (int)FramebufferAttachment.ColorAttachment0).ToHashSet();
        var outputs = Enumerable.Range(0, slots.Count == 0 ? 0 : slots.Max() + 1)
            .Select(slot => slots.Contains(slot) ? new RenderPassColor(slot,
                clear ? AttachmentLoad.Clear : AttachmentLoad.Preserve, Clear: clear ? ColorClearValue.Float(0, 0, 0, 0) : null)
                : new RenderPassColor(-1)).ToArray();
        var description = new RenderPassDesc(target, outputs);
        using var metadata = new RenderPassTargets(description);
        graphicsPipelines.TryGetValue(shader.PassName, out var pipeline);
        if (pipeline is null || !ReferenceEquals(pipeline.Shader, shader)
            || pipeline.ExecutableRevision != shader.ExecutableRevision || pipeline.Description.Targets != metadata.Signature)
        {
            var replacement = new GraphicsPipeline(graphicsLifetime,
                new(shader.GraphicsIdentity!, EngineFullscreenGeometry.Layout, metadata.Signature, DynamicPipelineState.Viewport), shader);
            pipeline?.Dispose();
            graphicsPipelines[shader.PassName] = pipeline = replacement;
        }
        return GraphicsCommandContext.TryRun("LumOn." + shader.PassName, [pipeline], true, commands =>
        {
            commands.BeginPass(description);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry!, new(0, 6));
            commands.EndPass();
        });
    }

    /// <summary>Releases retained realizations without changing shader or attachment ownership.</summary>
    private void DisposeGraphicsPipelines()
    {
        foreach (var pipeline in graphicsPipelines.Values) pipeline.Dispose();
        graphicsPipelines.Clear();
        graphicsLifetime.Dispose();
    }
    #endregion
}
