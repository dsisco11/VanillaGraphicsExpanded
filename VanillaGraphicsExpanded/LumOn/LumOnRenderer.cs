using System;
using System.Collections.Generic;
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

    private readonly LumOnPmjJitterTexture pmjJitter;
    private readonly LumOnUniformBuffers uniformBuffers = new();

    // Fullscreen quad mesh
    private MeshRef? quadMeshRef;

    // Matrix buffers
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];
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

        pmjJitter = LumOnPmjJitterTexture.Create(config.LumOn.PmjJitterCycleLength, config.LumOn.PmjJitterSeed);

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
        capi.ModLoader.GetModSystem<LumOnModSystem>().ToggleLumOnStatsOverlay();
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
        UpdateAndBindFrameUbo(historyValid);
        UpdateAndBindWorldProbeUbo();

        // Generate velocity buffer early so downstream temporal consumers can sample it.
        // This pass is safe even if not yet used by all temporal shaders.
        RenderVelocityPass(primaryFb);

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

            // === Pass 2: Probe Trace (Probe-Atlas) ===
            using var cpuTrace = Profiler.BeginScope("LumOn.Trace", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Trace"))
            {
                RenderProbeAtlasTracePass(primaryFb);
            }

            // === Pass 3: Temporal Accumulation (Probe-Atlas) ===
            using var cpuTemporal = Profiler.BeginScope("LumOn.Temporal", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.Temporal"))
            {
                RenderProbeAtlasTemporalPass();
            }

            // === Pass 3.5: Probe-Atlas Filter/Denoise (Probe-space) ===
            using var cpuAtlasFilter = Profiler.BeginScope("LumOn.AtlasFilter", "Render");
            using (GlGpuProfiler.Instance.Scope("LumOn.AtlasFilter"))
            {
                RenderProbeAtlasFilterPass();
            }

            // === Pass 3.75: Probe-Atlas Projection (Option B) ===
            if (config.LumOn.ProbeAtlasGather == VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
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
        ReadOnlySpan<float> projection = capi.Render.CurrentProjectionMatrix;
        ReadOnlySpan<float> view = capi.Render.CameraMatrixOriginf;

        // Compute inverse matrices
        MatrixHelper.Invert(projection, invProjectionMatrix);
        MatrixHelper.Invert(view, invModelViewMatrix);

        // Compute current view-projection matrix for next frame's reprojection
        MatrixHelper.Multiply(projection, view, currentViewProjMatrix);

        // Compute inverse current view-projection for depth-based reprojection.
        MatrixHelper.Invert(currentViewProjMatrix, invCurrentViewProjMatrix);
    }

    private void UpdateAndBindFrameUbo(bool historyValid)
    {
        Vec3f sunPosF = new(0, 1, 0);
        Vec3f sunColF = new(1, 1, 1);

        if (capi.World?.Calendar is not null)
        {
            var sunPos = capi.World.Calendar.SunPositionNormalized;
            sunPosF = new Vec3f((float)sunPos.X, (float)sunPos.Y, (float)sunPos.Z);

            var sunCol = capi.World.Calendar.SunColor;
            sunColF = new Vec3f(sunCol.R, sunCol.G, sunCol.B);
        }

        uniformBuffers.UpdateFrame(
            invProjectionMatrix: invProjectionMatrix,
            projectionMatrix: capi.Render.CurrentProjectionMatrix,
            viewMatrix: capi.Render.CameraMatrixOriginf,
            invViewMatrix: invModelViewMatrix,
            prevViewProjMatrix: prevViewProjMatrix,
            invCurrViewProjMatrix: invCurrentViewProjMatrix,
            screenWidth: capi.Render.FrameWidth,
            screenHeight: capi.Render.FrameHeight,
            halfResWidth: bufferManager.HalfResWidth,
            halfResHeight: bufferManager.HalfResHeight,
            probeGridWidth: bufferManager.ProbeCountX,
            probeGridHeight: bufferManager.ProbeCountY,
            zNear: capi.Render.ShaderUniforms.ZNear,
            zFar: capi.Render.ShaderUniforms.ZFar,
            probeSpacing: config.LumOn.ProbeSpacingPx,
            frameIndex: frameIndex,
            historyValid: historyValid ? 1 : 0,
            anchorJitterEnabled: config.LumOn.AnchorJitterEnabled ? 1 : 0,
            pmjCycleLength: pmjJitter.CycleLength,
            enableVelocityReprojection: config.LumOn.EnableReprojectionVelocity ? 1 : 0,
            anchorJitterScale: config.LumOn.AnchorJitterScale * 0.1f,
            velocityRejectThreshold: config.LumOn.VelocityRejectThreshold,
            sunPosition: sunPosF,
            sunColor: sunColF,
            ambientColor: capi.Render.AmbientColor);
    }

    private void UpdateAndBindWorldProbeUbo()
    {
        if (TryBindWorldProbeClipmapCommon(
            out _,
            out var camPosWS,
            out _,
            out _,
            out _,
            out var originsWs,
            out var ringsWs))
        {
            uniformBuffers.UpdateWorldProbe(
                skyTint: capi.Render.AmbientColor,
                cameraPosWS: camPosWS,
                originMinCorner: originsWs,
                ringOffset: ringsWs);
        }
        else
        {
            // Publish a stable zeroed buffer so shaders can safely read the block even when disabled.
            uniformBuffers.UpdateWorldProbe(
                skyTint: capi.Render.AmbientColor,
                cameraPosWS: default,
                originMinCorner: default,
                ringOffset: default);
        }

    }

    private void RenderVelocityPass(FrameBufferRef primaryFb)
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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        shader.PrimaryDepth = primaryFb.DepthTextureId;

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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        // Bind G-buffer textures
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        shader.PmjJitter = pmjJitter;

        // Edge detection threshold for depth discontinuity
        shader.DepthDiscontinuityThreshold = config.LumOn.DepthDiscontinuityThreshold;

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

        // World-probe clipmap uses compile-time defines. They must be configured before Use().
        // If defines change, VGE will queue a recompile; skip this pass (do not clear) to avoid black flicker.
        bool hasWorldProbe = TryBindWorldProbeClipmapCommon(
            out var wpResources,
            out _,
            out var wpBaseSpacing,
            out var wpLevels,
            out var wpResolution,
            out _,
            out _);

        if (hasWorldProbe)
        {
            int wpTileSize = config.WorldProbeClipmap.OctahedralTileSize;
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, wpBaseSpacing, wpLevels, wpResolution, wpTileSize))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0, worldProbeOctahedralTileSize: 0);
        }

        shader.Use();
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        // Bind probe anchor textures
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind scene depth for ray marching
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        // Bind radiance sources for hit sampling.
        var direct = DirectLightingBufferManager.Instance;
        if (direct?.IsInitialized == true && direct.DirectDiffuseTex != null && direct.EmissiveTex != null)
        {
            shader.DirectDiffuse = direct.DirectDiffuseTex;
            shader.Emissive = direct.EmissiveTex;
        }
        else
        {
            shader.DirectDiffuse = bufferManager.CapturedSceneTex!;
            shader.Emissive = null;
        }


        // Bind history for temporal preservation
        shader.ScreenProbeAtlasHistory = bufferManager.ScreenProbeAtlasHistoryTex!;
        shader.ScreenProbeAtlasMetaHistory = bufferManager.ScreenProbeAtlasMetaHistoryTex!;

        // HZB depth pyramid (always on)
        if (bufferManager.HzbDepthTex != null)
        {
            shader.HzbDepth = bufferManager.HzbDepthTex;
        }

        if (hasWorldProbe)
        {
            shader.WorldProbeSH0 = wpResources.ProbeSh0;
            shader.WorldProbeSH1 = wpResources.ProbeSh1;
            shader.WorldProbeSH2 = wpResources.ProbeSh2;
            shader.WorldProbeVis0 = wpResources.ProbeVis0;
            shader.WorldProbeMeta0 = wpResources.ProbeMeta0;
            shader.WorldProbeSky0 = wpResources.ProbeSky0;
        }
        else
        {
            shader.WorldProbeSH0 = null;
            shader.WorldProbeSH1 = null;
            shader.WorldProbeSH2 = null;
            shader.WorldProbeVis0 = null;
            shader.WorldProbeMeta0 = null;
            shader.WorldProbeSky0 = null;
        }

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
        if (bufferManager.HzbDepthTex is null || bufferManager.HzbFbo is null || !bufferManager.HzbFbo.IsValid)
            return;

        var copy = capi.Shader.GetProgramByName("lumon_hzb_copy") as LumOnHzbCopyShaderProgram;
        var down = capi.Shader.GetProgramByName("lumon_hzb_downsample") as LumOnHzbDownsampleShaderProgram;
        if (copy is null || down is null || copy.LoadError || down.LoadError)
            return;

        int previousFbo = Rendering.GpuFramebuffer.SaveBinding();

        var hzb = bufferManager.HzbDepthTex;
        var fbo = bufferManager.HzbFbo!;

        capi.Render.GlToggleBlend(false);

        // Copy mip 0 from the primary depth texture.
        fbo.Bind();
        fbo.AttachColorTextureId(hzb.TextureId, attachmentIndex: 0, mipLevel: 0);
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

            fbo.Bind();
            fbo.AttachColorTextureId(hzb.TextureId, attachmentIndex: 0, mipLevel: dstMip);
            GL.Viewport(0, 0, dstW, dstH);

            down.SrcMip = dstMip - 1;
            capi.Render.RenderMesh(quadMeshRef);
        }

        down.Stop();

        Rendering.GpuFramebuffer.RestoreBinding(previousFbo);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);
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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        // Bind trace output (fresh traced texels + history copies for non-traced)
        var traceTex = bufferManager.ScreenProbeAtlasTraceTex;
        if (traceTex is not null)
        {
            shader.ScreenProbeAtlasCurrent = traceTex;
        }

        // Bind history (from previous frame, before swap)
        var historyTex = bufferManager.ScreenProbeAtlasHistoryTex;
        if (historyTex is not null)
        {
            shader.ScreenProbeAtlasHistory = historyTex;
        }

        // Bind probe anchors for validity check
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;

        // Bind meta trace output (pass-through for now)
        shader.ScreenProbeAtlasMetaCurrent = bufferManager.ScreenProbeAtlasMetaTraceTex!;

        // Bind meta history from previous frame
        shader.ScreenProbeAtlasMetaHistory = bufferManager.ScreenProbeAtlasMetaHistoryTex!;

        // Phase 14: bind velocity buffer so temporal can reproject history.
        shader.VelocityTex = bufferManager.VelocityTex;

        // Reconstruct the same jittered probe UV as the probe-anchor pass.
        shader.PmjJitter = pmjJitter;

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
    /// </summary>
    private void RenderGatherPass(FrameBufferRef primaryFb)
    {
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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);
        shader.ScreenProbeAtlas = inputAtlas;
        shader.ScreenProbeAtlasMeta = inputMeta;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;

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

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        shader.ProbeSh0 = bufferManager.ProbeSh9Tex0;
        shader.ProbeSh1 = bufferManager.ProbeSh9Tex1!;
        shader.ProbeSh2 = bufferManager.ProbeSh9Tex2!;
        shader.ProbeSh3 = bufferManager.ProbeSh9Tex3!;
        shader.ProbeSh4 = bufferManager.ProbeSh9Tex4!;
        shader.ProbeSh5 = bufferManager.ProbeSh9Tex5!;
        shader.ProbeSh6 = bufferManager.ProbeSh9Tex6;

        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

        shader.Intensity = config.LumOn.Intensity;
        shader.IndirectTint = config.LumOn.IndirectTint;

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

        fbo.BindWithViewport();
        fbo.Clear();

        capi.Render.GlToggleBlend(false);
        shader.Use();
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        // Bind screen-probe atlas radiance
        shader.ScreenProbeAtlas = probeAtlas;

        // Bind probe anchors
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTex!;

        // Bind G-buffer for pixel info
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;
        shader.Intensity = config.LumOn.Intensity;
        shader.IndirectTint = config.LumOn.IndirectTint;

        // Probe-atlas gather parameters (from config per Section 2.5)
        shader.LeakThreshold = config.LumOn.ProbeAtlasLeakThreshold;
        shader.SampleStride = config.LumOn.ProbeAtlasSampleStride;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    private bool TryBindWorldProbeClipmapCommon(
        out LumOnWorldProbeClipmapGpuResources resources,
        out System.Numerics.Vector3 camPosWS,
        out float baseSpacing,
        out int levels,
        out int resolution,
        out System.Numerics.Vector3[] origins,
        out System.Numerics.Vector3[] rings)
    {
        resources = null!;
        camPosWS = default;
        baseSpacing = 0;
        levels = 0;
        resolution = 0;
        origins = Array.Empty<System.Numerics.Vector3>();
        rings = Array.Empty<System.Numerics.Vector3>();

        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return false;
        }

        // Runtime params are published by LumOnWorldProbeUpdateRenderer (Done stage).
        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out _,
                out camPosWS,
                out baseSpacing,
                out levels,
                out resolution,
                out origins,
                out rings))
        {
            return false;
        }

        resources = worldProbeClipmapBufferManager.Resources;
        return true;
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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        shader.ScreenProbeAtlas = inputAtlas;
        shader.ScreenProbeAtlasMeta = inputMeta;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTex!;

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
        shader.TryBindUniformBlock("LumOnFrameUBO", uniformBuffers.FrameUbo);
        shader.TryBindUniformBlock("LumOnWorldProbeUBO", uniformBuffers.WorldProbeUbo);

        // Bind half-res indirect diffuse
        shader.IndirectHalf = bufferManager.IndirectHalfTex!;

        // Bind G-buffer for edge-aware upsampling
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;

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
        uniformBuffers.Dispose();
        // Unregister world events
        capi.Event.LeaveWorld -= OnLeaveWorld;

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }
    }

    #endregion
}
