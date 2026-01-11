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
    private const double RENDER_ORDER = 10;
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
    // Indices:
    // 0=HZB, 1=Anchor, 2=Trace, 3=Temporal, 4=Filter, 5=Projection, 6=Gather, 7=Upsample
    private readonly int[] timerQueries = new int[8];
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
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "lumon");
        // capi.Event.RegisterRenderer(this, EnumRenderStage.AfterPostProcessing, "lumon");

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
        // Cycle through debug modes in a stable, explicit order.
        var cycle = new[]
        {
            LumOnDebugMode.Off,
            LumOnDebugMode.ProbeGrid,
            LumOnDebugMode.ProbeDepth,
            LumOnDebugMode.ProbeNormal,
            LumOnDebugMode.SceneDepth,
            LumOnDebugMode.SceneNormal,
            LumOnDebugMode.TemporalWeight,
            LumOnDebugMode.TemporalRejection,
            LumOnDebugMode.ShCoefficients,
            LumOnDebugMode.InterpolationWeights,
            LumOnDebugMode.RadianceOverlay,
            LumOnDebugMode.GatherWeight,
            LumOnDebugMode.ProbeAtlasMetaConfidence,
            LumOnDebugMode.ProbeAtlasTemporalAlpha,
            LumOnDebugMode.ProbeAtlasMetaFlags,
            LumOnDebugMode.ProbeAtlasFilteredRadiance,
            LumOnDebugMode.ProbeAtlasFilterDelta,
            LumOnDebugMode.ProbeAtlasGatherInputSource
        };

        int currentIndex = Array.IndexOf(cycle, config.DebugMode);
        if (currentIndex < 0) currentIndex = 0;
        int nextIndex = (currentIndex + 1) % cycle.Length;
        config.DebugMode = cycle[nextIndex];

        capi.TriggerIngameError(this, "vgelumondebug", $"[LumOn] Debug: {GetDebugModeDisplayName(config.DebugMode)}");
        return true;
    }

    private static string GetDebugModeDisplayName(LumOnDebugMode mode) => mode switch
    {
        LumOnDebugMode.Off => "Off (normal)",
        LumOnDebugMode.ProbeGrid => "Probe Grid",
        LumOnDebugMode.ProbeDepth => "Probe Depth",
        LumOnDebugMode.ProbeNormal => "Probe Normals",
        LumOnDebugMode.SceneDepth => "Scene Depth",
        LumOnDebugMode.SceneNormal => "Scene Normals",
        LumOnDebugMode.TemporalWeight => "Temporal Weight",
        LumOnDebugMode.TemporalRejection => "Temporal Rejection",
        LumOnDebugMode.ShCoefficients => "SH Coefficients",
        LumOnDebugMode.InterpolationWeights => "Interpolation Weights",
        LumOnDebugMode.RadianceOverlay => "Radiance Overlay",
        LumOnDebugMode.GatherWeight => "Gather Weight (diagnostic)",
        LumOnDebugMode.ProbeAtlasMetaConfidence => "Probe-Atlas Meta Confidence",
        LumOnDebugMode.ProbeAtlasTemporalAlpha => "Probe-Atlas Temporal Alpha",
        LumOnDebugMode.ProbeAtlasMetaFlags => "Probe-Atlas Meta Flags",
        LumOnDebugMode.ProbeAtlasFilteredRadiance => "Probe-Atlas Filtered Radiance",
        LumOnDebugMode.ProbeAtlasFilterDelta => "Probe-Atlas Filter Delta",
        LumOnDebugMode.ProbeAtlasGatherInputSource => "Probe-Atlas Gather Input Source",
        _ => mode.ToString()
    };

    private bool OnShowStats(KeyCombination keyCombination)
    {
        var c = debugCounters;
        string stats = $"[LumOn] Probes: {c.TotalProbes} | " +
                       $"Time: {c.TotalFrameMs:F2}ms (HZB:{c.HzbPassMs:F2} A:{c.ProbeAnchorPassMs:F2} T:{c.ProbeTracePassMs:F2} " +
                       $"Tp:{c.TemporalPassMs:F2} F:{c.ProbeAtlasFilterPassMs:F2} P:{c.ProbeAtlasProjectionPassMs:F2} " +
                       $"G:{c.GatherPassMs:F2} U:{c.UpsamplePassMs:F2})";
        capi.ShowChatMessage(stats);
        return true;
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        TryRenderFrame(deltaTime, stage);

        // Ensure VS's primary framebuffer is bound for subsequent passes.
        // LumOn is intended to overwrite the primary color attachment so the base game's post-processing
        // consumes the updated scene.
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb != null)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        }
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);
    }

    private bool TryRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (quadMeshRef is null || !config.Enabled)
            return false;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
            return false;

        // Ensure GBuffer textures are valid for current screen size
        // This handles resize events that may have invalidated the textures
        if (gBufferManager is null || !gBufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight))
        {
            return false;  // GBuffer not ready, skip this frame
        }

        // Ensure LumOn buffers are allocated
        if (bufferManager is null || !bufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight))
        {
            // Reset temporal state since buffers are new
            isFirstFrame = true;
            capi.Logger.Debug($"[LumOn] Screen resized to {capi.Render.FrameWidth}x{capi.Render.FrameHeight}, skipping frame to stabilize");
            return false;
        }

        // Collect GPU timing from previous frame (avoid stalls)
        CollectTimerQueryResults();

        // Reset debug counters
        debugCounters.Reset();
        debugCounters.TotalProbes = bufferManager.ProbeCountX * bufferManager.ProbeCountY;

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

        // Capture the current scene for radiance sampling.
        // IMPORTANT: Capture from VS's primary framebuffer (ColorAttachment0), since that's what the
        // base game's post processing consumes.
        bufferManager.CaptureScene(primaryFb.FboId, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Update matrices
        UpdateMatrices();

        // === Pass 0: HZB depth pyramid ===
        BeginTimerQuery(0);
        BuildHzb(primaryFb);
        EndTimerQuery();

        // === Pass 1: Probe Anchor ===
        BeginTimerQuery(1);
        RenderProbeAnchorPass(primaryFb);
        EndTimerQuery();

        // === Pass 2: Probe Trace ===
        BeginTimerQuery(2);
        if (config.UseProbeAtlas)
        {
            RenderProbeAtlasTracePass(primaryFb);
        }
        else
        {
            RenderProbeTracePass(primaryFb);
        }
        EndTimerQuery();

        // === Pass 3: Temporal Accumulation ===
        BeginTimerQuery(3);
        if (config.UseProbeAtlas)
        {
            RenderProbeAtlasTemporalPass();
        }
        else
        {
            RenderSHTemporalPass();
        }
        EndTimerQuery();

        // === Pass 3.5: Probe-Atlas Filter/Denoise (Probe-space) ===
        BeginTimerQuery(4);
        if (config.UseProbeAtlas)
        {
            RenderProbeAtlasFilterPass();
        }
        EndTimerQuery();

        // === Pass 3.75: Probe-Atlas Projection (Option B) ===
        BeginTimerQuery(5);
        if (config.UseProbeAtlas && config.ProbeAtlasGather == LumOnConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
        {
            RenderProbeAtlasProjectSh9Pass();
        }
        EndTimerQuery();

        // === Pass 4: Gather ===
        BeginTimerQuery(6);
        RenderGatherPass(primaryFb);
        EndTimerQuery();

        // === Pass 5: Upsample ===
        BeginTimerQuery(7);
        RenderUpsamplePass(primaryFb);
        EndTimerQuery();

        // === Pass 6: Combine (optional) ===
        // Only runs when UseCombinePass is enabled for proper material modulation
        if (config.UseCombinePass)
        {
            RenderCombinePass(primaryFb);
        }

        timerQueryPending = true;

        // Store current view-projection matrix for next frame
        Array.Copy(currentViewProjMatrix, prevViewProjMatrix, 16);

        // Swap radiance buffers for temporal accumulation
        bufferManager.SwapRadianceBuffers();

        // Store camera position for teleport detection
        StoreCameraPosition();

        frameIndex++;
        return true;
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
    /// Output: ProbeAnchor textures with world-space positions and normals.
    /// 
    /// Output is in WORLD-SPACE (matching UE5 Lumen's design) for temporal stability:
    /// - World-space directions remain valid across camera rotations
    /// - Radiance stored per world-space direction can be directly blended
    /// 
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
        shader.InvViewMatrix = invModelViewMatrix;  // For view-space to world-space transform
        
        // Pass probe grid uniforms
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Deterministic probe jitter (uses existing Squirrel3Hash in shader)
        shader.FrameIndex = frameIndex;
        shader.AnchorJitterEnabled = config.AnchorJitterEnabled ? 1 : 0;
        shader.AnchorJitterScale = config.AnchorJitterScale;
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
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind scene for radiance sampling (captured before this pass)
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.PrimaryColor = bufferManager.CapturedSceneTex!;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ProjectionMatrix = projectionMatrix;
        shader.ViewMatrix = modelViewMatrix;  // viewMatrix transforms WS probe data to VS for ray marching

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
    /// Pass 2 (Screen-Probe Atlas): Ray trace from each probe and store radiance + hit distance.
    /// Uses temporal distribution: only traces a subset of texels each frame.
    /// Output: Probe atlas texture with radiance and hit distance.
    /// </summary>
    private void RenderProbeAtlasTracePass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_atlas_trace") as LumOnScreenProbeAtlasTraceShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ScreenProbeAtlasTraceFbo;
        if (fbo is null) return;

        // Render at atlas resolution (probeCountX * 8, probeCountY * 8)
        fbo.BindWithViewport();
        // Don't clear - we want to preserve non-traced texels from history
        // The shader handles history read for non-traced texels

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind probe anchor textures
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind scene for radiance sampling
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.PrimaryColor = bufferManager.CapturedSceneTex!;

        // Bind history for temporal preservation
        shader.ScreenProbeAtlasHistory = bufferManager.ScreenProbeAtlasHistoryTex!;
        shader.ScreenProbeAtlasMetaHistory = bufferManager.ScreenProbeAtlasMetaHistoryTex!;

        // HZB depth pyramid (always on)
        if (bufferManager.HzbDepthTex != null)
        {
            shader.HzbDepth = bufferManager.HzbDepthTex;
            shader.HzbCoarseMip = Math.Clamp(config.HzbCoarseMip, 0, Math.Max(0, bufferManager.HzbDepthTex.MipLevels - 1));
        }

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ProjectionMatrix = projectionMatrix;
        shader.ViewMatrix = modelViewMatrix;
        shader.InvViewMatrix = invModelViewMatrix;

        // Pass uniforms
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.FrameIndex = frameIndex;
        shader.TexelsPerFrame = config.ProbeAtlasTexelsPerFrame;
        shader.RaySteps = config.RaySteps;
        shader.RayMaxDistance = config.RayMaxDistance;
        shader.RayThickness = config.RayThickness;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Sky fallback colors
        shader.SkyMissWeight = config.SkyMissWeight;
        var sunPos = capi.World.Calendar.SunPositionNormalized;
        shader.SunPosition = new Vec3f((float)sunPos.X, (float)sunPos.Y, (float)sunPos.Z);
        var sunCol = capi.World.Calendar.SunColor;
        shader.SunColor = new Vec3f(sunCol.R, sunCol.G, sunCol.B);
        shader.AmbientColor = capi.Render.AmbientColor;

        // Indirect lighting tint
        shader.IndirectTint = new Vec3f(
            config.IndirectTint[0],
            config.IndirectTint[1],
            config.IndirectTint[2]);

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    private void BuildHzb(FrameBufferRef primaryFb)
    {
        if (bufferManager.HzbDepthTex is null || bufferManager.HzbFboId == 0)
            return;

        var copy = ShaderRegistry.getProgramByName("lumon_hzb_copy") as LumOnHzbCopyShaderProgram;
        var down = ShaderRegistry.getProgramByName("lumon_hzb_downsample") as LumOnHzbDownsampleShaderProgram;
        if (copy is null || down is null || copy.LoadError || down.LoadError)
            return;

        int previousFbo = Rendering.GBuffer.SaveBinding();

        var hzb = bufferManager.HzbDepthTex;
        int fboId = bufferManager.HzbFboId;

        capi.Render.GlToggleBlend(false);

        // Copy mip 0 from the primary depth texture.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hzb.TextureId, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, hzb.Width, hzb.Height);

        copy.Use();
        copy.PrimaryDepth = primaryFb.DepthTextureId;
        capi.Render.RenderMesh(quadMeshRef);
        copy.Stop();

        // Downsample the mip chain using MIN depth.
        down.Use();
        down.HzbDepth = hzb;

        for (int dstMip = 1; dstMip < hzb.MipLevels; dstMip++)
        {
            int dstW = Math.Max(1, hzb.Width >> dstMip);
            int dstH = Math.Max(1, hzb.Height >> dstMip);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hzb.TextureId, dstMip);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(0, 0, dstW, dstH);

            down.SrcMip = dstMip - 1;
            capi.Render.RenderMesh(quadMeshRef);
        }

        down.Stop();

        Rendering.GBuffer.RestoreBinding(previousFbo);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);
    }

    /// <summary>
    /// Pass 3 (SH mode): Blend current SH radiance with history for temporal stability.
    /// Implements reprojection, validation, and neighborhood clamping.
    /// Output: Updated radiance history and metadata.
    /// </summary>
    private void RenderSHTemporalPass()
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
        shader.RadianceCurrent0 = bufferManager.RadianceTraceTex0!;
        shader.RadianceCurrent1 = bufferManager.RadianceTraceTex1!;

        // Bind history radiance (from previous frame, after last swap)
        shader.RadianceHistory0 = bufferManager.RadianceHistoryTex0!;
        shader.RadianceHistory1 = bufferManager.RadianceHistoryTex1!;

        // Bind probe anchors for validation and reprojection
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind history metadata for validation
        shader.HistoryMeta = bufferManager.ProbeMetaHistoryTex!;

        // Pass matrices for reprojection
        shader.ViewMatrix = modelViewMatrix;      // WS to VS for depth calc
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
    /// Pass 3 (Screen-Probe Atlas mode): Per-texel temporal blending for probe-atlas radiance.
    /// Only blends texels that were traced this frame; preserves non-traced texels.
    /// Uses hit-distance delta for per-texel disocclusion detection.
    /// Output: Blended probe atlas to ScreenProbeAtlasCurrentFbo.
    /// </summary>
    private void RenderProbeAtlasTemporalPass()
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_atlas_temporal") as LumOnScreenProbeAtlasTemporalShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ScreenProbeAtlasCurrentFbo;
        if (fbo is null) return;

        // Render to current octahedral atlas (which will become history after swap)
        fbo.BindWithViewport();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind trace output (fresh traced texels + history copies for non-traced)
        var traceTex = bufferManager.ScreenProbeAtlasTraceTex;
        if (traceTex is not null)
        {
            shader.ScreenProbeAtlasCurrent = traceTex.TextureId;
        }

        // Bind history (from previous frame, before swap)
        var historyTex = bufferManager.ScreenProbeAtlasHistoryTex;
        if (historyTex is not null)
        {
            shader.ScreenProbeAtlasHistory = historyTex.TextureId;
        }

        // Bind probe anchors for validity check
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;

        // Bind meta trace output (pass-through for now)
        shader.ScreenProbeAtlasMetaCurrent = bufferManager.ScreenProbeAtlasMetaTraceTex!;

        // Bind meta history from previous frame
        shader.ScreenProbeAtlasMetaHistory = bufferManager.ScreenProbeAtlasMetaHistoryTex!;

        // Pass probe grid size
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);

        // Pass temporal distribution parameters (must match trace shader)
        shader.FrameIndex = frameIndex;
        shader.TexelsPerFrame = config.ProbeAtlasTexelsPerFrame;

        // Pass temporal blending parameters
        shader.TemporalAlpha = config.TemporalAlpha;
        shader.HitDistanceRejectThreshold = 0.3f;  // 30% relative difference threshold

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 4: Gather irradiance at each pixel by interpolating nearby probes.
    /// Output: Half-resolution indirect diffuse.
    /// 
    /// Two modes:
    /// - SH mode (UseProbeAtlas = false): Evaluate SH at pixel normal
    /// - Probe-atlas mode (UseProbeAtlas = true): Either integrate atlas directly or project atlas→SH then evaluate
    /// </summary>
    private void RenderGatherPass(FrameBufferRef primaryFb)
    {
        if (!config.UseProbeAtlas)
        {
            RenderSHGatherPass(primaryFb);
            return;
        }

        if (config.ProbeAtlasGather == LumOnConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
        {
            RenderProbeSh9GatherPass(primaryFb);
            return;
        }

        RenderProbeAtlasGatherPass(primaryFb);
    }

    /// <summary>
    /// Phase 12 Option B: project the (filtered) probe atlas into packed SH9 coefficients per probe.
    /// Output: writes to ProbeSh9 textures (7 MRT attachments).
    /// </summary>
    private void RenderProbeAtlasProjectSh9Pass()
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_atlas_project_sh9") as LumOnScreenProbeAtlasProjectSh9ShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var outFbo = bufferManager.ProbeSh9Fbo;
        if (outFbo is null) return;

        var inputAtlas = bufferManager.ScreenProbeAtlasFilteredTex
            ?? bufferManager.ScreenProbeAtlasCurrentTex
            ?? bufferManager.ScreenProbeAtlasTraceTex;
        var inputMeta = bufferManager.ScreenProbeAtlasMetaFilteredTex
            ?? bufferManager.ScreenProbeAtlasMetaCurrentTex
            ?? bufferManager.ScreenProbeAtlasMetaTraceTex;

        if (inputAtlas is null || inputMeta is null) return;

        outFbo.BindWithViewport();
        outFbo.Clear();
        capi.Render.GlToggleBlend(false);

        shader.Use();
        shader.ScreenProbeAtlas = inputAtlas.TextureId;
        shader.ScreenProbeAtlasMeta = inputMeta.TextureId;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);

        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// SH9 gather pass for probe-atlas projected SH mode.
    /// Evaluates per-probe SH9 at each pixel normal and interpolates with edge-aware weights.
    /// </summary>
    private void RenderProbeSh9GatherPass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_sh9_gather") as LumOnProbeSh9GatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        if (bufferManager.ProbeSh9Tex0 is null || bufferManager.ProbeSh9Tex6 is null)
            return;

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        shader.ProbeSh0 = bufferManager.ProbeSh9Tex0.TextureId;
        shader.ProbeSh1 = bufferManager.ProbeSh9Tex1!.TextureId;
        shader.ProbeSh2 = bufferManager.ProbeSh9Tex2!.TextureId;
        shader.ProbeSh3 = bufferManager.ProbeSh9Tex3!.TextureId;
        shader.ProbeSh4 = bufferManager.ProbeSh9Tex4!.TextureId;
        shader.ProbeSh5 = bufferManager.ProbeSh9Tex5!.TextureId;
        shader.ProbeSh6 = bufferManager.ProbeSh9Tex6.TextureId;

        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = modelViewMatrix;
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.Intensity = config.Intensity;
        shader.IndirectTint = config.IndirectTint;

        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// SH-based gather pass (legacy mode).
    /// Evaluates SH coefficients at each pixel's normal direction.
    /// </summary>
    private void RenderSHGatherPass(FrameBufferRef primaryFb)
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
        shader.RadianceTexture0 = bufferManager.RadianceCurrentTex0!;
        shader.RadianceTexture1 = bufferManager.RadianceCurrentTex1!;

        // Bind probe anchors
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind G-buffer for pixel info
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = modelViewMatrix;
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DepthDiscontinuityThreshold = config.DepthDiscontinuityThreshold;
        shader.Intensity = config.Intensity;
        shader.IndirectTint = config.IndirectTint;

        // Edge-aware weighting parameters (SPG-007 Section 2.3)
        shader.DepthSigma = config.GatherDepthSigma;
        shader.NormalSigma = config.GatherNormalSigma;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Screen-probe atlas gather pass (new mode).
    /// Integrates radiance over hemisphere from probe-atlas tiles for each pixel.
    /// Provides per-direction hit distance for leak prevention.
    /// </summary>
    private void RenderProbeAtlasGatherPass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_atlas_gather") as LumOnScreenProbeAtlasGatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        // Prefer filtered atlas (post-temporal) when available.
        var probeAtlas = bufferManager.ScreenProbeAtlasFilteredTex
            ?? bufferManager.ScreenProbeAtlasCurrentTex
            ?? bufferManager.ScreenProbeAtlasTraceTex;
        if (probeAtlas is null) return;

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind screen-probe atlas radiance
        shader.ScreenProbeAtlas = probeAtlas.TextureId;

        // Bind probe anchors
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind G-buffer for pixel info
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = modelViewMatrix;
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.Intensity = config.Intensity;
        shader.IndirectTint = config.IndirectTint;

        // Probe-atlas gather parameters (from config per Section 2.5)
        shader.LeakThreshold = config.ProbeAtlasLeakThreshold;
        shader.SampleStride = config.ProbeAtlasSampleStride;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 3.5 (Screen-Probe Atlas mode): Probe-space filtering/denoise.
    /// Operates within each probe's 8x8 octahedral tile with edge-stopping based on hit distance and meta.
    /// Output: Filtered probe atlas to ScreenProbeAtlasFilteredFbo.
    /// </summary>
    private void RenderProbeAtlasFilterPass()
    {
        var shader = ShaderRegistry.getProgramByName("lumon_probe_atlas_filter") as LumOnScreenProbeAtlasFilterShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ScreenProbeAtlasFilteredFbo;
        if (fbo is null) return;

        // Filter the stabilized atlas for this frame (prefer temporal output, fallback to trace)
        var inputAtlas = bufferManager.ScreenProbeAtlasCurrentTex ?? bufferManager.ScreenProbeAtlasTraceTex;
        var inputMeta = bufferManager.ScreenProbeAtlasMetaCurrentTex ?? bufferManager.ScreenProbeAtlasMetaTraceTex;
        if (inputAtlas is null || inputMeta is null) return;

        fbo.BindWithViewport();

        capi.Render.GlToggleBlend(false);
        shader.Use();

        shader.ScreenProbeAtlas = inputAtlas.TextureId;
        shader.ScreenProbeAtlasMeta = inputMeta.TextureId;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;

        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);

        // Minimal initial settings: 3x3 within-tile filter with moderate edge stopping.
        shader.FilterRadius = 1;
        shader.HitDistanceSigma = 1.0f;

        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// Pass 5: Bilateral upsample from half-res to full resolution.
    /// Output depends on config.UseCombinePass:
    /// - false: Additively blend to screen (fast path)
    /// - true: Write to IndirectFullFB for combine pass
    /// </summary>
    private void RenderUpsamplePass(FrameBufferRef primaryFb)
    {
        var shader = ShaderRegistry.getProgramByName("lumon_upsample") as LumOnUpsampleShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        if (config.UseCombinePass)
        {
            // Write to full-res indirect buffer for combine pass
            var fbo = bufferManager.IndirectFullFbo;
            if (fbo is null) return;

            fbo.BindWithViewport();
            fbo.Clear();
            capi.Render.GlToggleBlend(false);
        }
        else
        {
            // Direct additive blend to screen (fast path)
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
            GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

            // Restrict output to ColorAttachment0 so we don't accidentally write into GBuffer attachments.
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            // Additive-style blend for indirect lighting
            capi.Render.GlToggleBlend(true, EnumBlendMode.Glow);
        }

        shader.Use();

        // Bind half-res indirect diffuse
        shader.IndirectHalf = bufferManager.IndirectHalfTex!;

        // Bind G-buffer for edge-aware upsampling
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DenoiseEnabled = config.DenoiseEnabled ? 1 : 0;

        // Bilateral upsample parameters (SPG-008 Section 3.1)
        shader.UpsampleDepthSigma = config.UpsampleDepthSigma;
        shader.UpsampleNormalSigma = config.UpsampleNormalSigma;
        shader.UpsampleSpatialSigma = config.UpsampleSpatialSigma;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();

        // Restore blend state
        capi.Render.GlToggleBlend(false);
    }

    /// <summary>
    /// Pass 6 (Optional): Combine indirect diffuse with direct lighting.
    /// Only runs when config.UseCombinePass is true.
    /// Applies proper material modulation (albedo, metallic rejection).
    /// </summary>
    private void RenderCombinePass(FrameBufferRef primaryFb)
    {
        if (!config.UseCombinePass)
            return;

        var shader = ShaderRegistry.getProgramByName("lumon_combine") as LumOnCombineShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var indirectFullTex = bufferManager.IndirectFullTex;
        if (indirectFullTex is null) return;

        // Render to VS primary framebuffer so subsequent base-game post-processing sees the combined result.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // IMPORTANT: When targeting VS's primary MRT framebuffer, restrict to ColorAttachment0.
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        // No blending - we're replacing the scene color with combined result
        capi.Render.GlToggleBlend(false);
        shader.Use();

        // The scene color is already in the primary framebuffer (ColorAttachment0)
        // We need to sample it as a texture, which requires the captured scene
        shader.SceneDirect = bufferManager.CapturedSceneTex?.TextureId ?? 0;

        // Bind upsampled indirect diffuse
        shader.IndirectDiffuse = indirectFullTex.TextureId;

        // Bind G-buffer for material modulation
        // Note: VS stores albedo in ColorAttachment0, but that's the render target
        // For proper albedo modulation, we'd need a separate albedo G-buffer
        // For now, we use the captured scene as a fallback
        shader.GBufferAlbedo = bufferManager.CapturedSceneTex?.TextureId ?? 0;
        shader.GBufferMaterial = gBufferManager?.MaterialTextureId ?? 0;
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        // Pass uniforms
        shader.IndirectIntensity = config.Intensity;
        shader.IndirectTint = new Vec3f(
            config.IndirectTint[0],
            config.IndirectTint[1],
            config.IndirectTint[2]);
        shader.LumOnEnabled = config.Enabled ? 1 : 0;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
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
        GL.GetQueryObject(timerQueries[^1], GetQueryObjectParam.QueryResultAvailable, out int available);
        if (available == 0)
            return;

        // Collect all timing results
        for (int i = 0; i < timerQueries.Length; i++)
        {
            GL.GetQueryObject(timerQueries[i], GetQueryObjectParam.QueryResult, out long nanoseconds);
            float ms = nanoseconds / 1_000_000f;

            switch (i)
            {
                case 0: debugCounters.HzbPassMs = ms; break;
                case 1: debugCounters.ProbeAnchorPassMs = ms; break;
                case 2: debugCounters.ProbeTracePassMs = ms; break;
                case 3: debugCounters.TemporalPassMs = ms; break;
                case 4: debugCounters.ProbeAtlasFilterPassMs = ms; break;
                case 5: debugCounters.ProbeAtlasProjectionPassMs = ms; break;
                case 6: debugCounters.GatherPassMs = ms; break;
                case 7: debugCounters.UpsamplePassMs = ms; break;
            }
        }

        debugCounters.TotalFrameMs =
            debugCounters.HzbPassMs +
            debugCounters.ProbeAnchorPassMs +
            debugCounters.ProbeTracePassMs +
            debugCounters.TemporalPassMs +
            debugCounters.ProbeAtlasFilterPassMs +
            debugCounters.ProbeAtlasProjectionPassMs +
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
