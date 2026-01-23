using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Profiling;

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
    /// Render order for the LumOn indirect pass.
    /// </summary>
    private const double RENDER_ORDER = 10;
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumOnBufferManager bufferManager;
    private readonly GBufferManager? gBufferManager;

    private LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManager;

    private LumOnWorldProbeScheduler? worldProbeScheduler;
    private Action<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>? worldProbeSchedulerAnchorShiftHandler;
    private LumOnWorldProbeTraceService? worldProbeTraceService;
    private BlockAccessorWorldProbeTraceScene? worldProbeTraceScene;
    private IBlockAccessor? worldProbeTraceBlockAccessor;

    private readonly System.Collections.Generic.List<LumOnWorldProbeTraceResult> worldProbeResults = new();

    // Debug-only CPU->GPU heatmap buffer for world-probe lifecycle visualization.
    private LumOnWorldProbeLifecycleState[]? worldProbeLifecycleScratch;
    private ushort[]? worldProbeDebugStateTexels;

    // Cache arrays to avoid per-frame allocations in TryBindWorldProbeClipmapCommon.
    private readonly Vec3f[] worldProbeOriginsCache = new Vec3f[8];
    private readonly Vec3f[] worldProbeRingsCache = new Vec3f[8];
    private readonly LumOnWorldProbeClipmapBufferManager.DebugTraceRay[] worldProbeDebugQueuedTraceRaysScratch =
        new LumOnWorldProbeClipmapBufferManager.DebugTraceRay[LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays];

    private bool worldProbeClipmapStartupLogged;

    private readonly LumOnPmjJitterTexture pmjJitter;

    // Fullscreen quad mesh
    private MeshRef? quadMeshRef;

    // Matrix buffers
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];
    private readonly float[] projectionMatrix = new float[16];
    private readonly float[] modelViewMatrix = new float[16];
    private readonly float[] prevViewProjMatrix = new float[16];
    private readonly float[] currentViewProjMatrix = new float[16];
    private readonly float[] invCurrentViewProjMatrix = new float[16];

    // Frame counter for ray jittering
    private int frameIndex;

    // First frame detection (no valid history)
    private bool isFirstFrame = true;

    // Teleport detection
    private double lastCameraX;
    private double lastCameraY;
    private double lastCameraZ;

    // Debug counters
    private readonly LumOnDebugCounters debugCounters = new();

    // Phase 14.5: reset diagnostics (throttle on-screen notifications)
    private int lastHistoryResetNotifyFrameIndex = -999999;
    private string lastHistoryResetNotifyReason = string.Empty;

    #endregion

    #region Properties

    /// <summary>
    /// Whether LumOn rendering is enabled.
    /// </summary>
    public bool Enabled => config.LumOn.Enabled;

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

    internal LumOnRenderer(
        ICoreClientAPI capi,
        VgeConfig config,
        LumOnBufferManager bufferManager,
        GBufferManager? gBufferManager,
        LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManager = null)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;
        this.worldProbeClipmapBufferManager = worldProbeClipmapBufferManager;

        pmjJitter = new LumOnPmjJitterTexture(config.LumOn.PmjJitterCycleLength, config.LumOn.PmjJitterSeed);

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

        // Register for world events (teleport, world change)
        RegisterWorldEvents();

        capi.Logger.Notification("[LumOn] Renderer initialized");
    }

    internal void SetWorldProbeClipmapBufferManager(LumOnWorldProbeClipmapBufferManager? bufferManager)
    {
        worldProbeClipmapBufferManager = bufferManager;
    }

    private void ApplyPendingWorldProbeDirtyChunks(double baseSpacing)
    {
        if (worldProbeScheduler is null)
        {
            return;
        }

        var wpms = capi.ModLoader.GetModSystem<WorldProbeModSystem>();
        if (wpms is null)
        {
            return;
        }

        int chunkSize = GlobalConstants.ChunkSize;
        int levels = worldProbeScheduler.LevelCount;

        wpms.DrainPendingWorldProbeDirtyChunks(
            onChunk: (cx, cy, cz) =>
            {
                var min = new Vec3d(cx * chunkSize, cy * chunkSize, cz * chunkSize);
                var max = new Vec3d(min.X + chunkSize, min.Y + chunkSize, min.Z + chunkSize);

                for (int level = 0; level < levels; level++)
                {
                    worldProbeScheduler.MarkDirtyWorldAabb(level, min, max, baseSpacing);
                }
            },
            overflowCount: out int overflow);

        // If we overflowed, conservatively invalidate everything at L0 by marking the current clip volume dirty.
        if (overflow > 0 && worldProbeScheduler.TryGetLevelParams(0, out var originMin, out _))
        {
            double spacing0 = LumOnClipmapTopology.GetSpacing(baseSpacing, level: 0);
            double size = spacing0 * worldProbeScheduler.Resolution;
            var min = originMin;
            var max = new Vec3d(min.X + size, min.Y + size, min.Z + size);
            worldProbeScheduler.MarkDirtyWorldAabb(level: 0, min, max, baseSpacing);
        }
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
        worldProbeScheduler?.ResetAll();
        worldProbeTraceService?.Dispose();
        worldProbeTraceService = null;
        worldProbeTraceScene = null;

        if (worldProbeClipmapBufferManager?.Resources is not null)
        {
            worldProbeClipmapBufferManager.Resources.ClearAll();
        }

        capi.Logger.Debug("[LumOn] World left, cleared history");
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
        config.LumOn.Enabled = !config.LumOn.Enabled;
        string status = config.LumOn.Enabled ? "enabled" : "disabled";
        capi.TriggerIngameError(this, "vgelumon", $"[LumOn] {status}");
        return true;
    }

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
        pmjJitter.EnsureCreated();
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
        if (quadMeshRef is null || !config.LumOn.Enabled)
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

            // Phase 14.5: on-screen notification for forced reset (throttled)
            NotifyHistoryReset("resize");
            return false;
        }

        // Update debug timing from resolved GPU profiler samples (avoid stalls)
        UpdateGpuTimingCountersFromProfiler();

        // Reset debug counters
        debugCounters.Reset();
        debugCounters.TotalProbes = bufferManager.ProbeCountX * bufferManager.ProbeCountY;

        // Check for teleportation (large camera movement)
        if (DetectTeleport())
        {
            bufferManager.InvalidateCache("teleport");
            isFirstFrame = true;
        }

        // History validity for this frame must be decided before we potentially clear/reset it.
        // If history is invalid, velocity should be marked invalid regardless of matrix values.
        bool historyValid = !isFirstFrame;

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

        // Generate velocity buffer early so downstream temporal consumers can sample it.
        // This pass is safe even if not yet used by all temporal shaders.
        RenderVelocityPass(primaryFb, historyValid);

        // Phase 18: run world-probe clipmap update + upload early in the frame
        // so gather shaders can sample the latest data.
        UpdateWorldProbeClipmap();

        using var cpuFrame = Profiler.BeginScope("LumOn.Frame", "Render");
        using (GlGpuProfiler.Instance.Scope("LumOn.Frame"))
        {
            // === Pass 0: HZB depth pyramid ===
            using var cpuHzb = Profiler.BeginScope("LumOn.HZB", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.HZB"))
            {
                BuildHzb(primaryFb);
            }

            // === Pass 1: Probe Anchor ===
            using var cpuAnchor = Profiler.BeginScope("LumOn.Anchor", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Anchor"))
            {
                RenderProbeAnchorPass(primaryFb);
            }

            // === Pass 2: Probe Trace ===
            using var cpuTrace = Profiler.BeginScope("LumOn.Trace", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Trace"))
            {
                if (config.LumOn.UseProbeAtlas)
                {
                    RenderProbeAtlasTracePass(primaryFb);
                }
                else
                {
                    RenderProbeTracePass(primaryFb);
                }
            }

            // === Pass 3: Temporal Accumulation ===
            using var cpuTemporal = Profiler.BeginScope("LumOn.Temporal", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Temporal"))
            {
                if (config.LumOn.UseProbeAtlas)
                {
                    RenderProbeAtlasTemporalPass();
                }
                else
                {
                    RenderSHTemporalPass();
                }
            }

            // === Pass 3.5: Probe-Atlas Filter/Denoise (Probe-space) ===
            if (config.LumOn.UseProbeAtlas)
            {
                using var cpuAtlasFilter = Profiler.BeginScope("LumOn.AtlasFilter", "Render");
                using (GlGpuProfiler.Instance.Scope("LumOn.AtlasFilter"))
                {
                    RenderProbeAtlasFilterPass();
                }
            }

            // === Pass 3.75: Probe-Atlas Projection (Option B) ===
            if (config.LumOn.UseProbeAtlas && config.LumOn.ProbeAtlasGather == VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
            {
                using var cpuAtlasProject = Profiler.BeginScope("LumOn.AtlasProjectSH9", "Render");
                using (GlGpuProfiler.Instance.Scope("LumOn.AtlasProjectSH9"))
                {
                    RenderProbeAtlasProjectSh9Pass();
                }
            }

            // === Pass 4: Gather ===
            using var cpuGather = Profiler.BeginScope("LumOn.Gather", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Gather"))
            {
                RenderGatherPass(primaryFb);
            }
        }

        // === Pass 5: Upsample ===
        using var cpuUpsample = Profiler.BeginScope("LumOn.Upsample", "Render");
        using (GlGpuProfiler.Instance.Scope("LumOn.Upsample"))
        {
            RenderUpsamplePass(primaryFb);
        }

        // Pass 6 (combine) is handled by PBRCompositeRenderer.

        // Store current view-projection matrix for next frame
        Array.Copy(currentViewProjMatrix, prevViewProjMatrix, 16);

        // Swap radiance buffers for temporal accumulation
        bufferManager.SwapRadianceBuffers();

        // Store camera position for teleport detection
        StoreCameraPosition();

        frameIndex++;
        return true;
    }

    private void NotifyHistoryReset(string reason)
    {
        // Avoid spamming when resizing window or when multiple subsystems trigger resets.
        // Require a minimum frame gap and suppress duplicates.
        const int minFrameGap = 30;

        if (frameIndex - lastHistoryResetNotifyFrameIndex < minFrameGap && lastHistoryResetNotifyReason == reason)
            return;

        lastHistoryResetNotifyFrameIndex = frameIndex;
        lastHistoryResetNotifyReason = reason;

        capi.Logger.Notification($"[LumOn] History reset: {reason}");
    }

    /// <summary>
    /// Detects large camera movements (teleportation) that invalidate history.
    /// </summary>
    private bool DetectTeleport()
    {
        var player = capi.World?.Player;
        if (player?.Entity is null)
            return false;

        var camPos = player.Entity.CameraPos;
        double dx = camPos.X - lastCameraX;
        double dy = camPos.Y - lastCameraY;
        double dz = camPos.Z - lastCameraZ;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        return distance > config.LumOn.CameraTeleportResetThreshold;
    }

    /// <summary>
    /// Stores current camera position for next frame's teleport detection.
    /// </summary>
    private void StoreCameraPosition()
    {
        var player = capi.World?.Player;
        if (player?.Entity is null)
            return;

        var camPos = player.Entity.CameraPos;
        lastCameraX = camPos.X;
        lastCameraY = camPos.Y;
        lastCameraZ = camPos.Z;
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

        // Compute inverse current view-projection for depth-based reprojection.
        MatrixHelper.Invert(currentViewProjMatrix, invCurrentViewProjMatrix);
    }

    private void RenderVelocityPass(FrameBufferRef primaryFb, bool historyValid)
    {
        if (bufferManager.VelocityFbo is null)
            return;

        var shader = capi.Shader.GetProgramByName("lumon_velocity") as LumOnVelocityShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        bufferManager.VelocityFbo.BindWithViewport();
        bufferManager.VelocityFbo.Clear();

        capi.Render.GlToggleBlend(false);

        // Emissive GI scaling is a compile-time define to avoid extra uniform plumbing.
        // Ensure we emit a float literal (e.g., 2.0) to keep GLSL typing happy.
        shader.SetDefine(VgeShaderDefines.LumOnEmissiveBoost, Math.Max(0.0f, config.LumOn.EmissiveGiBoost).ToString("0.0####", CultureInfo.InvariantCulture));

        shader.Use();

        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.InvCurrViewProjMatrix = invCurrentViewProjMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;
        shader.HistoryValid = historyValid ? 1 : 0;

        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
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
        var shader = capi.Shader.GetProgramByName("lumon_probe_anchor") as LumOnProbeAnchorShaderProgram;
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

        shader.PmjJitter = pmjJitter.TextureId;
        shader.PmjCycleLength = pmjJitter.CycleLength;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvViewMatrix = invModelViewMatrix;  // For view-space to world-space transform
        
        // Pass probe grid uniforms
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Deterministic probe jitter (uses existing Squirrel3Hash in shader)
        shader.FrameIndex = frameIndex;
        shader.AnchorJitterEnabled = config.LumOn.AnchorJitterEnabled ? 1 : 0;
        shader.AnchorJitterScale = config.LumOn.AnchorJitterScale * 0.1f;// Scale down for subtlety
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Edge detection threshold for depth discontinuity
        shader.DepthDiscontinuityThreshold = config.LumOn.DepthDiscontinuityThreshold;

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
        var shader = capi.Shader.GetProgramByName("lumon_probe_trace") as LumOnProbeTraceShaderProgram;
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

        // Bind scene depth for ray marching
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        shader.PmjJitter = pmjJitter.TextureId;
        shader.PmjCycleLength = pmjJitter.CycleLength;

        // Bind radiance sources for hit sampling.
        // Prefer the PBR split outputs (linear, pre-tonemap HDR) when available.
        var direct = DirectLightingBufferManager.Instance;
        if (direct?.IsInitialized == true && direct.DirectDiffuseTex != null && direct.EmissiveTex != null)
        {
            shader.DirectDiffuse = direct.DirectDiffuseTex.TextureId;
            shader.Emissive = direct.EmissiveTex.TextureId;
        }
        else
        {
            // Fallback: treat captured scene as "directDiffuse" and no emissive.
            shader.DirectDiffuse = bufferManager.CapturedSceneTex!;
            shader.Emissive = 0;
        }


        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ProjectionMatrix = projectionMatrix;
        shader.ViewMatrix = modelViewMatrix;  // viewMatrix transforms WS probe data to VS for ray marching

        // Pass uniforms
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.FrameIndex = frameIndex;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Sky fallback colors
        shader.SunPosition = capi.World.Calendar.SunPositionNormalized;
        shader.SunColor = capi.World.Calendar.SunColor;
        shader.AmbientColor = capi.Render.AmbientColor;

        // Indirect lighting tint (from config)
        shader.IndirectTint = new Vec3f(
            config.LumOn.IndirectTint[0], 
            config.LumOn.IndirectTint[1], 
            config.LumOn.IndirectTint[2]);

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
        var shader = capi.Shader.GetProgramByName("lumon_probe_atlas_trace") as LumOnScreenProbeAtlasTraceShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ScreenProbeAtlasTraceFbo;
        if (fbo is null) return;

        // Render at atlas resolution (probeCountX * 8, probeCountY * 8)
        fbo.BindWithViewport();
        // Don't clear - we want to preserve non-traced texels from history
        // The shader handles history read for non-traced texels

        capi.Render.GlToggleBlend(false);

        // Define-backed knobs must be set before Use() so the correct variant is bound.
        shader.SetDefine(VgeShaderDefines.LumOnEmissiveBoost, Math.Max(0.0f, config.LumOn.EmissiveGiBoost).ToString("0.0####", CultureInfo.InvariantCulture));
        shader.TexelsPerFrame = config.LumOn.ProbeAtlasTexelsPerFrame;
        shader.RaySteps = config.LumOn.RaySteps;
        shader.RayMaxDistance = config.LumOn.RayMaxDistance;
        shader.RayThickness = config.LumOn.RayThickness;
        shader.SkyMissWeight = config.LumOn.SkyMissWeight;

        if (bufferManager.HzbDepthTex != null)
        {
            shader.HzbCoarseMip = Math.Clamp(config.LumOn.HzbCoarseMip, 0, Math.Max(0, bufferManager.HzbDepthTex.MipLevels - 1));
        }

        shader.Use();

        // Bind probe anchor textures
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind scene depth for ray marching
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        // Bind radiance sources for hit sampling.
        var direct = DirectLightingBufferManager.Instance;
        if (direct?.IsInitialized == true && direct.DirectDiffuseTex != null && direct.EmissiveTex != null)
        {
            shader.DirectDiffuse = direct.DirectDiffuseTex.TextureId;
            shader.Emissive = direct.EmissiveTex.TextureId;
        }
        else
        {
            shader.DirectDiffuse = bufferManager.CapturedSceneTex!;
            shader.Emissive = 0;
        }


        // Bind history for temporal preservation
        shader.ScreenProbeAtlasHistory = bufferManager.ScreenProbeAtlasHistoryTex!;
        shader.ScreenProbeAtlasMetaHistory = bufferManager.ScreenProbeAtlasMetaHistoryTex!;

        // HZB depth pyramid (always on)
        if (bufferManager.HzbDepthTex != null)
        {
            shader.HzbDepth = bufferManager.HzbDepthTex;
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
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Sky fallback colors
        var sunPos = capi.World.Calendar.SunPositionNormalized;
        shader.SunPosition = new Vec3f((float)sunPos.X, (float)sunPos.Y, (float)sunPos.Z);
        var sunCol = capi.World.Calendar.SunColor;
        shader.SunColor = new Vec3f(sunCol.R, sunCol.G, sunCol.B);
        shader.AmbientColor = capi.Render.AmbientColor;

        // Indirect lighting tint
        shader.IndirectTint = new Vec3f(
            config.LumOn.IndirectTint[0],
            config.LumOn.IndirectTint[1],
            config.LumOn.IndirectTint[2]);

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    private void BuildHzb(FrameBufferRef primaryFb)
    {
        if (bufferManager.HzbDepthTex is null || bufferManager.HzbFboId == 0)
            return;

        var copy = capi.Shader.GetProgramByName("lumon_hzb_copy") as LumOnHzbCopyShaderProgram;
        var down = capi.Shader.GetProgramByName("lumon_hzb_downsample") as LumOnHzbDownsampleShaderProgram;
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
        var shader = capi.Shader.GetProgramByName("lumon_temporal") as LumOnTemporalShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.TemporalOutputFbo;
        if (fbo is null) return;

        // Bind temporal output FBO (MRT: radiance0, radiance1, meta)
        fbo.BindWithViewport();

        capi.Render.GlToggleBlend(false);

        // Define-backed knobs must be set before Use() so the correct variant is bound.
        shader.EnableReprojectionVelocity = config.LumOn.EnableReprojectionVelocity;

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

        // Bind velocity texture (full resolution)
        // The shader will only use it when EnableReprojectionVelocity is set.
        shader.VelocityTex = bufferManager.VelocityTex!;

        // Pass matrices for reprojection
        shader.ViewMatrix = modelViewMatrix;      // WS to VS for depth calc
        shader.InvViewMatrix = invModelViewMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;

        // Pass probe grid size
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);

        // Pass screen mapping + jitter params (must match anchor pass)
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.FrameIndex = frameIndex;
        shader.AnchorJitterEnabled = config.LumOn.AnchorJitterEnabled ? 1 : 0;
        shader.AnchorJitterScale = config.LumOn.AnchorJitterScale;

        // Pass depth parameters
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Pass temporal parameters
        shader.TemporalAlpha = config.LumOn.TemporalAlpha;
        shader.DepthRejectThreshold = config.LumOn.DepthRejectThreshold;
        shader.NormalRejectThreshold = config.LumOn.NormalRejectThreshold;

        shader.VelocityRejectThreshold = config.LumOn.VelocityRejectThreshold;

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
        var shader = capi.Shader.GetProgramByName("lumon_probe_atlas_temporal") as LumOnScreenProbeAtlasTemporalShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.ScreenProbeAtlasCurrentFbo;
        if (fbo is null) return;

        // Render to current octahedral atlas (which will become history after swap)
        fbo.BindWithViewport();

        capi.Render.GlToggleBlend(false);

        // Define-backed knobs must be set before Use() so the correct variant is bound.
        shader.TexelsPerFrame = config.LumOn.ProbeAtlasTexelsPerFrame;

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

        // Pass temporal blending parameters
        shader.TemporalAlpha = config.LumOn.TemporalAlpha;
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
        if (!config.LumOn.UseProbeAtlas)
        {
            RenderSHGatherPass(primaryFb);
            return;
        }

        if (config.LumOn.ProbeAtlasGather == VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
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
        var shader = capi.Shader.GetProgramByName("lumon_probe_atlas_project_sh9") as LumOnScreenProbeAtlasProjectSh9ShaderProgram;
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
        var shader = capi.Shader.GetProgramByName("lumon_probe_sh9_gather") as LumOnProbeSh9GatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        if (bufferManager.ProbeSh9Tex0 is null || bufferManager.ProbeSh9Tex6 is null)
            return;

        if (TryBindWorldProbeClipmapCommon(
                out _,
                out _,
                out var wpBaseSpacing,
                out var wpLevels,
                out var wpResolution,
                out _,
                out _))
        {
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, wpBaseSpacing, wpLevels, wpResolution))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
        }

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
        shader.InvViewMatrix = invModelViewMatrix;
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.Intensity = config.LumOn.Intensity;
        shader.IndirectTint = config.LumOn.IndirectTint;

        BindWorldProbeClipmap(shader);

        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    /// <summary>
    /// SH-based gather pass (legacy mode).
    /// Evaluates SH coefficients at each pixel's normal direction.
    /// </summary>
    private void RenderSHGatherPass(FrameBufferRef primaryFb)
    {
        var shader = capi.Shader.GetProgramByName("lumon_gather") as LumOnGatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        // World-probe clipmap uses compile-time defines. They must be configured before Use().
        // If defines change, a recompile is queued; skip this pass (do not clear) to avoid black flicker.
        if (TryBindWorldProbeClipmapCommon(
                out _,
                out _,
                out var wpBaseSpacing,
                out var wpLevels,
                out var wpResolution,
                out _,
                out _))
        {
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, wpBaseSpacing, wpLevels, wpResolution))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
        }

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
        shader.InvViewMatrix = invModelViewMatrix;
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DepthDiscontinuityThreshold = config.LumOn.DepthDiscontinuityThreshold;
        shader.Intensity = config.LumOn.Intensity;
        shader.IndirectTint = config.LumOn.IndirectTint;

        // Edge-aware weighting parameters (SPG-007 Section 2.3)
        shader.DepthSigma = config.LumOn.GatherDepthSigma;
        shader.NormalSigma = config.LumOn.GatherNormalSigma;

        BindWorldProbeClipmap(shader);

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
        var shader = capi.Shader.GetProgramByName("lumon_probe_atlas_gather") as LumOnScreenProbeAtlasGatherShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var fbo = bufferManager.IndirectHalfFbo;
        if (fbo is null) return;

        // Prefer filtered atlas (post-temporal) when available.
        var probeAtlas = bufferManager.ScreenProbeAtlasFilteredTex
            ?? bufferManager.ScreenProbeAtlasCurrentTex
            ?? bufferManager.ScreenProbeAtlasTraceTex;
        if (probeAtlas is null) return;

        if (TryBindWorldProbeClipmapCommon(
                out _,
                out _,
                out var wpBaseSpacing,
                out var wpLevels,
                out var wpResolution,
                out _,
                out _))
        {
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, wpBaseSpacing, wpLevels, wpResolution))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
        }

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
        shader.InvViewMatrix = invModelViewMatrix;
        shader.ProbeSpacing = config.LumOn.ProbeSpacingPx;
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.HalfResSize = new Vec2f(bufferManager.HalfResWidth, bufferManager.HalfResHeight);
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.Intensity = config.LumOn.Intensity;
        shader.IndirectTint = config.LumOn.IndirectTint;

        // Probe-atlas gather parameters (from config per Section 2.5)
        shader.LeakThreshold = config.LumOn.ProbeAtlasLeakThreshold;
        shader.SampleStride = config.LumOn.ProbeAtlasSampleStride;

        BindWorldProbeClipmap(shader);

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    private void UpdateWorldProbeClipmap()
    {
        using var scope = Profiler.BeginScope("LumOn.WorldProbe.Update", "LumOn");

        if (worldProbeClipmapBufferManager is null)
        {
            return;
        }

        worldProbeClipmapBufferManager.EnsureResources();
        var resources = worldProbeClipmapBufferManager.Resources;
        var uploader = worldProbeClipmapBufferManager.Uploader;
        if (resources is null || uploader is null)
        {
            return;
        }

        // Lazily create scheduler when topology becomes known.
        if (worldProbeScheduler is null || worldProbeScheduler.LevelCount != resources.Levels || worldProbeScheduler.Resolution != resources.Resolution)
        {
            if (worldProbeScheduler is not null && worldProbeSchedulerAnchorShiftHandler is not null)
            {
                worldProbeScheduler.AnchorShifted -= worldProbeSchedulerAnchorShiftHandler;
            }

            worldProbeScheduler = new LumOnWorldProbeScheduler(resources.Levels, resources.Resolution);

            worldProbeSchedulerAnchorShiftHandler ??= OnWorldProbeSchedulerAnchorShifted;
            worldProbeScheduler.AnchorShifted += worldProbeSchedulerAnchorShiftHandler;
        }

        if (!TryGetCameraPositions(out Vec3d camPosWorld, out Vec3d camPosMatrixSpace))
        {
            return;
        }

        var cfg = config.WorldProbeClipmap;
        double baseSpacing = Math.Max(1e-6, cfg.ClipmapBaseSpacing);

        // Update per-level origins + dirty slabs.
        using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.UpdateOrigins", "LumOn"))
        {
            worldProbeScheduler.UpdateOrigins(camPosWorld, baseSpacing);
        }

        // Apply external invalidations (chunk load / block changes) before selecting work for this frame.
        ApplyPendingWorldProbeDirtyChunks(baseSpacing);

        // Publish runtime params every frame so debug overlays can bind world-probe uniforms
        // even if the main gather shader path skips binding (e.g., due to a queued recompile).
        UpdateWorldProbeClipmapRuntimeParams(resources, camPosWorld, camPosMatrixSpace, baseSpacing);

        // World-space tracing requires the game world to be ready. Even if it's not,
        // we still publish clipmap params so debug views can show bounds/selection.
        //
        // IMPORTANT: tracing runs off-thread.
        worldProbeTraceBlockAccessor ??= capi.World?.BlockAccessor;
        var traceBlockAccessor = worldProbeTraceBlockAccessor;
        if (traceBlockAccessor is null)
        {
            return;
        }

        var mainThreadBlockAccessor = capi.World?.BlockAccessor;
        if (mainThreadBlockAccessor is null)
        {
            return;
        }

        // Lazily create trace service.
        worldProbeTraceScene ??= new BlockAccessorWorldProbeTraceScene(traceBlockAccessor);
        worldProbeTraceService ??= new LumOnWorldProbeTraceService(worldProbeTraceScene, maxQueuedWorkItems: 2048);

        // Build per-frame update list.
        int[] perLevelBudgets = cfg.PerLevelProbeUpdateBudget ?? Array.Empty<int>();
        var requests = new System.Collections.Generic.List<LumOnWorldProbeUpdateRequest>();
        using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.BuildList", "LumOn"))
        {
            requests = worldProbeScheduler.BuildUpdateList(
                frameIndex,
                camPosWorld,
                baseSpacing,
                perLevelBudgets,
                cfg.TraceMaxProbesPerFrame,
                cfg.UploadBudgetBytesPerFrame);
        }

        if (!worldProbeClipmapStartupLogged)
        {
            worldProbeClipmapStartupLogged = true;
            capi.Logger.Notification(
                "[VGE] Phase 18 world-probe clipmap started (levels={0}, res={1}, baseSpacing={2:0.###}, traceMax={3}/frame, uploadBudget={4} B/frame)",
                resources.Levels,
                resources.Resolution,
                baseSpacing,
                cfg.TraceMaxProbesPerFrame,
                cfg.UploadBudgetBytesPerFrame);
        }

        // Debug: publish a small capped set of "queued trace rays" for live preview.
        // This is derived from the requests list (not from per-ray hit results).
        if (config.LumOn.DebugMode == LumOnDebugMode.WorldProbeOrbsPoints)
        {
            PublishWorldProbeQueuedTraceRaysForDebug(resources, baseSpacing, requests);
        }
        else
        {
            worldProbeClipmapBufferManager.ClearDebugTraceRays(frameIndex);
        }

        int requestedCount = requests.Count;
        int enqueuedOk = 0;
        int enqueuedFail = 0;
        int disabledSolid = 0;

        // Enqueue trace work.
        using (Profiler.BeginScope("LumOn.WorldProbe.Trace.Enqueue", "LumOn"))
        {
            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                if (!worldProbeScheduler.TryGetLevelParams(req.Level, out var originMinCorner, out _))
                {
                    worldProbeScheduler.Complete(req, frameIndex, success: false);
                    continue;
                }

                double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, req.Level);
                Vec3d probePosWorldVs = LumOnClipmapTopology.IndexToProbeCenterWorld(req.LocalIndex, originMinCorner, spacing);
                var probePosWorld = new VanillaGraphicsExpanded.Numerics.Vector3d(probePosWorldVs.X, probePosWorldVs.Y, probePosWorldVs.Z);

                if (IsWorldProbeCenterInsideSolidBlock(mainThreadBlockAccessor, probePosWorld))
                {
                    worldProbeScheduler.Disable(req);
                    disabledSolid++;
                    continue;
                }

                // Conservative max distance: one clipmap diameter for this level.
                double maxDist = spacing * resources.Resolution;

                var item = new LumOnWorldProbeTraceWorkItem(frameIndex, req, probePosWorld, maxDist);
                if (!worldProbeTraceService.TryEnqueue(item))
                {
                    // Backpressure: re-mark dirty so we try again next frame.
                    worldProbeScheduler.Complete(req, frameIndex, success: false);
                    enqueuedFail++;
                }
                else
                {
                    enqueuedOk++;
                }
            }
        }

        // Local helper: treat probe centers inside solid blocks as disabled.
        // This avoids spending trace budget on locations that cannot represent empty space lighting.
        bool IsWorldProbeCenterInsideSolidBlock(IBlockAccessor blockAccessor, VanillaGraphicsExpanded.Numerics.Vector3d probePosWorld)
        {
            if (blockAccessor is null) return false;

            try
            {

            var pos = new BlockPos(0);
            pos.Set((int)Math.Floor(probePosWorld.X), (int)Math.Floor(probePosWorld.Y), (int)Math.Floor(probePosWorld.Z));

            // Avoid forcing chunk loads; if it's not loaded, don't permanently disable.
            if (blockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                return false;
            }

            Block b = blockAccessor.GetMostSolidBlock(pos);
            if (b.Id == 0)
            {
                return false;
            }

            Cuboidf[] boxes = b.GetCollisionBoxes(blockAccessor, pos);
            if (boxes is null || boxes.Length == 0)
            {
                return false;
            }

            float lx = (float)(probePosWorld.X - pos.X);
            float ly = (float)(probePosWorld.Y - pos.Y);
            float lz = (float)(probePosWorld.Z - pos.Z);

            const float eps = 1e-4f;
            for (int i = 0; i < boxes.Length; i++)
            {
                var c = boxes[i];
                if (lx >= c.X1 - eps && lx <= c.X2 + eps &&
                    ly >= c.Y1 - eps && ly <= c.Y2 + eps &&
                    lz >= c.Z1 - eps && lz <= c.Z2 + eps)
                {
                    return true;
                }
            }

            return false;

            }
            catch (NotImplementedException)
            {
                // Some accessors may surface placeholder chunk data that doesn't support all queries.
                // Treat this as "unknown/unloaded" so we don't crash and we don't permanently disable the probe.
                return false;
            }
        }

        // Drain completed results.
        using (Profiler.BeginScope("LumOn.WorldProbe.Trace.Drain", "LumOn"))
        {
            worldProbeResults.Clear();
            while (worldProbeTraceService.TryDequeueResult(out var res))
            {
                if (res.Success)
                {
                    worldProbeResults.Add(res);
                    worldProbeScheduler.Complete(res.Request, frameIndex, success: true);
                }
                else
                {
                    bool aborted = res.FailureReason == WorldProbeTraceFailureReason.Aborted;
                    worldProbeScheduler.Complete(res.Request, frameIndex, success: false, aborted);
                }
            }
        }

        // Upload to GPU.
        int uploaded = 0;
        if (worldProbeResults.Count > 0)
        {
            using var uploadScope = Profiler.BeginScope("LumOn.WorldProbe.Upload", "LumOn");
            uploaded = uploader.Upload(resources, worldProbeResults, cfg.UploadBudgetBytesPerFrame);
        }

        if (config.LumOn.DebugMode == LumOnDebugMode.WorldProbeMetaFlagsHeatmap)
        {
            using var heatmapScope = Profiler.BeginScope("LumOn.WorldProbe.DebugHeatmap", "LumOn");
            UpdateWorldProbeDebugHeatmap(resources);
        }
    }

    private static bool IsWorldProbeDebugMode(LumOnDebugMode mode)
    {
        return mode >= LumOnDebugMode.WorldProbeIrradianceCombined && mode <= LumOnDebugMode.WorldProbeRawConfidences;
    }

    private void UpdateWorldProbeClipmapRuntimeParams(
        LumOnWorldProbeClipmapGpuResources resources,
        Vec3d camPosWorld,
        Vec3d camPosMatrixSpace,
        double baseSpacing)
    {
        if (worldProbeClipmapBufferManager is null || worldProbeScheduler is null)
        {
            return;
        }

        int levels = Math.Clamp(resources.Levels, 1, 8);
        int resolution = resources.Resolution;
        float baseSpacingF = (float)Math.Max(1e-6, baseSpacing);

        Span<System.Numerics.Vector3> originsSpan = stackalloc System.Numerics.Vector3[8];
        Span<System.Numerics.Vector3> ringsSpan = stackalloc System.Numerics.Vector3[8];

        for (int i = 0; i < 8; i++)
        {
            if (i < levels && worldProbeScheduler.TryGetLevelParams(i, out var o, out var r))
            {
                // Keep uniforms stable in float precision: store origins relative to the *absolute* camera position.
                originsSpan[i] = new System.Numerics.Vector3(
                    (float)(o.X - camPosWorld.X),
                    (float)(o.Y - camPosWorld.Y),
                    (float)(o.Z - camPosWorld.Z));
                ringsSpan[i] = new System.Numerics.Vector3(r.X, r.Y, r.Z);
            }
            else
            {
                originsSpan[i] = default;
                ringsSpan[i] = default;
            }
        }

        worldProbeClipmapBufferManager.UpdateRuntimeParams(
            camPosWorld,
            new System.Numerics.Vector3((float)camPosMatrixSpace.X, (float)camPosMatrixSpace.Y, (float)camPosMatrixSpace.Z),
            baseSpacingF,
            levels,
            resolution,
            originsSpan,
            ringsSpan);
    }

    private void PublishWorldProbeQueuedTraceRaysForDebug(
        LumOnWorldProbeClipmapGpuResources resources,
        double baseSpacing,
        System.Collections.Generic.List<LumOnWorldProbeUpdateRequest> requests)
    {
        if (worldProbeClipmapBufferManager is null || worldProbeScheduler is null)
        {
            return;
        }

        var dirs = LumOnWorldProbeTraceDirections.GetDirections();
        if (dirs.Length <= 0)
        {
            worldProbeClipmapBufferManager.ClearDebugTraceRays(frameIndex);
            return;
        }

        const int maxPreviewProbes = 3;
        const int maxRaysPerProbe = 64;
        const int maxTotalRays = LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays;

        int probesToShow = Math.Min(maxPreviewProbes, requests.Count);
        int raysPerProbe = Math.Min(maxRaysPerProbe, dirs.Length);
        int dirStride = Math.Max(1, dirs.Length / raysPerProbe);

        var rays = worldProbeDebugQueuedTraceRaysScratch;
        int written = 0;

        for (int i = 0; i < probesToShow; i++)
        {
            var req = requests[i];
            if (!worldProbeScheduler.TryGetLevelParams(req.Level, out var originMinCorner, out _))
            {
                continue;
            }

            double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, req.Level);
            Vec3d probePosWorld = LumOnClipmapTopology.IndexToProbeCenterWorld(req.LocalIndex, originMinCorner, spacing);
            double maxDist = spacing * resources.Resolution;

            int writtenThisProbe = 0;
            for (int d = 0; d < dirs.Length && written < maxTotalRays; d += dirStride)
            {
                var dir = dirs[d];
                var end = new Vec3d(
                    probePosWorld.X + dir.X * maxDist,
                    probePosWorld.Y + dir.Y * maxDist,
                    probePosWorld.Z + dir.Z * maxDist);

                // Color by direction (abs) so it's easy to see the lobe distribution.
                float r = MathF.Abs(dir.X);
                float g = MathF.Abs(dir.Y);
                float b = MathF.Abs(dir.Z);
                rays[written++] = new LumOnWorldProbeClipmapBufferManager.DebugTraceRay(probePosWorld, end, r, g, b, 0.9f);
                writtenThisProbe++;

                // Keep rays per probe capped even if dirStride doesn't divide evenly.
                if (writtenThisProbe >= raysPerProbe)
                {
                    break;
                }
            }
        }

        worldProbeClipmapBufferManager.PublishDebugTraceRays(frameIndex, rays.AsSpan(0, written));
    }

    private void UpdateWorldProbeDebugHeatmap(LumOnWorldProbeClipmapGpuResources resources)
    {
        if (worldProbeScheduler is null)
        {
            return;
        }

        int levels = Math.Clamp(resources.Levels, 1, 8);
        int resolution = resources.Resolution;
        int probesPerLevel = worldProbeScheduler.ProbesPerLevel;

        worldProbeLifecycleScratch ??= new LumOnWorldProbeLifecycleState[probesPerLevel];
        if (worldProbeLifecycleScratch.Length < probesPerLevel)
        {
            worldProbeLifecycleScratch = new LumOnWorldProbeLifecycleState[probesPerLevel];
        }

        int atlasW = resources.AtlasWidth;
        int atlasH = resources.AtlasHeight;
        int texelCount = atlasW * atlasH;

        worldProbeDebugStateTexels ??= new ushort[texelCount * 4];
        if (worldProbeDebugStateTexels.Length != texelCount * 4)
        {
            worldProbeDebugStateTexels = new ushort[texelCount * 4];
        }

        const ushort On = ushort.MaxValue;
        const ushort Off = 0;

        for (int level = 0; level < levels; level++)
        {
            if (!worldProbeScheduler.TryCopyLifecycleStates(level, worldProbeLifecycleScratch))
            {
                continue;
            }

            for (int storageLinear = 0; storageLinear < probesPerLevel; storageLinear++)
            {
                // Decode storage linear index -> storage coord (x,y,z).
                int x = storageLinear % resolution;
                int yz = storageLinear / resolution;
                int y = yz % resolution;
                int z = yz / resolution;

                int u = x + z * resolution;
                int v = y + level * resolution;

                int dst = (v * atlasW + u) * 4;

                ushort r = Off;
                ushort g = Off;
                ushort b = Off;
                ushort a = On;

                switch (worldProbeLifecycleScratch[storageLinear])
                {
                    case LumOnWorldProbeLifecycleState.Valid:
                        b = On;
                        break;
                    case LumOnWorldProbeLifecycleState.Stale:
                        r = On;
                        break;
                    case LumOnWorldProbeLifecycleState.Dirty:
                        r = On;
                        g = On;
                        break;
                    case LumOnWorldProbeLifecycleState.InFlight:
                        g = On;
                        break;
                    case LumOnWorldProbeLifecycleState.Disabled:
                        // Disabled: magenta (R+B)
                        r = On;
                        b = On;
                        break;
                    default:
                        // Uninitialized/unknown -> black.
                        break;
                }

                worldProbeDebugStateTexels[dst + 0] = r;
                worldProbeDebugStateTexels[dst + 1] = g;
                worldProbeDebugStateTexels[dst + 2] = b;
                worldProbeDebugStateTexels[dst + 3] = a;
            }
        }

        resources.UploadDebugState0(worldProbeDebugStateTexels);
    }

    private void BindWorldProbeClipmap(LumOnGatherShaderProgram shader)
    {
        if (!TryBindWorldProbeClipmapCommon(
                out var resources,
                out var camPos,
                out var baseSpacing,
                out var levels,
                out var resolution,
                out var origins,
                out var rings))
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
            return;
        }

        // Clipmap parameters are compile-time defines in the shader include.
        // If they change, VGE will queue a recompile; we must skip binding uniforms this frame.
        if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, baseSpacing, levels, resolution))
        {
            return;
        }

        shader.WorldProbeSH0 = resources.ProbeSh0TextureId;
        shader.WorldProbeSH1 = resources.ProbeSh1TextureId;
        shader.WorldProbeSH2 = resources.ProbeSh2TextureId;
        shader.WorldProbeVis0 = resources.ProbeVis0TextureId;
        shader.WorldProbeMeta0 = resources.ProbeMeta0TextureId;
        shader.WorldProbeSky0 = resources.ProbeSky0TextureId;

        // Shaders reconstruct world positions in the engine's camera-matrix space via invViewMatrix.
        // Use the matching camera position/origins in that same space (do not apply additional camera-relative shifts).
        shader.WorldProbeCameraPosWS = camPos;
        shader.WorldProbeSkyTint = capi.Render.AmbientColor;

        for (int i = 0; i < 8; i++)
        {
            if (!shader.TrySetWorldProbeLevelParams(i, origins[i], rings[i]))
            {
                return;
            }
        }
    }

    private void BindWorldProbeClipmap(LumOnProbeSh9GatherShaderProgram shader)
    {
        if (!TryBindWorldProbeClipmapCommon(
                out var resources,
                out var camPos,
                out var baseSpacing,
                out var levels,
                out var resolution,
                out var origins,
                out var rings))
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
            return;
        }

        if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, baseSpacing, levels, resolution))
        {
            return;
        }

        shader.WorldProbeSH0 = resources.ProbeSh0TextureId;
        shader.WorldProbeSH1 = resources.ProbeSh1TextureId;
        shader.WorldProbeSH2 = resources.ProbeSh2TextureId;
        shader.WorldProbeVis0 = resources.ProbeVis0TextureId;
        shader.WorldProbeMeta0 = resources.ProbeMeta0TextureId;
        shader.WorldProbeSky0 = resources.ProbeSky0TextureId;
        shader.WorldProbeCameraPosWS = camPos;
        shader.WorldProbeSkyTint = capi.Render.AmbientColor;

        for (int i = 0; i < 8; i++)
        {
            if (!shader.TrySetWorldProbeLevelParams(i, origins[i], rings[i]))
            {
                return;
            }
        }
    }

    private void BindWorldProbeClipmap(LumOnScreenProbeAtlasGatherShaderProgram shader)
    {
        if (!TryBindWorldProbeClipmapCommon(
                out var resources,
                out var camPos,
                out var baseSpacing,
                out var levels,
                out var resolution,
                out var origins,
                out var rings))
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
            return;
        }

        if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, baseSpacing, levels, resolution))
        {
            return;
        }

        shader.WorldProbeSH0 = resources.ProbeSh0TextureId;
        shader.WorldProbeSH1 = resources.ProbeSh1TextureId;
        shader.WorldProbeSH2 = resources.ProbeSh2TextureId;
        shader.WorldProbeVis0 = resources.ProbeVis0TextureId;
        shader.WorldProbeMeta0 = resources.ProbeMeta0TextureId;
        shader.WorldProbeSky0 = resources.ProbeSky0TextureId;
        shader.WorldProbeCameraPosWS = camPos;
        shader.WorldProbeSkyTint = capi.Render.AmbientColor;

        for (int i = 0; i < 8; i++)
        {
            if (!shader.TrySetWorldProbeLevelParams(i, origins[i], rings[i]))
            {
                return;
            }
        }
    }

    private bool TryBindWorldProbeClipmapCommon(
        out LumOnWorldProbeClipmapGpuResources resources,
        out Vec3f camPos,
        out float baseSpacing,
        out int levels,
        out int resolution,
        out Vec3f[] origins,
        out Vec3f[] rings)
    {
        origins = worldProbeOriginsCache;
        rings = worldProbeRingsCache;

        resources = null!;
        camPos = new Vec3f();
        baseSpacing = 0;
        levels = 0;
        resolution = 0;

        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return false;
        }

        if (worldProbeScheduler is null)
        {
            return false;
        }

        resources = worldProbeClipmapBufferManager.Resources;
        levels = Math.Clamp(resources.Levels, 1, 8);
        resolution = resources.Resolution;
        baseSpacing = Math.Max(1e-6f, config.WorldProbeClipmap.ClipmapBaseSpacing);
        camPos = new Vec3f(invModelViewMatrix[12], invModelViewMatrix[13], invModelViewMatrix[14]);

        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            return false;
        }

        Vec3d camPosWorld = player.Entity.CameraPos;

        for (int i = 0; i < 8; i++)
        {
            if (i < levels && worldProbeScheduler.TryGetLevelParams(i, out var o, out var r))
            {
                // Origins are stored relative to absolute camera position. Shaders reconstruct:
                // (posWS_matrix - camPosWS_matrix) - originRel  == posAbs - originAbs.
                origins[i] = new Vec3f(
                    (float)(o.X - camPosWorld.X),
                    (float)(o.Y - camPosWorld.Y),
                    (float)(o.Z - camPosWorld.Z));
                rings[i] = new Vec3f(r.X, r.Y, r.Z);
            }
            else
            {
                origins[i] = new Vec3f(0, 0, 0);
                rings[i] = new Vec3f(0, 0, 0);
            }
        }

        // Cache for debug overlays and other passes that can't see the scheduler.
        Span<System.Numerics.Vector3> originsSpan = stackalloc System.Numerics.Vector3[8];
        Span<System.Numerics.Vector3> ringsSpan = stackalloc System.Numerics.Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            var oi = origins[i];
            var ri = rings[i];
            originsSpan[i] = new System.Numerics.Vector3(oi.X, oi.Y, oi.Z);
            ringsSpan[i] = new System.Numerics.Vector3(ri.X, ri.Y, ri.Z);
        }

        worldProbeClipmapBufferManager.UpdateRuntimeParams(
            camPosWorld,
            new System.Numerics.Vector3(camPos.X, camPos.Y, camPos.Z),
            baseSpacing,
            levels,
            resolution,
            originsSpan,
            ringsSpan);

        return true;
    }

    private bool TryGetCameraPositions(out Vec3d cameraPosWorld, out Vec3d cameraPosMatrixSpace)
    {
        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            cameraPosWorld = new Vec3d();
            cameraPosMatrixSpace = new Vec3d();
            return false;
        }

        cameraPosWorld = player.Entity.CameraPos;
        cameraPosMatrixSpace = new Vec3d(invModelViewMatrix[12], invModelViewMatrix[13], invModelViewMatrix[14]);

        return true;
    }

    private void OnWorldProbeSchedulerAnchorShifted(LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent evt)
    {
        worldProbeClipmapBufferManager?.NotifyAnchorShifted(evt);
    }

    /// <summary>
    /// Pass 3.5 (Screen-Probe Atlas mode): Probe-space filtering/denoise.
    /// Operates within each probe's 8x8 octahedral tile with edge-stopping based on hit distance and meta.
    /// Output: Filtered probe atlas to ScreenProbeAtlasFilteredFbo.
    /// </summary>
    private void RenderProbeAtlasFilterPass()
    {
        var shader = capi.Shader.GetProgramByName("lumon_probe_atlas_filter") as LumOnScreenProbeAtlasFilterShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        using var gpuScope = GlGpuProfiler.Instance.Scope("LumOn.ProbeAtlas.Filter");

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
    /// Output is written to IndirectFullFbo for consumption by the final PBR composite.
    /// (Fallback: if IndirectFullFbo is unavailable, additively blend to Primary.)
    /// </summary>
    private void RenderUpsamplePass(FrameBufferRef primaryFb)
    {
        var shader = capi.Shader.GetProgramByName("lumon_upsample") as LumOnUpsampleShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        // Preferred path: write to full-res indirect buffer for the final composite.
        // This keeps the primary scene untouched until PBRCompositeRenderer merges everything.
        var fullResFbo = bufferManager.IndirectFullFbo;
        if (fullResFbo is not null)
        {
            fullResFbo.BindWithViewport();
            fullResFbo.Clear();
            capi.Render.GlToggleBlend(false);
        }
        else
        {
            // Fallback: direct additive blend to screen.
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

        // Matrices for plane-weighted edge-aware filtering (UE-style)
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = modelViewMatrix;

        // Bilateral upsample parameters (SPG-008 Section 3.1)
        shader.UpsampleDepthSigma = config.LumOn.UpsampleDepthSigma;
        shader.UpsampleNormalSigma = config.LumOn.UpsampleNormalSigma;
        shader.UpsampleSpatialSigma = config.LumOn.UpsampleSpatialSigma;

        // Phase 14: bounded hole filling for low-confidence indirect values
        shader.HoleFillRadius = Math.Max(0, config.LumOn.UpsampleHoleFillRadius);
        shader.HoleFillMinConfidence = Math.Clamp(config.LumOn.UpsampleHoleFillMinConfidence, 0f, 1f);

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();

        // Restore blend state
        capi.Render.GlToggleBlend(false);
    }

    // Combine pass removed: final composite is handled by PBRCompositeRenderer.

    #endregion

    #region Matrix Utilities

    // Matrix utilities moved to VanillaGraphicsExpanded.Rendering.MatrixHelper

    #endregion

    #region GPU Profiling

    private void UpdateGpuTimingCountersFromProfiler()
    {
        // Sample resolved GPU profiler values. These typically represent a recent completed frame,
        // matching the intent of the legacy "collect results next frame" approach.
        UpdateCounter("LumOn.HZB", v => debugCounters.HzbPassMs = v);
        UpdateCounter("LumOn.Anchor", v => debugCounters.ProbeAnchorPassMs = v);
        UpdateCounter("LumOn.Trace", v => debugCounters.ProbeTracePassMs = v);
        UpdateCounter("LumOn.Temporal", v => debugCounters.TemporalPassMs = v);
        UpdateCounter("LumOn.AtlasFilter", v => debugCounters.ProbeAtlasFilterPassMs = v);
        UpdateCounter("LumOn.AtlasProjectSH9", v => debugCounters.ProbeAtlasProjectionPassMs = v);
        UpdateCounter("LumOn.Gather", v => debugCounters.GatherPassMs = v);
        UpdateCounter("LumOn.Upsample", v => debugCounters.UpsamplePassMs = v);

        debugCounters.TotalFrameMs =
            debugCounters.HzbPassMs +
            debugCounters.ProbeAnchorPassMs +
            debugCounters.ProbeTracePassMs +
            debugCounters.TemporalPassMs +
            debugCounters.ProbeAtlasFilterPassMs +
            debugCounters.ProbeAtlasProjectionPassMs +
            debugCounters.GatherPassMs +
            debugCounters.UpsamplePassMs;
    }

    private static void UpdateCounter(string name, Action<float> setter)
    {
        if (GlGpuProfiler.Instance.TryGetStats(name, out var stats))
        {
            setter(stats.LastMs);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        pmjJitter.Dispose();
        // Unregister world events
        capi.Event.LeaveWorld -= OnLeaveWorld;

        if (worldProbeScheduler is not null && worldProbeSchedulerAnchorShiftHandler is not null)
        {
            worldProbeScheduler.AnchorShifted -= worldProbeSchedulerAnchorShiftHandler;
        }

        worldProbeTraceService?.Dispose();
        worldProbeTraceService = null;
        worldProbeTraceScene = null;

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }
    }

    #endregion
}
