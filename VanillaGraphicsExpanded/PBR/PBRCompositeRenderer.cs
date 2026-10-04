using System;

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
public sealed class PBRCompositeRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 11.0;
    private const int RenderRangeValue = 1;

    // Neither fullscreen draw may test or overwrite the primary terrain depth.
    private static readonly GlPipelineDesc CompositePipeline = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.BlendEnable)
            .With(GlPipelineStateId.CullFaceEnable)
            .With(GlPipelineStateId.ScissorTestEnable)
            .With(GlPipelineStateId.ColorMask),
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask),
        depthWriteMask: false,
        name: "PBR.CompositeAndDisplay");

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager directLightingBuffers;
    private readonly VgeConfig? lumOnConfig;
    private readonly Func<LumOnBufferManager?> getLumOnBuffers;
    private readonly Func<bool> readLightingMode;
    private readonly Action unregisterResize;

    private MeshRef? quadMeshRef;

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
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_composite");
        capi.Event.LeaveWorld += ReleaseScreenResources;

        capi.Logger.Notification("[VGE] PBRCompositeRenderer registered (Opaque @ 11.0)");
    }


    /// <summary>Prepares composition resources before a caller selects the frame's scene color convention.</summary>
    internal bool PrepareFrame()
    {
        if (quadMeshRef is null || capi.Render.FrameWidth <= 0 || capi.Render.FrameHeight <= 0) return false;
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
        if (PrepareCompositeProgram(false) is null) return false;
        return PrepareCompositeProgram(readLightingMode()) is not null && display?.EnsureReady() == true;
    }

    /// <summary>Combines lighting and hands it to primary using the prepared frame's color convention.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        => RenderComposite(stage);

    /// <summary>Uses the existing composition algorithm for either the primary resolve or an isolated world publication.</summary>
    internal void RenderComposite(EnumRenderStage stage, Liquids.WaterRefractionScene? capture = null,
        DirectLightingTargets? isolatedLighting = null)
    {
        var publication = capture ?? RefractionScene;
        publication.Invalidate();
        if (!ConfigModSystem.Config.WaterRefractionEnabled) RefractionScene.Dispose();
        if (stage != EnumRenderStage.Opaque || quadMeshRef is null)
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

        // Allocation, preparation and early exits share the same owner-backed restoration boundary.
        using var framebufferBindings = StateCache.Current.BindFramebufferScope();

        // Need direct pass outputs.
        if (isolatedLighting is null && (directLightingBuffers.DirectDiffuseTex is null
            || directLightingBuffers.DirectSpecularTex is null
            || directLightingBuffers.EmissiveTex is null))
        {
            return;
        }

        if (!PrepareTargets(primaryFb)) return;
        // Define-backed toggles must be set before Use() so the correct variant is bound.
        bool lumOnEnabled = capture is null && readLightingMode();
        var currentBuffers = lumOnEnabled ? getLumOnBuffers() : null;
        // Current-frame LumOn gather has not run at the hand boundary. Use the established environment fallback.
        var indirectTex = capture is null && currentBuffers?.HasPublishedIndirect == true ? currentBuffers.IndirectFullTex : null;
        if (indirectTex?.IsValid != true) indirectTex = null;

        var shader = PrepareCompositeProgram(lumOnEnabled);
        var display = GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        if (shader is null || display is null || !display.EnsureReady()) return;

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Render into a scratch buffer to avoid sampling from the same texture we're writing to
        // (Primary ColorAttachment0 is also used as gBufferAlbedo / primaryScene input).
        using var fixedFunctionScope = StateCache.Current.CaptureLegacyFixedFunctionState(preserveViewport: true);
        StateCache.Current.InvalidateAll();
        StateCache.Current.Apply(CompositePipeline);
        GpuFramebuffer? refractionTarget = null;
        try { refractionTarget = publication.BeginFrame(ConfigModSystem.Config.WaterRefractionEnabled, compositeColorTex); }
        catch (Exception error) { capi.Logger.Warning("[VGE] Water refraction source unavailable: {0}", error.Message); }
        (refractionTarget ?? compositeFbo!).BindWithViewport();
        shader.RefractionSourceEnabled = refractionTarget is not null;
        var cleanSource = capture is null && PreOverlayScene?.Published == true ? PreOverlayScene : null;
        shader.PreOverlayColor = cleanSource?.Color;
        shader.PreOverlayDepth = cleanSource?.Depth;



        // Direct lighting radiance buffers (linear, fog-free)
        shader.DirectDiffuse = isolatedLighting?.DirectDiffuse ?? directLightingBuffers.DirectDiffuseTex;
        shader.DirectSpecular = isolatedLighting?.DirectSpecular ?? directLightingBuffers.DirectSpecularTex;
        shader.Emissive = isolatedLighting?.Emissive ?? directLightingBuffers.EmissiveTex;

        if (lumOnEnabled) shader.IndirectDiffuse = indirectTex;
        shader.GBufferEnvironment = gBufferManager.EnvironmentTexture;

        // GBuffer inputs
        shader.GBufferAlbedo = primaryFb.ColorTextureIds[0];
        shader.GBufferMaterial = gBufferManager.MaterialTexture;
        shader.GBufferNormal = gBufferManager.NormalTexture;
        shader.GBufferPosition = gBufferManager.PositionTextureId;
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, primaryFb.DepthTextureId);

        // Fog uniforms
        shader.RgbaFogIn = capi.Render.FogColor;
        shader.FogDensityIn = capi.Render.FogDensity;
        shader.FogMinIn = capi.Render.FogMin;
        shader.SetAtmosphere(AtmosphereModSystem.Lighting);
        shader.SetUnderwater(capi.Render.ShaderUniforms.CameraUnderwater > .7f);
        shader.SetWaterVolume(Liquids.WaterVolumeRenderer.TryGetFrame(capi, out var waterFrame) ? waterFrame : null);

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
        using (shader.UseScope())
        using (GlGpuProfiler.Instance.Scope("PBR.Composite"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        if (capture is not null)
        {
            if (refractionTarget is not null) publication.Publish();
            return;
        }

        // Preserve HDR for a prepared linear frame; otherwise retain the legacy display resolve.
        // The source is our scratch texture, so this draw has no color attachment feedback.
        // Borrow only primary color; this FBO owns its routing and never owns the engine texture.
        primaryResolveFbo!.Bind();

        display.PrimaryScene = compositeColorTex!.TextureId;
        display.PrimaryDepth = primaryFb.DepthTextureId;
        // Runtime scene output remains display-referred until its existing binding owners adopt HDR.
        display.SceneLinear = 0;
        var particleLayer = SceneColor.SceneColorParticleCapture.Layer(capi);
        display.ParticleLayer = particleLayer;
        display.ParticleLayerEnabled = particleLayer is null ? 0 : 1;
        using (display.UseScope())
        using (GlGpuProfiler.Instance.Scope("PBR.DisplayResolve"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }
        // Deferred receivers are no longer needed this frame. Publish the original
        // particle normal/position values before engine SSAO consumes its attachments.
        SceneColor.SceneColorParticleCapture.RestoreSsao(capi);
        Liquids.WaterVolumeRenderer.MarkComposed(capi);
        if (refractionTarget is not null) RefractionScene.Publish();
    }

    /// <summary>Releases owned fullscreen resources and unregisters the renderer.</summary>
    public void Dispose()
    {
        capi.Event.LeaveWorld -= ReleaseScreenResources;
        ReleaseScreenResources();
        unregisterResize();
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }

    #endregion

    #region Private
    /// <summary>Prepares the same shader options for early readiness checks and actual composition.</summary>
    private PBRCompositeShaderProgram? PrepareCompositeProgram(bool lumOnEnabled)
    {
        var shader = GpuShaderPrograms.Get<PBRCompositeShaderProgram>(capi, "pbr_composite");
        if (shader is null) return null;
        shader.ConfigureOptions(() =>
        {
            shader.LumOnEnabled = lumOnEnabled;
            shader.EnablePbrComposite = lumOnEnabled && (lumOnConfig?.LumOn.EnablePbrComposite ?? true);
            shader.EnableShortRangeAo = lumOnEnabled && (lumOnConfig?.LumOn.EnableShortRangeAo ?? true);
        });
        return shader.EnsureReady() ? shader : null;
    }

    /// <summary>Prepares separate composition and borrowed primary destinations without drawing or publishing.</summary>
    private bool PrepareTargets(FrameBufferRef primary)
    {
        if (compositeFbo is not { IsValid: true } || compositeColorTex is not { IsValid: true })
        {
            ReleaseScreenResources();
            compositeColorTex = DynamicTexture2D.Create(primary.Width, primary.Height,
                PixelInternalFormat.Rgba16f, debugName: "PBRComposite");
            compositeFbo = GpuFramebuffer.CreateSingle(compositeColorTex, depthTexture: null, debugName: "PBRCompositeFBO");
            if (!compositeColorTex.IsValid || compositeFbo is not { IsValid: true })
            {
                ReleaseScreenResources();
                return false;
            }
        }
        if (compositeFbo.Width != primary.Width || compositeFbo.Height != primary.Height)
        {
            // Withdraw borrowers before resizing the owned attachment they reference.
            RefractionScene.Dispose();
            PreOverlayScene?.Invalidate();
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
}
