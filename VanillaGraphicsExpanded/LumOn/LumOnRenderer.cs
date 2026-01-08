using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Main renderer orchestrating LumOn shader passes for Screen Probe Gather.
/// Implements the probe-based indirect diffuse lighting pipeline:
/// 1. Probe Anchor Pass - determine probe positions from G-buffer
/// 2. Probe Trace Pass - ray march and accumulate radiance into SH
/// 3. Temporal Pass - blend with history for stability
/// 4. Gather Pass - interpolate probes to pixels
/// 5. Upsample Pass - bilateral upsample to full resolution
/// </summary>
public class LumOnRenderer : IRenderer, IDisposable
{
    #region Constants

    /// <summary>
    /// Render order - must match legacy SSGI for feature toggle compatibility.
    /// </summary>
    private const double RENDER_ORDER = 0.5;
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;
    private readonly LumOnBufferManager bufferManager;
    private readonly GBufferManager? gBufferManager;

    // Fullscreen quad mesh
    private MeshRef? quadMeshRef;

    // Matrix buffers
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];
    private readonly float[] projectionMatrix = new float[16];
    private readonly float[] modelViewMatrix = new float[16];
    private readonly float[] prevViewProjMatrix = new float[16];
    private readonly float[] currentViewProjMatrix = new float[16];

    // Frame counter for ray jittering
    private int frameIndex;

    // First frame detection (no valid history)
    private bool isFirstFrame = true;

    // Teleport detection
    private double lastCameraX;
    private double lastCameraY;
    private double lastCameraZ;
    private const float TeleportThreshold = 50.0f;  // meters

    // Debug counters
    private readonly LumOnDebugCounters debugCounters = new();

    // GPU timer queries for performance profiling
    private readonly int[] timerQueries = new int[5];
    private bool timerQueriesInitialized;
    private bool timerQueryPending;

    #endregion

    #region Properties

    /// <summary>
    /// Whether LumOn rendering is enabled.
    /// </summary>
    public bool Enabled => config.Enabled;

    /// <summary>
    /// Debug counters for performance monitoring.
    /// </summary>
    public LumOnDebugCounters DebugCounters => debugCounters;

    #endregion

    #region IRenderer Implementation

    public double RenderOrder => RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public LumOnRenderer(
        ICoreClientAPI capi,
        LumOnConfig config,
        LumOnBufferManager bufferManager,
        GBufferManager? gBufferManager)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;

        // Create fullscreen quad mesh
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register renderer
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterPostProcessing, "lumon");

        // Initialize previous frame matrix to identity
        for (int i = 0; i < 16; i++)
        {
            prevViewProjMatrix[i] = (i % 5 == 0) ? 1.0f : 0.0f;
        }

        // Register debug hotkeys
        RegisterHotkeys();

        // Initialize GPU timer queries
        InitializeTimerQueries();

        // Register for world events (teleport, world change)
        RegisterWorldEvents();

        capi.Logger.Notification("[LumOn] Renderer initialized");
    }

    private void RegisterWorldEvents()
    {
        // Clear history when leaving world (prevents stale data on rejoin)
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    private void OnLeaveWorld()
    {
        isFirstFrame = true;
        bufferManager?.ClearHistory();
        capi.Logger.Debug("[LumOn] World left, cleared history");
    }

    private void InitializeTimerQueries()
    {
        GL.GenQueries(timerQueries.Length, timerQueries);
        timerQueriesInitialized = true;
    }

    private void RegisterHotkeys()
    {
        // Register hotkey for LumOn toggle (F9)
        capi.Input.RegisterHotKey(
            "vgelumon",
            "VGE Toggle LumOn",
            GlKeys.F9,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgelumon", OnToggleLumOn);

        // Register hotkey for LumOn debug mode cycling (Shift+F9)
        capi.Input.RegisterHotKey(
            "vgelumondebug",
            "VGE Cycle LumOn Debug Mode",
            GlKeys.F9,
            HotkeyType.DevTool,
            shiftPressed: true);
        capi.Input.SetHotKeyHandler("vgelumondebug", OnCycleDebugMode);

        // Register hotkey for LumOn stats display (Ctrl+F9)
        capi.Input.RegisterHotKey(
            "vgelumonstats",
            "VGE Show LumOn Stats",
            GlKeys.F9,
            HotkeyType.DevTool,
            ctrlPressed: true);
        capi.Input.SetHotKeyHandler("vgelumonstats", OnShowStats);
    }

    private bool OnToggleLumOn(KeyCombination keyCombination)
    {
        config.Enabled = !config.Enabled;
        string status = config.Enabled ? "enabled" : "disabled";
        capi.TriggerIngameError(this, "vgelumon", $"[LumOn] {status}");
        return true;
    }

    private bool OnCycleDebugMode(KeyCombination keyCombination)
    {
        // Cycle through debug modes: 0-7
        config.DebugMode = (config.DebugMode + 1) % 8;
        string[] modeNames =
        [
            "Off (normal)",
            "Probe Grid",
            "Probe Depth",
            "Probe Normals",
            "Scene Depth",
            "Scene Normals",
            "Temporal Weight",
            "Temporal Rejection"
        ];
        capi.TriggerIngameError(this, "vgelumondebug", $"[LumOn] Debug: {modeNames[config.DebugMode]}");
        return true;
    }

    private bool OnShowStats(KeyCombination keyCombination)
    {
        var c = debugCounters;
        string stats = $"[LumOn] Probes: {c.TotalProbes} | " +
                       $"Time: {c.TotalFrameMs:F2}ms (A:{c.ProbeAnchorPassMs:F2} T:{c.ProbeTracePassMs:F2} " +
                       $"Tp:{c.TemporalPassMs:F2} G:{c.GatherPassMs:F2} U:{c.UpsamplePassMs:F2})";
        capi.ShowChatMessage(stats);
        return true;
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (quadMeshRef is null || !config.Enabled)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
            return;

        // Collect GPU timing from previous frame (avoid stalls)
        CollectTimerQueryResults();

        // Reset debug counters
        debugCounters.Reset();
        debugCounters.TotalProbes = bufferManager.ProbeCountX * bufferManager.ProbeCountY;

        // Ensure LumOn buffers are allocated
        bufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Check for teleportation (large camera movement)
        if (DetectTeleport())
        {
            bufferManager.ClearHistory();
            isFirstFrame = true;
        }

        // Handle first frame (no valid history)
        if (isFirstFrame)
        {
            bufferManager.ClearHistory();
            isFirstFrame = false;
        }

        // Capture the current scene for radiance sampling
        bufferManager.CaptureScene(primaryFb.FboId, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Update matrices
        UpdateMatrices();

        // === Pass 1: Probe Anchor ===
        BeginTimerQuery(0);
        RenderProbeAnchorPass(primaryFb);
        EndTimerQuery();

        // === Pass 2: Probe Trace ===
        BeginTimerQuery(1);
        RenderProbeTracePass(primaryFb);
        EndTimerQuery();

        // === Pass 3: Temporal Accumulation ===
        BeginTimerQuery(2);
        RenderTemporalPass();
        EndTimerQuery();

        // === Pass 4: Gather ===
        BeginTimerQuery(3);
        RenderGatherPass(primaryFb);
        EndTimerQuery();

        // === Pass 5: Upsample ===
        BeginTimerQuery(4);
        RenderUpsamplePass(primaryFb);
        EndTimerQuery();

        timerQueryPending = true;

        // Store current view-projection matrix for next frame
        Array.Copy(currentViewProjMatrix, prevViewProjMatrix, 16);

        // Swap radiance buffers for temporal accumulation
        bufferManager.SwapRadianceBuffers();

        // Store camera position for teleport detection
        StoreCameraPosition();

        frameIndex++;
    }

    /// <summary>
    /// Detects large camera movements (teleportation) that invalidate history.
    /// </summary>
    private bool DetectTeleport()
    {
        var origin = capi.Render.CameraMatrixOrigin;
        if (origin == null)
            return false;

        double dx = origin[0] - lastCameraX;
        double dy = origin[1] - lastCameraY;
        double dz = origin[2] - lastCameraZ;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        return distance > TeleportThreshold;
    }

    /// <summary>
    /// Stores current camera position for next frame's teleport detection.
    /// </summary>
    private void StoreCameraPosition()
    {
        var origin = capi.Render.CameraMatrixOrigin;
        if (origin == null)
            return;

        lastCameraX = origin[0];
        lastCameraY = origin[1];
        lastCameraZ = origin[2];
    }

    private void UpdateMatrices()
    {
        // Copy current matrices
        Array.Copy(capi.Render.CurrentProjectionMatrix, projectionMatrix, 16);
        Array.Copy(capi.Render.CameraMatrixOriginf, modelViewMatrix, 16);

        // Compute inverse matrices
        MatrixHelper.Invert(projectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(modelViewMatrix, invModelViewMatrix);

        // Compute current view-projection matrix for next frame's reprojection
        MatrixHelper.Multiply(projectionMatrix, modelViewMatrix, currentViewProjMatrix);
    }

    /// <summary>
    /// Pass 1: Build probe anchors from G-buffer depth and normals.
    /// Output: ProbeAnchor textures with probe positions and normals.
    /// Implements validation from LumOn.02-Probe-Grid.md:
    /// - Sky rejection (depth >= 0.9999)
    /// - Edge detection via depth discontinuity (partial validity)
    /// - Invalid normal rejection
    /// </summary>
    private void RenderProbeAnchorPass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_anchor") as LumOnProbeAnchorShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ProbeAnchorFbo;
        if (fbo is null) return;

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind G-buffer textures
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ModelViewMatrix = modelViewMatrix;  // For world-to-view space normal transform
        
        // Pass probe grid uniforms
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Edge detection threshold for depth discontinuity
        shader.DepthDiscontinuityThreshold = config.DepthDiscontinuityThreshold;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 2: Ray trace from each probe and accumulate radiance into SH.
    /// Output: ProbeRadiance textures with SH coefficients.
    /// </summary>
    private void RenderProbeTracePass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_trace") as LumOnProbeTraceShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.RadianceTraceFbo;
        if (fbo is null) return;

        // Write to dedicated trace buffer (separate from temporal current/history)
        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind probe anchor textures
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex;

        // Bind scene for radiance sampling (captured before this pass)
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.PrimaryColor = bufferManager.CapturedSceneTex;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ProjectionMatrix = projectionMatrix;
        shader.ModelViewMatrix = modelViewMatrix;

        // Pass uniforms
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.FrameIndex = frameIndex;
        shader.RaysPerProbe = config.RaysPerProbePerFrame;
        shader.RaySteps = config.RaySteps;
        shader.RayMaxDistance = config.RayMaxDistance;
        shader.RayThickness = config.RayThickness;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Sky fallback colors
        shader.SkyMissWeight = config.SkyMissWeight;
        shader.SunPosition = capi.World.Calendar.SunPositionNormalized;
        shader.SunColor = capi.World.Calendar.SunColor;
        shader.AmbientColor = capi.Render.AmbientColor;

        // Indirect lighting tint (from config)
        shader.IndirectTint = new Vec3f(
            config.IndirectTint[0], 
            config.IndirectTint[1], 
            config.IndirectTint[2]);

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 3: Blend current radiance with history for temporal stability.
    /// Implements reprojection, validation, and neighborhood clamping.
    /// Output: Updated radiance history and metadata.
    /// </summary>
    private void RenderTemporalPass()
    {
        var shader = ShaderRegistry.getProgramByName("lumon_temporal") as LumOnTemporalShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.TemporalOutputFbo;
        if (fbo is null) return;

        // Bind temporal output FBO (MRT: radiance0, radiance1, meta)
        fbo.BindWithViewport();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind current frame radiance (from trace pass - dedicated trace buffer)
        shader.RadianceCurrent0 = bufferManager.RadianceTraceTex0;
        shader.RadianceCurrent1 = bufferManager.RadianceTraceTex1;

        // Bind history radiance (from previous frame, after last swap)
        shader.RadianceHistory0 = bufferManager.RadianceHistoryTex0;
        shader.RadianceHistory1 = bufferManager.RadianceHistoryTex1;

        // Bind probe anchors for validation and reprojection
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex;

        // Bind history metadata for validation
        shader.HistoryMeta = bufferManager.ProbeMetaHistoryTex;

        // Pass matrices for reprojection
        shader.InvViewMatrix = invModelViewMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;

        // Pass probe grid size
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);

        // Pass depth parameters
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Pass temporal parameters
        shader.TemporalAlpha = config.TemporalAlpha;
        shader.DepthRejectThreshold = config.DepthRejectThreshold;
        shader.NormalRejectThreshold = config.NormalRejectThreshold;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 4: Gather irradiance at each pixel by interpolating nearby probes.
    /// Output: Half-resolution indirect diffuse.
    /// </summary>
    private void RenderGatherPass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_gather") as LumOnGatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind radiance textures (from current after temporal blend)
        // Note: Temporal pass writes to current, gather reads from current
        shader.RadianceTexture0 = bufferManager.RadianceCurrentTex0;
        shader.RadianceTexture1 = bufferManager.RadianceCurrentTex1;

        // Bind probe anchors
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex;

        // Bind G-buffer for pixel info
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DepthDiscontinuityThreshold = config.DepthDiscontinuityThreshold;
        shader.Intensity = config.Intensity;
        shader.IndirectTint = config.IndirectTint;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 5: Bilateral upsample from half-res to full resolution.
    /// Output: Full-resolution indirect diffuse composited to screen.
    /// </summary>
    private void RenderUpsamplePass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_upsample") as LumOnUpsampleShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        // Bind primary framebuffer for final composite (VS-managed FBO)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Enable additive blending for indirect light contribution
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        shader.Use();

        // Bind half-res indirect diffuse
        shader.IndirectHalf = bufferManager.IndirectHalfTex;

        // Bind G-buffer for edge-aware upsampling
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DenoiseEnabled = config.DenoiseEnabled ? 1 : 0;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();

        // Restore blend state
        capi.Render.GlToggleBlend(false);
    }

    #endregion

    #region Matrix Utilities

    // Matrix utilities moved to VanillaGraphicsExpanded.Rendering.MatrixHelper

    #endregion

    #region GPU Timer Queries

    private void BeginTimerQuery(int passIndex)
    {
        if (!timerQueriesInitialized || passIndex < 0 || passIndex >= timerQueries.Length)
            return;

        GL.BeginQuery(QueryTarget.TimeElapsed, timerQueries[passIndex]);
    }

    private void EndTimerQuery()
    {
        GL.EndQuery(QueryTarget.TimeElapsed);
    }

    private void CollectTimerQueryResults()
    {
        if (!timerQueryPending || !timerQueriesInitialized)
            return;

        // Check if results are available (avoid stalls)
        GL.GetQueryObject(timerQueries[4], GetQueryObjectParam.QueryResultAvailable, out int available);
        if (available == 0)
            return;

        // Collect all timing results
        for (int i = 0; i < timerQueries.Length; i++)
        {
            GL.GetQueryObject(timerQueries[i], GetQueryObjectParam.QueryResult, out long nanoseconds);
            float ms = nanoseconds / 1_000_000f;

            switch (i)
            {
                case 0: debugCounters.ProbeAnchorPassMs = ms; break;
                case 1: debugCounters.ProbeTracePassMs = ms; break;
                case 2: debugCounters.TemporalPassMs = ms; break;
                case 3: debugCounters.GatherPassMs = ms; break;
                case 4: debugCounters.UpsamplePassMs = ms; break;
            }
        }

        debugCounters.TotalFrameMs =
            debugCounters.ProbeAnchorPassMs +
            debugCounters.ProbeTracePassMs +
            debugCounters.TemporalPassMs +
            debugCounters.GatherPassMs +
            debugCounters.UpsamplePassMs;

        timerQueryPending = false;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        // Unregister world events
        capi.Event.LeaveWorld -= OnLeaveWorld;

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        if (timerQueriesInitialized)
        {
            GL.DeleteQueries(timerQueries.Length, timerQueries);
            timerQueriesInitialized = false;
        }
    }

    #endregion
}
