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


    /// <summary>Combines scene-linear lighting, then resolves it into the engine's display target.</summary>
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
        using var framebufferBindings = GlStateCache.Current.BindFramebufferScope();

        // Need direct pass outputs.
        if (isolatedLighting is null && (directLightingBuffers.DirectDiffuseTex is null
            || directLightingBuffers.DirectSpecularTex is null
            || directLightingBuffers.EmissiveTex is null))
        {
            return;
        }

        var shader = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(capi, "pbr_composite");
        if (shader is null)
        {
            return;
        }

        // Ensure the scratch composite target exists and matches current resolution.
        // Use existing GBuffer/DynamicTexture resize support (no custom ensure helper).
        if (compositeFbo is null || compositeColorTex is null || !compositeFbo.IsValid || !compositeColorTex.IsValid)
        {
            ReleaseScreenResources();

            compositeColorTex = DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "PBRComposite");
            if (!compositeColorTex.IsValid)
            {
                ReleaseScreenResources();
                return;
            }

            compositeFbo = GpuFramebuffer.CreateSingle(compositeColorTex, depthTexture: null, ownsTextures: false, debugName: "PBRCompositeFBO");
            if (compositeFbo is null || !compositeFbo.IsValid)
            {
                ReleaseScreenResources();
                return;
            }
        }
        // Define-backed toggles must be set before Use() so the correct variant is bound.
        bool lumOnEnabled = capture is null && readLightingMode();
        var currentBuffers = lumOnEnabled ? getLumOnBuffers() : null;
        // Current-frame LumOn gather has not run at the hand boundary. Use the established environment fallback.
        var indirectTex = capture is null && currentBuffers?.HasPublishedIndirect == true ? currentBuffers.IndirectFullTex : null;
        if (indirectTex?.IsValid != true) indirectTex = null;

        shader.ConfigureOptions(() =>
        {
            shader.LumOnEnabled = lumOnEnabled;
            shader.EnablePbrComposite = lumOnEnabled && (lumOnConfig?.LumOn.EnablePbrComposite ?? true);
            shader.EnableShortRangeAo = lumOnEnabled && (lumOnConfig?.LumOn.EnableShortRangeAo ?? true);
        });

        var display = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        if (!shader.EnsureReady() || display is null || !display.EnsureReady()) return;

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Render into a scratch buffer to avoid sampling from the same texture we're writing to
        // (Primary ColorAttachment0 is also used as gBufferAlbedo / primaryScene input).
        using var fixedFunctionScope = GlStateCache.Current.CaptureLegacyFixedFunctionState(preserveViewport: true);
        GlStateCache.Current.InvalidateAll();
        GlStateCache.Current.Apply(CompositePipeline);
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
        shader.PrimaryDepth = primaryFb.DepthTextureId;

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

        // Resolve HDR deliberately before the RGBA8 primary and vanilla display grading.
        // The source is our scratch texture, so this draw has no color attachment feedback.
        // Borrow only primary color; this FBO owns its routing and never owns the engine texture.
        if (primaryResolveFbo is not { IsValid: true }
            || !ReferenceEquals(primaryResolveSource, primaryFb)
            || primaryResolveSourceId != primaryFb.FboId
            || primaryResolveColorId != primaryFb.ColorTextureIds[0])
        {
            // Rebuild only when the engine replaces the source or its borrowed attachment.
            ReleasePrimaryResolve();
            primaryResolveFbo = GpuFramebuffer.CreateEmpty("PBR.PrimaryResolve");
            primaryResolveFbo.Attach(primaryFb.ColorTextureIds[0]);
            primaryResolveSource = primaryFb;
            primaryResolveSourceId = primaryFb.FboId;
            primaryResolveColorId = primaryFb.ColorTextureIds[0];
        }
        primaryResolveFbo.Bind();

        display.PrimaryScene = compositeColorTex!.TextureId;
        display.PrimaryDepth = primaryFb.DepthTextureId;
        using (display.UseScope())
        using (GlGpuProfiler.Instance.Scope("PBR.DisplayResolve"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }
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
