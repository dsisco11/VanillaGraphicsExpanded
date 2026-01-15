using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Renders LumOn debug visualizations as a fullscreen overlay at the AfterBlit stage.
/// This ensures debug output is visible on top of all other rendering.
/// 
/// Debug Modes:
/// 0 = Off (no debug rendering)
/// 1 = Probe Grid (shows probe positions with validity coloring)
/// 2 = Probe Depth (shows probe anchor depth as heatmap)
/// 3 = Probe Normals (shows probe anchor normals)
/// 4 = Scene Depth (shows linearized depth buffer)
/// 5 = Scene Normals (shows G-buffer normals)
/// 6 = Temporal Weight (shows how much history is used per probe)
/// 7 = Temporal Rejection (shows why history was rejected: bounds/depth/normal)
/// 8 = SH Coefficients (shows DC + directional magnitude)
/// 9 = Interpolation Weights (shows probe blend weights per pixel)
/// 10 = Radiance Overlay (shows indirect diffuse buffer)
/// 11 = Gather Weight (diagnostic; grayscale weight, red fallback)
/// </summary>
public sealed class LumOnDebugRenderer : IRenderer, IDisposable
{
    #region Constants

    private const double DEBUG_RENDER_ORDER = 1.1; // After other debug overlays
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;
    private readonly LumOnBufferManager? bufferManager;
    private readonly GBufferManager? gBufferManager;
    private readonly DirectLightingBufferManager? directLightingBufferManager;

    private MeshRef? quadMeshRef;

    // Matrix buffers
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invViewMatrix = new float[16];
    private readonly float[] prevViewProjMatrix = new float[16];
    private readonly float[] currentViewProjMatrix = new float[16];
    private readonly float[] tempProjectionMatrix = new float[16];
    private readonly float[] tempModelViewMatrix = new float[16];

    #endregion

    #region Properties

    public double RenderOrder => DEBUG_RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public LumOnDebugRenderer(
        ICoreClientAPI capi,
        LumOnConfig config,
        LumOnBufferManager? bufferManager,
        GBufferManager? gBufferManager,
        DirectLightingBufferManager? directLightingBufferManager)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;
        this.directLightingBufferManager = directLightingBufferManager;

        // Create fullscreen quad mesh (-1 to 1 in NDC)
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register at AfterBlit stage so debug output renders on top
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "lumon_debug");

        capi.Logger.Notification("[LumOn] Debug renderer initialized");
    }

    /// <summary>
    /// Updates the previous view-projection matrix for temporal debug visualization.
    /// Called automatically at the end of each frame.
    /// </summary>
    private void StorePrevViewProjMatrix()
    {
        Array.Copy(currentViewProjMatrix, prevViewProjMatrix, 16);
    }

    /// <summary>
    /// Computes the current view-projection matrix.
    /// </summary>
    private void UpdateCurrentViewProjMatrix()
    {
        Array.Copy(capi.Render.CurrentProjectionMatrix, tempProjectionMatrix, 16);
        Array.Copy(capi.Render.CameraMatrixOriginf, tempModelViewMatrix, 16);
        MatrixHelper.Multiply(tempProjectionMatrix, tempModelViewMatrix, currentViewProjMatrix);
    }

    #endregion

    #region IRenderer

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        // Only render when debug mode is active
        if (config.DebugMode == LumOnDebugMode.Off || quadMeshRef is null)
            return;

        // Most debug modes require the GBuffer to be ready.
        // Direct lighting debug modes do not.
        if (!IsDirectLightingMode(config.DebugMode))
        {
            if (gBufferManager is null || !gBufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight))
                return;
        }

        var shader = capi.Shader.GetProgramByName("lumon_debug") as LumOnDebugShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
            return;

        // Modes that rely on LumOn internals should not render unless LumOn is enabled.
        if (RequiresLumOnBuffers(config.DebugMode))
        {
            if (!config.Enabled || bufferManager is null || !bufferManager.IsInitialized)
                return;
        }

        // Phase 16 direct lighting debug modes rely on the direct lighting MRT outputs.
        if (IsDirectLightingMode(config.DebugMode))
        {
            if (directLightingBufferManager?.DirectDiffuseTex is null
                || directLightingBufferManager.DirectSpecularTex is null
                || directLightingBufferManager.EmissiveTex is null)
            {
                return;
            }
        }

        // Update matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invViewMatrix);
        UpdateCurrentViewProjMatrix();

        // Disable depth test for fullscreen overlay
        capi.Render.GLDepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        capi.Render.GlToggleBlend(false);

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        shader.EnablePbrComposite = config.EnablePbrComposite;
        shader.EnableAO = config.EnableAO;
        shader.EnableBentNormal = config.EnableBentNormal;

        shader.Use();

        // Bind textures
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        // Use VGE's G-buffer normal (ColorAttachment4) which contains world-space normals
        // encoded to [0,1] via the shader patching system
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;
        shader.ProbeAnchorPosition = bufferManager?.ProbeAnchorPositionTex?.TextureId ?? 0;
        shader.ProbeAnchorNormal = bufferManager?.ProbeAnchorNormalTex?.TextureId ?? 0;
        shader.RadianceTexture0 = bufferManager?.RadianceHistoryTex0?.TextureId ?? 0;
        shader.RadianceTexture1 = bufferManager?.RadianceHistoryTex1?.TextureId ?? 0;
        shader.IndirectHalf = bufferManager?.IndirectHalfTex?.TextureId ?? 0;
        shader.HistoryMeta = bufferManager?.ProbeMetaHistoryTex?.TextureId ?? 0;

        shader.ProbeAtlasMeta = bufferManager?.ScreenProbeAtlasMetaHistoryTex?.TextureId ?? 0;

        // Probe-atlas debug textures (raw/current/filtered + the actual gather input selection)
        int probeAtlasTrace = bufferManager?.ScreenProbeAtlasTraceTex?.TextureId ?? 0;
        int probeAtlasCurrent = bufferManager?.ScreenProbeAtlasCurrentTex?.TextureId ?? probeAtlasTrace;
        int probeAtlasFiltered = bufferManager?.ScreenProbeAtlasFilteredTex?.TextureId ?? 0;

        int gatherAtlasSource = 0;
        int gatherInput = probeAtlasTrace;
        if (probeAtlasFiltered != 0)
        {
            gatherAtlasSource = 2;
            gatherInput = probeAtlasFiltered;
        }
        else if (probeAtlasCurrent != 0)
        {
            gatherAtlasSource = 1;
            gatherInput = probeAtlasCurrent;
        }

        shader.ProbeAtlasCurrent = probeAtlasCurrent;
        shader.ProbeAtlasFiltered = probeAtlasFiltered;
        shader.ProbeAtlasGatherInput = gatherInput;
        shader.GatherAtlasSource = gatherAtlasSource;

        // Phase 15 composite debug inputs
        shader.IndirectDiffuseFull = bufferManager?.IndirectFullTex?.TextureId ?? 0;
        shader.GBufferAlbedo = bufferManager?.CapturedSceneTex?.TextureId ?? 0;
        shader.GBufferMaterial = gBufferManager?.MaterialTextureId ?? 0;

        // Phase 16 direct lighting debug inputs
        shader.DirectDiffuse = directLightingBufferManager?.DirectDiffuseTex?.TextureId ?? 0;
        shader.DirectSpecular = directLightingBufferManager?.DirectSpecularTex?.TextureId ?? 0;
        shader.Emissive = directLightingBufferManager?.EmissiveTex?.TextureId ?? 0;

        // Phase 14 velocity debug input
        shader.VelocityTex = bufferManager?.VelocityTex?.TextureId ?? 0;

        // Pass uniforms
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.ProbeGridSize = new Vec2i(bufferManager?.ProbeCountX ?? 0, bufferManager?.ProbeCountY ?? 0);
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DebugMode = (int)config.DebugMode;
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvViewMatrix = invViewMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;
        shader.TemporalAlpha = config.TemporalAlpha;
        shader.DepthRejectThreshold = config.DepthRejectThreshold;
        shader.NormalRejectThreshold = config.NormalRejectThreshold;
        shader.VelocityRejectThreshold = config.VelocityRejectThreshold;

        // Phase 15 composite params (now compile-time defines)
        shader.IndirectIntensity = config.Intensity;
        shader.IndirectTint = new Vec3f(config.IndirectTint[0], config.IndirectTint[1], config.IndirectTint[2]);
        shader.DiffuseAOStrength = Math.Clamp(config.DiffuseAOStrength, 0f, 1f);
        shader.SpecularAOStrength = Math.Clamp(config.SpecularAOStrength, 0f, 1f);

        // Render fullscreen quad
        using (GlGpuProfiler.Instance.Scope("Debug.LumOn"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        shader.Stop();

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);

        // Store current matrix for next frame's reprojection
        StorePrevViewProjMatrix();
    }

    private static bool IsDirectLightingMode(LumOnDebugMode mode) =>
        mode is LumOnDebugMode.DirectDiffuse
            or LumOnDebugMode.DirectSpecular
            or LumOnDebugMode.DirectEmissive
            or LumOnDebugMode.DirectTotal;

    private static bool RequiresLumOnBuffers(LumOnDebugMode mode)
    {
        // Anything involving probes/atlases/temporal/indirect assumes LumOn is enabled.
        return (mode is >= LumOnDebugMode.ProbeGrid and <= LumOnDebugMode.CompositeMaterial)
            || (mode is >= LumOnDebugMode.VelocityMagnitude and <= LumOnDebugMode.VelocityPrevUv);
    }

    #endregion

    #region Matrix Utilities

    // Matrix utilities moved to VanillaGraphicsExpanded.Rendering.MatrixHelper

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
    }

    #endregion
}
