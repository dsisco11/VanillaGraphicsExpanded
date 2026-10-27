using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    private static readonly RenderTargetSignature CompositeTargets = new([new(TextureFormat.R11fG11fB10f), new(null, true), new(null, true)]);
    private static readonly RenderTargetSignature ReceiverTargets = new([new(TextureFormat.R11fG11fB10f), new(TextureFormat.Rgba16f), new(TextureFormat.R32f)]);
    private static readonly RenderTargetSignature CaptureTargets = new([new(null), new(TextureFormat.Rgba16f), new(TextureFormat.R32f)]);
    private static readonly RenderTargetSignature ReductionTargets = new([new(TextureFormat.Rgba16f), new(TextureFormat.Rgba32f)]);
    private GraphicsPipelineAsset? compositePipeline, receiverPipeline, capturePipeline, displayPipeline, reductionPipeline;

    #region Internal API
    /// <summary>Prepares all possible draw destinations before resolving the boundary's complete resource footprint.</summary>
    internal RHIGraphicsPipeline[]? PrepareBoundaryPipelines(bool capture)
    {
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        var shader = PrepareCompositeProgram(!capture && readLightingMode(), capture);
        if (shader is null) return null;
        if (capture) return [PreparePipeline(ref capturePipeline, shader, CaptureTargets)];

        // Both receiver allocation success and its optional fallback must be declared before entry.
        var pipelines = new List<RHIGraphicsPipeline>
        {
            PreparePipeline(ref compositePipeline, shader, CompositeTargets),
            PreparePipeline(ref receiverPipeline, shader, ReceiverTargets)
        };
        var display = GpuShaderProgramLibraries.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        if (display?.EnsureReady() != true) return null;
        var primaryColor = gBufferManager.PrimaryFramebuffer.GetAttachment(FramebufferSlot.Color0)
            ?? throw new InvalidOperationException("Primary color metadata is unavailable.");
        pipelines.Add(PreparePipeline(ref displayPipeline, display,
            new([new(primaryColor.Format)], samples: primaryColor.Samples)));
        var ssao = SceneColor.SceneColorParticleCapture.PrepareSsaoPipeline(capi);
        if (ssao is not null) pipelines.Add(ssao);
        if (ConfigModSystem.Config.WaterRefractionEnabled && ConfigModSystem.Config.WaterRefractionBackgroundScale == 1)
        {
            var reduction = GpuShaderProgramLibraries.Get<Liquids.WaterRefractionReductionShaderProgram>(capi, "water_refraction_reduce");
            if (reduction?.EnsureReady() == true)
                pipelines.Add(PreparePipeline(ref reductionPipeline, reduction, ReductionTargets));
        }
        return pipelines.ToArray();
    }
    #endregion

    #region Private
    /// <summary>Replaces one executable realization only after its complete replacement validates.</summary>
    private RHIGraphicsPipeline PreparePipeline(ref GraphicsPipelineAsset? slot, GpuProgram shader, RenderTargetSignature targets)
    {
        if (slot is not null && slot.Description.Shader == shader.Shader && slot.Description.Targets == targets) return slot.Pipeline;
        var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, EngineFullscreenGeometry.Layout, targets, DynamicPipelineState.Viewport));
        slot?.Dispose();
        slot = replacement;
        return replacement.Pipeline;
    }

    /// <summary>Uses complete default raster state and an explicit target-sized viewport for one fullscreen draw.</summary>
    private void SubmitFullscreen(RHICommandContext commands, RHIGraphicsPipeline pipeline,
        RHIRenderPassDescription pass,RHIResourceBindings inputs)
    {
        commands.BeginRenderPass(pass);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.BindResources(inputs);
        commands.DrawIndexed(geometry!.Inputs,new(0,6));
        commands.EndRenderPass();
    }

    /// <summary>Retires only renderer-owned realizations; programs and target storage retain their original owners.</summary>
    private void DisposePipelines()
    {
        compositePipeline?.Dispose();
        receiverPipeline?.Dispose();
        capturePipeline?.Dispose();
        displayPipeline?.Dispose();
        reductionPipeline?.Dispose();
    }
    #endregion
}
