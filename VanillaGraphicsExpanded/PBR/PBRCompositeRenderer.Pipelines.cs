using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Prepares the finite composition destinations and submits their explicit fullscreen passes.</summary>
public sealed partial class PBRCompositeRenderer
{
    private static readonly RenderPassColor[] SingleOutput = [new(0)];
    private static readonly RenderPassColor[] PairOutputs = [new(0), new(1)];
    private static readonly RenderPassColor[] CompositeOutputs = [new(0), new(-1, DiscardOutput: true), new(-1, DiscardOutput: true)];
    private static readonly RenderPassColor[] ReceiverOutputs = [new(0), new(1), new(2)];
    private static readonly RenderPassColor[] CaptureOutputs = [new(-1), new(1), new(2)];
    private static readonly RenderTargetSignature CompositeTargets = new([new(PixelInternalFormat.R11fG11fB10f), new(null, true), new(null, true)]);
    private static readonly RenderTargetSignature ReceiverTargets = new([new(PixelInternalFormat.R11fG11fB10f), new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.R32f)]);
    private static readonly RenderTargetSignature CaptureTargets = new([new(null), new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.R32f)]);
    private static readonly RenderTargetSignature ReductionTargets = new([new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba32f)]);
    private readonly GraphicsPipelineLifetime pipelineLifetime = new();
    private GraphicsPipeline? compositePipeline, receiverPipeline, capturePipeline, displayPipeline, reductionPipeline;

    #region Internal API
    /// <summary>Prepares all possible draw destinations before resolving the boundary's complete resource footprint.</summary>
    internal GraphicsPipeline[]? PrepareBoundaryPipelines(bool capture)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        var shader = PrepareCompositeProgram(!capture && readLightingMode(), capture);
        if (shader is null) return null;
        if (capture) return [PreparePipeline(ref capturePipeline, shader, CaptureTargets)];

        // Both receiver allocation success and its optional fallback must be declared before entry.
        var pipelines = new List<GraphicsPipeline>
        {
            PreparePipeline(ref compositePipeline, shader, CompositeTargets),
            PreparePipeline(ref receiverPipeline, shader, ReceiverTargets)
        };
        var display = GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        if (display?.EnsureReady() != true) return null;
        var primaryColor = gBufferManager.PrimaryFramebuffer.GetAttachment(FramebufferAttachment.ColorAttachment0)
            ?? throw new InvalidOperationException("Primary color metadata is unavailable.");
        pipelines.Add(PreparePipeline(ref displayPipeline, display,
            new([new(primaryColor.InternalFormat)], samples: primaryColor.Samples)));
        var ssao = SceneColor.SceneColorParticleCapture.PrepareSsaoPipeline(capi);
        if (ssao is not null) pipelines.Add(ssao);
        if (ConfigModSystem.Config.WaterRefractionEnabled && ConfigModSystem.Config.WaterRefractionBackgroundScale == 1)
        {
            var reduction = GpuShaderPrograms.Get<Liquids.WaterRefractionReductionShaderProgram>(capi, "water_refraction_reduce");
            if (reduction?.EnsureReady() == true)
                pipelines.Add(PreparePipeline(ref reductionPipeline, reduction, ReductionTargets));
        }
        return pipelines.ToArray();
    }
    #endregion

    #region Private
    /// <summary>Replaces one executable realization only after its complete replacement validates.</summary>
    private GraphicsPipeline PreparePipeline(ref GraphicsPipeline? slot, GpuProgram shader, RenderTargetSignature targets)
    {
        if (slot is not null && ReferenceEquals(slot.Shader, shader)
            && slot.ExecutableRevision == shader.ExecutableRevision && slot.Description.Targets == targets) return slot;
        var replacement = new GraphicsPipeline(pipelineLifetime,
            new(shader.GraphicsIdentity!, EngineFullscreenGeometry.Layout, targets, DynamicPipelineState.Viewport), shader);
        slot?.Dispose();
        return slot = replacement;
    }

    /// <summary>Uses complete default raster state and an explicit target-sized viewport for one fullscreen draw.</summary>
    private void SubmitFullscreen(GraphicsCommandContext commands, GraphicsPipeline pipeline,
        GpuFramebuffer target, RenderPassColor[] outputs)
    {
        commands.BeginPass(new(target, outputs));
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.Draw(geometry!, new(0, 6));
        commands.EndPass();
    }

    /// <summary>Retires only renderer-owned realizations; programs and target storage retain their original owners.</summary>
    private void DisposePipelines()
    {
        compositePipeline?.Dispose();
        receiverPipeline?.Dispose();
        capturePipeline?.Dispose();
        displayPipeline?.Dispose();
        reductionPipeline?.Dispose();
        pipelineLifetime.Dispose();
    }
    #endregion
}
