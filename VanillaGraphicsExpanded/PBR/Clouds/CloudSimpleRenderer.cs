using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns Simple pipeline resources while borrowing native geometry only during synchronous callback submission.</summary>
internal sealed class CloudSimpleRenderer : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Action unregisterResize;
    private GpuFramebuffer? target;
    private GraphicsPipeline? pipeline;
    private CloudSimpleProgram? program;
    private bool failed, disposed;

    #region Public API
    /// <summary>Uses the existing screen lifecycle to expire borrowed framebuffer metadata.</summary>
    internal CloudSimpleRenderer(ICoreClientAPI api)
    {
        this.api = api;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
    }

    /// <summary>Acquires the dedicated executable before visible ownership can be transferred.</summary>
    internal bool Prepare()
    {
        if (disposed || failed || AtmosphereModSystem.Lighting is null || !GpuUniformRingSystem.TryGetCurrent(out _)) return false;
        program = GpuShaderProgramLibraries.Get<CloudSimpleProgram>(api, "cloud_simple");
        return program?.EnsureReady() == true;
    }

    /// <summary>Draws the current post-tick mesh only into the engine Transparent target using a frozen light publication.</summary>
    internal void Draw(CloudSimpleInput input)
    {
        if (disposed || failed || program is null || api.Render.FrameHeight <= 0) return;
        var buffers = api.Render.FrameBuffers;
        if (buffers.Count <= (int)EnumFrameBuffer.LiquidDepth
            || buffers.Count <= (int)EnumFrameBuffer.Transparent) return;
        var transparent = buffers[(int)EnumFrameBuffer.Transparent];
        var liquid = buffers[(int)EnumFrameBuffer.LiquidDepth];
        if (transparent is null || liquid is null || liquid.DepthTextureId == 0
            || api.Render.CurrentRenderStage != EnumRenderStage.OIT
            || api.Render.CurrentFrameBuffer?.FboId != transparent.FboId
            || input.Frame != VgeFrameRenderer.Current.FrameIndex) return;
        var lighting = AtmosphereModSystem.Lighting;
        if (lighting is null || AtmosphereModSystem.AerialRadianceTexture is null || AtmosphereModSystem.AerialAttenuationTexture is null) return;
        try
        {
            // Admission checks the same immutable atmosphere as the Opaque freeze. Native
            // compatibility remains conservatively reserved for mid-frame ownership release.
            SceneLightingPublicationRenderer.RequireNativeInputs("cloud_simple");
            program.Parameters.Capture(api, input, lighting);
            program.LiquidDepth = liquid.DepthTextureId;
            program.AerialRadiance = AtmosphereModSystem.AerialRadianceTexture;
            program.AerialAttenuation = AtmosphereModSystem.AerialAttenuationTexture;
            program.LightShafts = Postprocessing.LightShaftOcclusionRenderer.Texture;
            if (target is null || target.FboId != transparent.FboId || target.Width != transparent.Width || target.Height != transparent.Height)
            {
                var replacement = GpuFramebuffer.Wrap(transparent.FboId, "Cloud.Simple.Transparent", transparent.Width, transparent.Height);
                try { replacement.PublishRenderPassMetadata(); }
                catch { replacement.Dispose(); throw; }
                target?.Dispose(); target = replacement;
            }
            using var geometry = new CloudSimpleGeometry(input.Mesh, input.Count);
            var pass = new RenderPassDesc(target, LiquidPipelineStates.SurfaceOutputs);
            using var metadata = new RenderPassTargets(pass);
            var description = new GraphicsPipelineDesc(program.GraphicsIdentity!, CloudSimpleGeometry.Layout, metadata.Signature,
                DynamicPipelineState.Viewport, depthStencil: LiquidPipelineStates.Surface,
                blending: LiquidPipelineStates.SurfaceBlending);
            if (pipeline is null || pipeline.Description != description || !ReferenceEquals(pipeline.Shader, program)
                || pipeline.ExecutableRevision != program.ExecutableRevision)
            {
                var replacement = new GraphicsPipeline(lifetime, description, program); pipeline?.Dispose(); pipeline = replacement;
            }
            if (!GraphicsCommandContext.TryRun("Cloud.Simple", [pipeline], true, commands =>
            {
                commands.BeginPass(pass); commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(geometry, new(0, 36, 0, input.Count)); commands.EndPass();
            })) throw new InvalidOperationException("Simple cloud graphics boundary was rejected.");
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true;
            api.Logger.Error("[VGE] Simple cloud drawing disabled until reload/reset; native drawing remains suppressed. {0}", error);
        }
    }

    /// <summary>Releases VGE pipeline state and borrowed target metadata without deleting native images or mesh storage.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        unregisterResize(); RenderingCleanup.Run(Reset, lifetime.Dispose);
    }

    /// <summary>Retries resources at existing reload/resize boundaries without relinquishing visible ownership.</summary>
    internal void Reset()
    {
        failed = false; program = null;
        RenderingCleanup.Run(() => { pipeline?.Dispose(); pipeline = null; }, () => { target?.Dispose(); target = null; });
    }
    #endregion
}
