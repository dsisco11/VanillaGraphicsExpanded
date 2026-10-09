using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Integration;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Final composite pass that merges direct radiance buffers (diffuse/specular/emissive)
/// with optional indirect lighting (LumOn) and applies fog once.
/// Writes the result into the primary framebuffer ColorAttachment0 so base-game
/// post-processing sees the combined result.
/// </summary>
public sealed partial class PBRCompositeRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 11.0;
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager directLightingBuffers;
    private readonly VgeConfig? lumOnConfig;
    private readonly Func<LumOnBufferManager?> getLumOnBuffers;
    private readonly Func<bool> readLightingMode;
    private readonly Action unregisterResize;

    private EngineFullscreenGeometry? geometry;

    /// <summary>Optional opaque snapshots consumed only after completed composition.</summary>
    internal Liquids.WaterRefractionScene RefractionScene { get; } = new();
    /// <summary>Frame-scoped clean world source captured before local hand projection begins.</summary>
    internal Liquids.WaterRefractionScene? PreOverlayScene { get; set; }

    private GpuFramebuffer? compositeFbo;
    private GpuFramebuffer? primaryResolveFbo;
    private FrameBufferRef? primaryResolveSource;
    private int primaryResolveSourceId;
    private int primaryResolveColorId;
    private DynamicTexture2D? compositeColorTex;

    /// <summary>Pre-display lighting retained by this renderer; sky pixels retain the engine's color convention.</summary>
    internal GpuTexture? SceneLinearColor => compositeColorTex;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] viewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    #region Public API
    #region Initialization
    /// <summary>Creates the fullscreen mesh and registers composition after direct and indirect lighting.</summary>
    public PBRCompositeRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager directLightingBuffers,
        VgeConfig? lumOnConfig,
        Func<LumOnBufferManager?> getLumOnBuffers,
        Func<bool>? readLightingMode = null)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.directLightingBuffers = directLightingBuffers;
        this.lumOnConfig = lumOnConfig;
        this.getLumOnBuffers = getLumOnBuffers;
        this.readLightingMode = readLightingMode ?? (() => lumOnConfig?.LumOn.Enabled == true);
        unregisterResize = ScreenResourceManager.Register(
            ScreenResourceManager.CompositeOrder,
            OnScreenResized);

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        geometry = EngineFullscreenGeometry.Upload(capi.Render, quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_composite");
        capi.Event.LeaveWorld += ReleaseScreenResources;

        capi.Logger.Notification("[VGE] PBRCompositeRenderer registered (Opaque @ 11.0)");
    }


    /// <summary>Prepares composition resources before a caller selects the frame's scene color convention.</summary>
    internal bool PrepareFrame()
    {
        if (geometry is null || capi.Render.FrameWidth <= 0 || capi.Render.FrameHeight <= 0) return false;
        var buffers = capi.Render.FrameBuffers;
        if (buffers.Count <= (int)EnumFrameBuffer.Primary
            || buffers[(int)EnumFrameBuffer.Primary] is not { } primary
            || primary.ColorTextureIds is not { Length: > 0 } colors || colors[0] == 0
            || primary.DepthTextureId == 0 || primary.Width <= 0 || primary.Height <= 0) return false;

        // Preparation may allocate and bind FBOs, but never publishes color or changes
        // the frame convention. The frame owner must also prepare the other producers.
        using var bindings = StateCache.Current.BindFramebufferScope();
        if (!PrepareTargets(primary)) return false;
        var display = GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        // The pre-overlay world capture uses the environment fallback even when
        // ordinary composition consumes LumOn, so both variants must be available.
        if (PrepareCompositeProgram(false, preOverlay: true) is null) return false;
        return PrepareCompositeProgram(readLightingMode(), preOverlay: false) is not null && display?.EnsureReady() == true;
    }

    #endregion
    #region Rendering
    /// <summary>Combines lighting and hands it to primary using the prepared frame's color convention.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        => RenderComposite(stage);

    /// <summary>Uses the existing composition algorithm for either the primary resolve or an isolated world publication.</summary>
    internal void RenderComposite(EnumRenderStage stage, Liquids.WaterRefractionScene? capture = null,
        DirectLightingTargets? isolatedLighting = null)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        var publication = capture ?? RefractionScene;
        publication.Invalidate();
        bool disabled = !ConfigModSystem.Config.WaterRefractionEnabled;
        bool canRender = stage == EnumRenderStage.Opaque && geometry is not null
            && capi.Render.FrameWidth > 0 && capi.Render.FrameHeight > 0;
        try
        {
            var pipelines = canRender ? PrepareBoundaryPipelines(capture is not null) : null;
            if (pipelines is null)
            {
                if (disabled) RefractionScene.Dispose();
                return;
            }
            if (!GraphicsCommandContext.TryRun("PBR.Composite", pipelines, true, commands =>
                {
                    // Option retirement is also part of the handoff, even when no shader can draw.
                    if (disabled) RefractionScene.Dispose();
                    RenderCompositeWithinBoundary(commands, stage, capture, isolatedLighting);
                }))
                throw new InvalidOperationException("Composite engine boundary unavailable.");
        }
        catch { publication.Invalidate(); throw; }
    }

    /// <summary>Executes composition and its helper passes under an established engine boundary.</summary>
    internal void RenderCompositeWithinBoundary(GraphicsCommandContext commands, EnumRenderStage stage,
        Liquids.WaterRefractionScene? capture = null, DirectLightingTargets? isolatedLighting = null)
    {
        var publication = capture ?? RefractionScene;
        var waterSettings = ConfigModSystem.Config;
        bool refractionEnabled = waterSettings.WaterRefractionEnabled;
        // Persisted resolution increases with quality; the receiver owner consumes a size divisor.
        int backgroundScale = capture is null ? (waterSettings.WaterRefractionBackgroundScale == 1 ? 2 : 1) : 1;
        publication.Invalidate();
        if (!refractionEnabled) RefractionScene.Dispose();
        if (stage != EnumRenderStage.Opaque || geometry is null)
        {
            return;
        }

        int screenW = capi.Render.FrameWidth;
        int screenH = capi.Render.FrameHeight;
        if (screenW <= 0 || screenH <= 0)
        {
            return;
        }

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return;
        }

        // Allocation and early exits remain inside the caller's restoration boundary.

        // Need direct pass outputs.
        if (isolatedLighting is null && directLightingBuffers.Radiance is null)
        {
            return;
        }

        // The clean receiver capture has no ordinary color consumer or display resolve.
        // Its two-image target is independent of the later scene-color scratch allocation.
        if (capture is null && !PrepareTargets(primaryFb)) return;
        if (capture is not null && !refractionEnabled) return;
        // Define-backed toggles must be set before Use() so the correct variant is bound.
        bool lumOnEnabled = capture is null && readLightingMode();
        var currentBuffers = lumOnEnabled ? getLumOnBuffers() : null;
        // Current-frame LumOn gather has not run at the hand boundary. Use the established environment fallback.
        var indirectTex = capture is null && currentBuffers?.HasPublishedIndirect == true ? currentBuffers.IndirectFullTex : null;
        if (indirectTex?.IsValid != true) indirectTex = null;

        var shader = GpuShaderPrograms.Get<PBRCompositeShaderProgram>(capi,
            capture is not null ? PBRCompositeShaderProgram.PreOverlayPassName : "pbr_composite");
        var display = capture is null ? GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve") : null;
        if (shader is null || shader.RequiresPreparation || shader.IsRetired
            || (capture is null && (display is null || display.RequiresPreparation || display.IsRetired))) return;

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Render into a scratch buffer to avoid sampling from the same texture we're writing to
        // (Primary ColorAttachment0 is also used as gBufferAlbedo / primaryScene input).
        GpuFramebuffer? refractionTarget = null;
        try
        {
            refractionTarget = capture is null
                ? publication.BeginFrame(refractionEnabled, compositeColorTex, backgroundScale)
                : publication.BeginCapture(screenW, screenH);
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error)) { capi.Logger.Warning("[VGE] Water refraction source unavailable: {0}", error.Message); }
        // A failed optional capture must not draw its receiver-only shader into ordinary color.
        if (capture is not null && refractionTarget is null) return;
        shader.RefractionSourceEnabled = refractionTarget is not null;
        var cleanSource = capture is null && PreOverlayScene?.Published == true ? PreOverlayScene : null;
        shader.PreOverlayColor = cleanSource?.Color;
        shader.PreOverlayDepth = cleanSource?.Depth;

        // Direct lighting radiance buffers (linear, fog-free)
        shader.DirectLighting = isolatedLighting?.Radiance ?? directLightingBuffers.Radiance;

        if (lumOnEnabled) shader.IndirectDiffuse = indirectTex;
        shader.GBufferSurface = gBufferManager.SurfaceTexture;
        shader.SetAmbientOcclusion(Postprocessing.AmbientOcclusionRenderer.Texture);

        // GBuffer inputs
        shader.GBufferAlbedo = primaryFb.ColorTextureIds[0];
        shader.GBufferPosition = gBufferManager.PositionTextureId;
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, primaryFb.DepthTextureId);

        // Only ordinary color consumes atmospheric, fog and signed-volume transport.
        if (capture is null)
        {
            shader.RgbaFogIn = capi.Render.FogColor;
            shader.FogDensityIn = capi.Render.FogDensity;
            shader.FogMinIn = capi.Render.FogMin;
            shader.SetAtmosphere(AtmosphereModSystem.Lighting);
            shader.SetUnderwater(capi.Render.ShaderUniforms.CameraUnderwater > .7f);
            shader.SetWaterVolume(Liquids.WaterVolumeRenderer.TryGetFrame(capi, out var waterFrame) ? waterFrame : null);
        }

        // The published LumOn gather output already includes intensity and tint.
        // Composition applies receiver material response without scaling that signal twice.
        shader.IndirectIntensity = indirectTex is not null ? 1.0f : 0.0f;
        shader.IndirectTint = new Vec3f(1, 1, 1);

        shader.DiffuseAOStrength = Math.Clamp(lumOnConfig?.LumOn.DiffuseAOStrength ?? 1.0f, 0f, 1f);
        shader.SpecularAOStrength = Math.Clamp(lumOnConfig?.LumOn.SpecularAOStrength ?? 1.0f, 0f, 1f);

        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = viewMatrix;
        shader.PreOverlaySourceEnabled = cleanSource is not null;

        using var cpuScope = Profiler.BeginScope("PBR.Composite", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.Composite"))
        {
            SubmitFullscreen(commands, capture is not null ? capturePipeline!
                : refractionTarget is not null ? receiverPipeline! : compositePipeline!,
                refractionTarget ?? compositeFbo!,
                capture is not null ? CaptureOutputs : refractionTarget is not null ? ReceiverOutputs : CompositeOutputs);
        }

        if (capture is not null)
        {
            if (refractionTarget is not null) publication.Publish();
            return;
        }

        // Preserve HDR through the primary handoff; final composition owns display conversion.
        // The source is our scratch texture, so this draw has no color attachment feedback.
        // Borrow only primary color; this FBO owns its routing and never owns the engine texture.

        display!.PrimaryScene = compositeColorTex!.TextureId;
        display.PrimaryDepth = primaryFb.DepthTextureId;
        // Preserve radiance until the engine final display boundary on a prepared HDR frame.
        display.SceneLinear = 1;
        var particleLayer = SceneColor.SceneColorParticleCapture.Layer(capi);
        display.ParticleLayer = particleLayer;
        display.ParticleLayerEnabled = particleLayer is null ? 0 : 1;
        using (GlGpuProfiler.Instance.Scope("PBR.DisplayResolve"))
        {
            SubmitFullscreen(commands, displayPipeline!, primaryResolveFbo!, SingleOutput);
        }
        // Deferred receivers are no longer needed this frame. Publish the original
        // particle normal/position values before engine SSAO consumes its attachments.
        SceneColor.SceneColorParticleCapture.RestoreSsao(capi, commands);
        Liquids.WaterVolumeRenderer.MarkComposed(capi);
        if (refractionTarget is not null) PublishRefractionScene(commands);
    }

    #endregion
    #region Lifetime
    /// <summary>Releases owned fullscreen resources and unregisters the renderer.</summary>
    public void Dispose()
    {
        capi.Event.LeaveWorld -= ReleaseScreenResources;
        ReleaseScreenResources();
        unregisterResize();
        if (geometry is not null)
        {
            geometry.Dispose();
            geometry = null;
        }

        DisposePipelines();
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }

    #endregion

    #endregion

    #region Private
    #region Publication
    /// <summary>Publishes restored receivers only after an optional geometry-aware reduction completes.</summary>
    private void PublishRefractionScene(GraphicsCommandContext commands)
    {
        // Reduction consumes the final restored pair, after the ordinary scene handoff.
        // Optional publication failure retains ordinary straight-through liquid rendering.
        try
        {
            if (RefractionScene.BackgroundScale == 1) RefractionScene.Publish();
            else
            {
                var reduction = GpuShaderPrograms.Get<Liquids.WaterRefractionReductionShaderProgram>(capi, "water_refraction_reduce");
                if (reduction is { RequiresPreparation: false, IsRetired: false })
                    RefractionScene.Publish((target, color, depth) =>
                    {
                        reduction.SourceColor = color;
                        reduction.SourceDepth = depth;
                        using (GlGpuProfiler.Instance.Scope("PBR.WaterReceiverReduction"))
                            SubmitFullscreen(commands, reductionPipeline!, target, PairOutputs);
                    });
            }
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            RefractionScene.Invalidate();
            capi.Logger.Warning("[VGE] Water receiver reduction unavailable: {0}", error.Message);
        }
    }

    #endregion
    #region Preparation
    /// <summary>Prepares the selected retained owner, adopting ordinary lighting changes before readiness.</summary>
    private PBRCompositeShaderProgram? PrepareCompositeProgram(bool lumOnEnabled, bool preOverlay)
    {
        var shader = GpuShaderPrograms.Get<PBRCompositeShaderProgram>(capi,
            preOverlay ? PBRCompositeShaderProgram.PreOverlayPassName : "pbr_composite");
        if (shader is null) return null;
        // Capture's fixed options belong to its declaration. Ordinary composition adopts
        // the engine generation's lighting mode without changing the capture executable.
        // Compare requested options so pending edits and failed preparation remain retryable.
        bool pbrComposite = lumOnEnabled && (lumOnConfig?.LumOn.EnablePbrComposite ?? true);
        bool shortRangeAo = lumOnEnabled && (lumOnConfig?.LumOn.EnableShortRangeAo ?? true);
        if (!preOverlay && (shader.LumOnEnabled != lumOnEnabled
            || shader.EnablePbrComposite != pbrComposite || shader.EnableShortRangeAo != shortRangeAo))
        {
            shader.ConfigureOptions(() =>
            {
                shader.LumOnEnabled = lumOnEnabled;
                shader.EnablePbrComposite = pbrComposite;
                shader.EnableShortRangeAo = shortRangeAo;
            });
        }
        return shader.EnsureReady() ? shader : null;
    }

    /// <summary>Prepares separate composition and borrowed primary destinations without drawing or publishing.</summary>
    private bool PrepareTargets(FrameBufferRef primary)
    {
        if (compositeFbo is not { IsValid: true } || compositeColorTex is not { IsValid: true })
        {
            ReleaseCompositeTargets();
            compositeColorTex = DynamicTexture2D.Create(primary.Width, primary.Height,
                PixelInternalFormat.Rgba16f, debugName: "PBRComposite");
            compositeFbo = GpuFramebuffer.CreateSingle(compositeColorTex, depthTexture: null, debugName: "PBRCompositeFBO");
            if (!compositeColorTex.IsValid || compositeFbo is not { IsValid: true })
            {
                ReleaseCompositeTargets();
                return false;
            }
        }
        if (compositeFbo.Width != primary.Width || compositeFbo.Height != primary.Height)
        {
            // Withdraw borrowers before resizing the owned attachment they reference.
            RefractionScene.Dispose();
            compositeFbo.Resize(primary.Width, primary.Height);
        }
        if (primaryResolveFbo is not { IsValid: true }
            || !ReferenceEquals(primaryResolveSource, primary)
            || primaryResolveSourceId != primary.FboId
            || primaryResolveColorId != primary.ColorTextureIds[0])
        {
            ReleasePrimaryResolve();
            primaryResolveFbo = GpuFramebuffer.CreateEmpty("PBR.PrimaryResolve");
            primaryResolveFbo.Attach(primary.ColorTextureIds[0]);
            primaryResolveSource = primary;
            primaryResolveSourceId = primary.FboId;
            primaryResolveColorId = primary.ColorTextureIds[0];
        }
        return primaryResolveFbo.IsValid;
    }

    #endregion
    #region Lifetime
    /// <summary>Retires snapshots before resizing their borrowed composite attachment.</summary>
    private void OnScreenResized()
    {
        ReleasePrimaryResolve();
        RefractionScene.Dispose();
        PreOverlayScene?.Invalidate();
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is not null && compositeFbo is { IsValid: true })
        {
            compositeFbo.Resize(primaryFb.Width, primaryFb.Height);
        }
    }

    /// <summary>Withdraws borrowed publications before releasing this renderer's owned screen resources.</summary>
    private void ReleaseScreenResources()
    {
        PreOverlayScene?.Invalidate();
        ReleaseCompositeTargets();
    }

    /// <summary>Retires ordinary composition targets without withdrawing an independent current-frame capture.</summary>
    private void ReleaseCompositeTargets()
    {
        RefractionScene.Dispose();
        compositeFbo?.Dispose();
        ReleasePrimaryResolve();
        compositeFbo = null;
        compositeColorTex?.Dispose();
        compositeColorTex = null;
    }

    /// <summary>Retires the borrowed primary attachment and its cached source identity together.</summary>
    private void ReleasePrimaryResolve()
    {
        primaryResolveFbo?.Dispose();
        primaryResolveFbo = null;
        primaryResolveSource = null;
        primaryResolveSourceId = 0;
        primaryResolveColorId = 0;
    }
    #endregion
    #endregion
}
