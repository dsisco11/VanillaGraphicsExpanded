using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

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
    private readonly LumOnBufferManager bufferManager;
    private readonly GBufferManager? gBufferManager;

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
        LumOnBufferManager bufferManager,
        GBufferManager? gBufferManager)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;

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
        if (config.DebugMode == 0 || !config.Enabled || quadMeshRef is null)
            return;

        // Ensure buffers are initialized
        if (!bufferManager.IsInitialized)
            return;

        var shader = ShaderRegistry.getProgramByName("lumon_debug") as LumOnDebugShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
            return;

        // Update matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invViewMatrix);
        UpdateCurrentViewProjMatrix();

        // Disable depth test for fullscreen overlay
        capi.Render.GLDepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        capi.Render.GlToggleBlend(false);

        shader.Use();

        // Bind textures
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex;
        shader.RadianceTexture0 = bufferManager.RadianceHistoryTex0;
        shader.IndirectHalf = bufferManager.IndirectHalfTex;
        shader.HistoryMeta = bufferManager.ProbeMetaHistoryTex;

        // Pass uniforms
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DebugMode = config.DebugMode;
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvViewMatrix = invViewMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;
        shader.TemporalAlpha = config.TemporalAlpha;
        shader.DepthRejectThreshold = config.DepthRejectThreshold;
        shader.NormalRejectThreshold = config.NormalRejectThreshold;

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);

        shader.Stop();

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);

        // Store current matrix for next frame's reprojection
        StorePrevViewProjMatrix();
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
