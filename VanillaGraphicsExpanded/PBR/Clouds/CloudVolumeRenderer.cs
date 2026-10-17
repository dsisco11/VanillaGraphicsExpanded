using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
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

/// <summary>Draws reconstructed or reference transport at the native volume order; never advances native weather.</summary>
internal sealed class CloudVolumeRenderer : IRenderer
{
    internal static readonly VertexLayoutDesc Layout = new([]);
    private readonly ICoreClientAPI api;
    private readonly CloudNativeRuntime runtime;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Action unregisterResize;
    private readonly CloudVolumeReconstruction reconstruction;
    private readonly CloudHierarchy cloudHierarchy;
    private bool reference;
    private ArrayGraphicsGeometry? geometry;
    private GpuFramebuffer? target;
    private GraphicsPipeline? pipeline;
    private CloudVolumeProgram? program;
    private bool failed, disposed;

    #region Public API
    #region Preparation and admission
    /// <summary>Retains the exact bounded quality admitted before the scene lighting scale is frozen.</summary>
    internal CloudVolumeSettings Settings { get; private set; } = CloudVolumeSettings.Default;
    /// <summary>Consumes native generation at .30 before the later Simple callback.</summary>
    public double RenderOrder => .31;
    /// <summary>Clouds are independent of visible terrain chunks.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Uses the existing resize lifecycle; registration is transferred only after preparation succeeds.</summary>
    internal CloudVolumeRenderer(ICoreClientAPI api, CloudNativeRuntime runtime)
    {
        this.api = api; this.runtime = runtime;
        reconstruction = new(api);
        cloudHierarchy = new(() => CloudHierarchyComputeShader.Create(api));
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
    }
    /// <summary>Admits the dedicated executable and procedural geometry before visible ownership.</summary>
    internal bool Prepare()
    {
        if (disposed || failed || AtmosphereModSystem.Lighting is null || !GpuUniformRingSystem.TryGetCurrent(out _)) return false;
        Settings = ConfigModSystem.Config.Clouds.Capture();
        reference = ConfigModSystem.Config.Clouds.FullResolutionReference;
        program = GpuShaderProgramLibraries.Get<CloudVolumeProgram>(api, "cloud_volume");
        if (program?.EnsureReady() != true || (!reference && !reconstruction.Prepare())) return false;
        geometry ??= new(Layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        return true;
    }
    #endregion
    #region Submission
    /// <summary>Samples only the certified same-frame map and the main view's explicit depth/target contract.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        runtime.Update();
        if (disposed || failed || stage != EnumRenderStage.OIT || !runtime.Ownership.Owns(CloudMode.Volumetric)
            || program is null || geometry is null || !runtime.TryGetMap(out var input)) { cloudHierarchy.Reset(); reconstruction.Invalidate(); return; }
        cloudHierarchy.ObserveSource(input);
        var render = api.Render;
        var buffers = render.FrameBuffers;
        if (render.FrameWidth <= 0 || render.FrameHeight <= 0 || buffers.Count <= (int)EnumFrameBuffer.Transparent
            || buffers.Count <= (int)EnumFrameBuffer.Primary || buffers.Count <= (int)EnumFrameBuffer.LiquidDepth) { reconstruction.Invalidate(); return; }
        var transparent = buffers[(int)EnumFrameBuffer.Transparent];
        var primary = buffers[(int)EnumFrameBuffer.Primary]; var liquid = buffers[(int)EnumFrameBuffer.LiquidDepth];
        if (transparent is null || primary is null || liquid is null || primary.DepthTextureId == 0 || liquid.DepthTextureId == 0
            || render.CurrentRenderStage != EnumRenderStage.OIT || render.CurrentFrameBuffer?.FboId != transparent.FboId) { reconstruction.Invalidate(); return; }
        if (!DepthHierarchyRenderer.TryGetCurrent(VgeFrameRenderer.Current, render.FrameWidth, render.FrameHeight, out var hierarchy)
            || hierarchy is null) { reconstruction.Invalidate(); return; }
        var lighting = AtmosphereModSystem.Lighting;
        if (lighting is null || AtmosphereModSystem.AerialRadianceTexture is null || AtmosphereModSystem.AerialAttenuationTexture is null) { reconstruction.Invalidate(); return; }
        try
        {
            // Environmental response is already included in the immutable atmosphere publication.
            // No directional reflection consumer exists here: never substitute main-view depth for it.
            SceneLightingPublicationRenderer.RequireNativeInputs("cloud_volume");
            program.Parameters.Capture(api, input, lighting, Settings);
            program.Map = input.Storage.Map; program.SceneDepth = hierarchy.TextureId; program.VisibilityDepth = primary.DepthTextureId; program.LiquidDepth = liquid.DepthTextureId;
            program.AerialRadiance = AtmosphereModSystem.AerialRadianceTexture;
            program.AerialAttenuation = AtmosphereModSystem.AerialAttenuationTexture;
            program.LightShafts = Postprocessing.LightShaftOcclusionRenderer.Texture;
            if (target is null || target.FboId != transparent.FboId || target.Width != transparent.Width || target.Height != transparent.Height)
            {
                var replacement = GpuFramebuffer.Wrap(transparent.FboId, "Cloud.Volume.Transparent", transparent.Width, transparent.Height);
                try { replacement.PublishRenderPassMetadata(); }
                catch { replacement.Dispose(); throw; }
                target?.Dispose(); target = replacement;
            }
            if (!reference)
            {
                // This same-context boundary follows native map generation. Missing summaries retain
                // the existing DDA; hierarchical tracing is admitted separately by its consumer.
                var source = new CloudHierarchySource(input);
                cloudHierarchy.TryBuild(input, () => runtime.TryGetMap(out var current) && source.Matches(current));
                reconstruction.Draw(input, lighting, Settings, target, hierarchy.TextureId, primary.DepthTextureId, liquid.DepthTextureId, geometry,
                    () => runtime.TryGetMap(out var current) && current == input, cloudHierarchy);
                return;
            }
            cloudHierarchy.Reset();
            reconstruction.Invalidate();
            var pass = new RenderPassDesc(target, LiquidPipelineStates.SurfaceOutputs);
            using var metadata = new RenderPassTargets(pass);
            var description = new GraphicsPipelineDesc(program.GraphicsIdentity!, Layout, metadata.Signature,
                DynamicPipelineState.Viewport, blending: LiquidPipelineStates.SurfaceBlending);
            if (pipeline is null || pipeline.Description != description || !ReferenceEquals(pipeline.Shader, program)
                || pipeline.ExecutableRevision != program.ExecutableRevision)
            {
                var replacement = new GraphicsPipeline(lifetime, description, program); pipeline?.Dispose(); pipeline = replacement;
            }
            // Revalidate the borrow directly before synchronous submission, never retain it for another frame.
            if (!runtime.TryGetMap(out var current) || current != input) return;
            if (!GraphicsCommandContext.TryRun("Cloud.Volume.Reference", [pipeline], true, commands =>
            {
                commands.BeginPass(pass); commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(geometry, new(0, 3)); commands.EndPass();
            })) throw new InvalidOperationException("Volumetric cloud graphics boundary was rejected.");
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            reconstruction.Invalidate(); failed = true;
            api.Logger.Error("[VGE] Volumetric cloud drawing disabled until reload/reset; native drawing remains suppressed. {0}", error);
        }
    }
    #endregion
    #region Resource lifecycle
    /// <summary>Expires owned pipeline and borrowed framebuffer metadata without touching native texture storage.</summary>
    internal void Reset()
    {
        failed = false; program = null;
        RenderingCleanup.Run(cloudHierarchy.Reset, reconstruction.Reset, () => { pipeline?.Dispose(); pipeline = null; }, () => { target?.Dispose(); target = null; });
    }
    /// <summary>Withdraws the independent draw before retiring VGE-only resources.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        RenderingCleanup.Run(() => api.Event.UnregisterRenderer(this, EnumRenderStage.OIT), unregisterResize,
            Reset, cloudHierarchy.Dispose, reconstruction.Dispose, () => { geometry?.Dispose(); geometry = null; }, lifetime.Dispose);
    }
    #endregion
    #endregion
}

