using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes;

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

    private const double DEBUG_RENDER_ORDER = 12.0; // After PBR composite (needs depth for wireframes)
    private const int RENDER_RANGE = 1;

    private const int MaxWorldProbeLevels = 8;
    private const int ClipmapBoundsVerticesPerLevel = 48; // Outer clip volume + inner probe-center bounds (2 * 12 edges * 2 vertices)
    private const int FrozenMarkerVertices = 36; // camera axes + L0 center + L0 min + L0 max + L0 first/last probe centers (3*2 each)
    private const int MaxProbePointVertices = 300_000; // Safety cap to avoid pathological allocations (e.g., res>64).

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumOnBufferManager? bufferManager;
    private readonly GBufferManager? gBufferManager;
    private readonly DirectLightingBufferManager? directLightingBufferManager;

    private LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManager;
    private LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManagerEventSource;

    private MeshRef? quadMeshRef;

    private LumOnDebugMode lastMode = LumOnDebugMode.Off;

    private bool hasFrozenClipmapBounds;
    private Vec3d frozenCameraPosWorld = new Vec3d();
    private float frozenBaseSpacing;
    private int frozenLevels;
    private int frozenResolution;
    private readonly Vec3d[] frozenOriginsWorld = new Vec3d[MaxWorldProbeLevels];
    private readonly System.Numerics.Vector3[] frozenOrigins = new System.Numerics.Vector3[MaxWorldProbeLevels];

    // World-probe live debug geometry is built in camera-matrix world space at build time:
    //   originMatBuild = (originAbs - camWorldBuild) + camWSBuild
    // and then translated each frame by:
    //   delta = (camWorldBuild - camWorldNow) + (camWSNow - camWSBuild)
    // so it stays fixed in absolute world space (even as the engine shifts its floating origin).
    private readonly System.Numerics.Vector3[] clipmapDebugOriginsMatBuildF = new System.Numerics.Vector3[MaxWorldProbeLevels];
    private readonly System.Numerics.Vector3[] clipmapDebugOriginsCameraRelNowF = new System.Numerics.Vector3[MaxWorldProbeLevels];

    // World-probe bounds debug line rendering (GL_LINES).
    private readonly LineVertex[] clipmapBoundsVertices = new LineVertex[MaxWorldProbeLevels * ClipmapBoundsVerticesPerLevel + FrozenMarkerVertices];
    private GpuVao? clipmapBoundsVao;
    private GpuVbo? clipmapBoundsVbo;

    // World-probe queued trace rays (GL_LINES).
    private LineVertex[]? clipmapQueuedTraceRayVertices;
    private GpuVao? clipmapQueuedTraceRaysVao;
    private GpuVbo? clipmapQueuedTraceRaysVbo;
    private int clipmapQueuedTraceRayVertexCount;

    // World-probe per-probe rendering (shared positions + per-mode attributes).
    private System.Numerics.Vector3[]? clipmapProbePositions;
    private GpuVbo? clipmapProbePositionsVbo;

    // Probe point cloud (GL_POINTS) attributes.
    private ColorVertex[]? clipmapProbePointColors;
    private GpuVao? clipmapProbePointsVao;
    private GpuVbo? clipmapProbePointsColorVbo;

    // Probe orb impostors (GL_POINTS + point sprite shading) attributes.
    private ColorVertex[]? clipmapProbeOrbColors;
    private UvVertex[]? clipmapProbeOrbAtlasCoords;
    private GpuVao? clipmapProbeOrbsVao;
    private GpuVbo? clipmapProbeOrbsColorVbo;
    private GpuVbo? clipmapProbeOrbsAtlasVbo;
    private int clipmapProbeOrbsCount;

    private bool worldProbeClipmapDebugDirty = true;
    private int clipmapBoundsCount;
    private int clipmapProbePointsCount;
    private float clipmapDebugBaseSpacing;
    private int clipmapDebugLevels;
    private int clipmapDebugResolution;
    private Vec3d clipmapDebugBuildCameraPosWorld = new Vec3d();
    private bool hasClipmapDebugBuildCameraPosWorld;
    private Vec3d clipmapDebugRuntimeCameraPosWorld = new Vec3d();
    private bool hasClipmapDebugRuntimeCameraPosWorld;
    private Vec3d clipmapDebugBuildCameraPosWS = new Vec3d();
    private bool hasClipmapDebugBuildCameraPosWS;
    private Vec3d clipmapDebugRuntimeCameraPosWS = new Vec3d();
    private bool hasClipmapDebugRuntimeCameraPosWS;
    private readonly Vec3d[] clipmapDebugOriginsAbsWorld = new Vec3d[MaxWorldProbeLevels];

    private long lastClipmapDebugLogTick;

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

    internal LumOnDebugRenderer(
        ICoreClientAPI capi,
        VgeConfig config,
        LumOnBufferManager? bufferManager,
        GBufferManager? gBufferManager,
        DirectLightingBufferManager? directLightingBufferManager,
        LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManager)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;
        this.directLightingBufferManager = directLightingBufferManager;
        this.worldProbeClipmapBufferManager = worldProbeClipmapBufferManager;

        // Create fullscreen quad mesh (-1 to 1 in NDC)
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Debug views:
        // - Fullscreen overlays: AfterBlit (always visible).
        // - 3D wireframe bounds: Opaque (so depth-testing works against the scene).
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "lumon_debug");
        capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "lumon_debug_bounds");

        capi.Logger.Notification("[LumOn] Debug renderer initialized");
    }

    internal void SetWorldProbeClipmapBufferManager(LumOnWorldProbeClipmapBufferManager? bufferManager)
    {
        if (worldProbeClipmapBufferManagerEventSource is not null)
        {
            worldProbeClipmapBufferManagerEventSource.AnchorShifted -= OnWorldProbeClipmapAnchorShifted;
            worldProbeClipmapBufferManagerEventSource = null;
        }

        worldProbeClipmapBufferManager = bufferManager;

        if (worldProbeClipmapBufferManager is not null)
        {
            worldProbeClipmapBufferManager.AnchorShifted += OnWorldProbeClipmapAnchorShifted;
            worldProbeClipmapBufferManagerEventSource = worldProbeClipmapBufferManager;
            worldProbeClipmapDebugDirty = true;
        }
    }

    private void OnWorldProbeClipmapAnchorShifted(LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent _)
    {
        worldProbeClipmapDebugDirty = true;
    }

    private void EnsureWorldProbeClipmapManagerBound(string reason)
    {
        if (worldProbeClipmapBufferManager is not null && worldProbeClipmapBufferManager.Resources is not null)
        {
            return;
        }

        var clipmapMs = capi.ModLoader.GetModSystem<ModSystems.WorldProbeModSystem>();
        SetWorldProbeClipmapBufferManager(clipmapMs.EnsureClipmapResources(capi, reason));
    }

    private void EnsureWorldProbeClipmapDebugBuffers()
    {
        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            clipmapBoundsCount = 0;
            clipmapProbePointsCount = 0;
            clipmapProbeOrbsCount = 0;
            worldProbeClipmapDebugDirty = true;
            RateLimitedClipmapDebugLog("World-probe debug buffers: resources missing");
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out var camPosWorld,
                out var camPosWS,
                out float baseSpacing,
                out int levels,
                out int resolution,
                out var origins,
                out var rings))
        {
            clipmapBoundsCount = 0;
            clipmapProbePointsCount = 0;
            clipmapProbeOrbsCount = 0;
            worldProbeClipmapDebugDirty = true;
            RateLimitedClipmapDebugLog("World-probe debug buffers: runtime params missing");
            return;
        }

        clipmapDebugRuntimeCameraPosWorld = camPosWorld;
        hasClipmapDebugRuntimeCameraPosWorld = true;
        clipmapDebugRuntimeCameraPosWS = new Vec3d(camPosWS.X, camPosWS.Y, camPosWS.Z);
        hasClipmapDebugRuntimeCameraPosWS = true;

        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);
        resolution = Math.Max(1, resolution);

        if (MathF.Abs(baseSpacing - clipmapDebugBaseSpacing) > 1e-6f ||
            levels != clipmapDebugLevels ||
            resolution != clipmapDebugResolution)
        {
            worldProbeClipmapDebugDirty = true;
        }

        if (!worldProbeClipmapDebugDirty)
        {
            return;
        }

        clipmapDebugBaseSpacing = baseSpacing;
        clipmapDebugLevels = levels;
        clipmapDebugResolution = resolution;

        // Cache absolute camera position at build time.
        // IMPORTANT: use the same cameraPosWorld that the runtime origins were computed against,
        // otherwise stage-dependent camera sway can cause apparent "swimming" when rotating.
        clipmapDebugBuildCameraPosWorld = camPosWorld;
        hasClipmapDebugBuildCameraPosWorld = true;

        // Cache the camera position in camera-matrix world space (float-origin space).
        // Use the published runtime cameraPosWS so debug geometry is consistent with the world-probe system.
        clipmapDebugBuildCameraPosWS = new Vec3d(camPosWS.X, camPosWS.Y, camPosWS.Z);
        hasClipmapDebugBuildCameraPosWS = true;

        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            if (i < levels)
            {
                // Reconstruct originAbs (double precision) from originRel + cameraPosWorldAbs.
                Vec3d oAbs = hasClipmapDebugBuildCameraPosWorld
                    ? new Vec3d(
                        clipmapDebugBuildCameraPosWorld.X + origins[i].X,
                        clipmapDebugBuildCameraPosWorld.Y + origins[i].Y,
                        clipmapDebugBuildCameraPosWorld.Z + origins[i].Z)
                    : new Vec3d();

                clipmapDebugOriginsAbsWorld[i] = oAbs;

                // Store camera-matrix world space origins at build time:
                //   originMatBuild = originRel + camWSBuild
                // Runtime origins are already originRel = originAbs - camWorldBuild.
                clipmapDebugOriginsMatBuildF[i] = new System.Numerics.Vector3(
                    origins[i].X + camPosWS.X,
                    origins[i].Y + camPosWS.Y,
                    origins[i].Z + camPosWS.Z);
            }
            else
            {
                clipmapDebugOriginsAbsWorld[i] = new Vec3d();
                clipmapDebugOriginsMatBuildF[i] = default;
            }
        }

        EnsureClipmapBoundsLineGlObjects();
        EnsureClipmapProbePointsGlObjects();
        EnsureClipmapProbeOrbsGlObjects();

        if (clipmapBoundsVao is null || !clipmapBoundsVao.IsValid || clipmapBoundsVbo is null || !clipmapBoundsVbo.IsValid)
        {
            clipmapBoundsCount = 0;
            clipmapProbePointsCount = 0;
            clipmapProbeOrbsCount = 0;
            worldProbeClipmapDebugDirty = true;
            RateLimitedClipmapDebugLog("World-probe debug buffers: missing bounds vao/vbo");
            return;
        }

        clipmapBoundsCount = BuildClipmapBoundsVertices(
            baseSpacing: baseSpacing,
            levels: levels,
            resolution: resolution,
            origins: clipmapDebugOriginsMatBuildF,
            frozenCameraMarkerPos: default);

        int lineStride = Marshal.SizeOf<LineVertex>();
        clipmapBoundsVbo.UploadData(clipmapBoundsVertices, clipmapBoundsCount * lineStride);

        int probeCount = ComputeClipmapProbeCount(levels: levels, resolution: resolution);
        EnsureClipmapProbePositionsArray(probeCount);

        clipmapProbePointsCount = 0;
        if (clipmapProbePointsVao is not null
            && clipmapProbePointsVao.IsValid
            && clipmapProbePointsColorVbo is not null
            && clipmapProbePointsColorVbo.IsValid)
        {
            clipmapProbePointsCount = probeCount;
            if (clipmapProbePointsCount > 0)
            {
                BuildClipmapProbePointColors(levels: levels, resolution: resolution);
            }

            if (clipmapProbePointsCount > 0 && clipmapProbePointColors is not null)
            {
                int colorStride = Marshal.SizeOf<ColorVertex>();
                clipmapProbePointsColorVbo.UploadData(clipmapProbePointColors, clipmapProbePointsCount * colorStride);
            }
        }

        clipmapProbeOrbsCount = 0;
        if (clipmapProbeOrbsVao is not null
            && clipmapProbeOrbsVao.IsValid
            && clipmapProbeOrbsColorVbo is not null
            && clipmapProbeOrbsColorVbo.IsValid
            && clipmapProbeOrbsAtlasVbo is not null
            && clipmapProbeOrbsAtlasVbo.IsValid)
        {
            clipmapProbeOrbsCount = probeCount;
            if (clipmapProbeOrbsCount > 0)
            {
                BuildClipmapProbeOrbAttributes(
                    levels: levels,
                    resolution: resolution,
                    rings: rings);
            }

            if (clipmapProbeOrbsCount > 0 && clipmapProbeOrbColors is not null && clipmapProbeOrbAtlasCoords is not null)
            {
                int colorStride = Marshal.SizeOf<ColorVertex>();
                int uvStride = Marshal.SizeOf<UvVertex>();
                clipmapProbeOrbsColorVbo.UploadData(clipmapProbeOrbColors, clipmapProbeOrbsCount * colorStride);
                clipmapProbeOrbsAtlasVbo.UploadData(clipmapProbeOrbAtlasCoords, clipmapProbeOrbsCount * uvStride);
            }
        }

        worldProbeClipmapDebugDirty = false;

        var offsetNow = GetClipmapDebugWorldOffset();
        capi.Logger.Debug(
            "[VGE] World-probe debug rebuild: levels={0} res={1} baseSpacing={2:0.###} boundsVerts={3} probePts={4} probeOrbs={5} camWorld=({6:0.###},{7:0.###},{8:0.###}) camWS(runtime)=({9:0.###},{10:0.###},{11:0.###}) camWS(build)=({12:0.###},{13:0.###},{14:0.###}) offsetNow=({15:0.###},{16:0.###},{17:0.###})",
            levels,
            resolution,
            baseSpacing,
            clipmapBoundsCount,
            clipmapProbePointsCount,
            clipmapProbeOrbsCount,
            camPosWorld.X,
            camPosWorld.Y,
            camPosWorld.Z,
            camPosWS.X,
            camPosWS.Y,
            camPosWS.Z,
            clipmapDebugBuildCameraPosWS.X,
            clipmapDebugBuildCameraPosWS.Y,
            clipmapDebugBuildCameraPosWS.Z,
            offsetNow.X,
            offsetNow.Y,
            offsetNow.Z);

        if (levels > 0)
        {
            var oRel0 = origins[0];
            var oAbs0 = clipmapDebugOriginsAbsWorld[0];
            var oMatF0 = clipmapDebugOriginsMatBuildF[0];
            var camMinusOriginBuild = camPosWorld - oAbs0;

            capi.Logger.Debug(
                "[VGE] World-probe debug L0: originRel=({0:0.###},{1:0.###},{2:0.###}) originAbs=({3:0.###},{4:0.###},{5:0.###}) originMatBuildF=({6:0.###},{7:0.###},{8:0.###}) camWorld-originAbs=({9:0.###},{10:0.###},{11:0.###})",
                oRel0.X, oRel0.Y, oRel0.Z,
                oAbs0.X, oAbs0.Y, oAbs0.Z,
                oMatF0.X, oMatF0.Y, oMatF0.Z,
                camMinusOriginBuild.X, camMinusOriginBuild.Y, camMinusOriginBuild.Z);
        }
    }

    private void RateLimitedClipmapDebugLog(string msg)
    {
        long now = Environment.TickCount64;
        if (now - lastClipmapDebugLogTick < 1500)
        {
            return;
        }

        lastClipmapDebugLogTick = now;
        capi.Logger.Debug("[VGE] {0}", msg);
    }

    private Vec3f GetClipmapDebugWorldOffset()
    {
        if (!hasClipmapDebugBuildCameraPosWorld ||
            !hasClipmapDebugBuildCameraPosWS ||
            !hasClipmapDebugRuntimeCameraPosWorld ||
            !hasClipmapDebugRuntimeCameraPosWS)
        {
            return new Vec3f(0, 0, 0);
        }

        Vec3d camWorldNow = clipmapDebugRuntimeCameraPosWorld;
        Vec3d camWSNow = clipmapDebugRuntimeCameraPosWS;

        // Convert build-time camera-matrix coordinates into current-frame camera-matrix coordinates:
        //   delta = (camWorldBuild - camWorldNow) + (camWSNow - camWSBuild)
        Vec3d d = (clipmapDebugBuildCameraPosWorld - camWorldNow) + (camWSNow - clipmapDebugBuildCameraPosWS);
        return new Vec3f((float)d.X, (float)d.Y, (float)d.Z);
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

    private void UpdateCurrentViewProjMatrixNoTranslate()
    {
        // Bounds vertices are in camera-relative space (originAbs - cameraAbs).
        // Render with a view matrix that has translation removed so we don't apply camera translation twice.
        Array.Copy(capi.Render.CurrentProjectionMatrix, tempProjectionMatrix, 16);
        Array.Copy(capi.Render.CameraMatrixOriginf, tempModelViewMatrix, 16);
        tempModelViewMatrix[12] = 0;
        tempModelViewMatrix[13] = 0;
        tempModelViewMatrix[14] = 0;
        MatrixHelper.Multiply(tempProjectionMatrix, tempModelViewMatrix, currentViewProjMatrix);
    }

    #endregion

    #region IRenderer

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var lum = config.LumOn;
        LumOnDebugMode mode = lum.DebugMode;

        // Render 3D wireframes during the OIT stage so we use the same camera matrices the
        // main scene uses for transparency (avoids post-pass camera sway differences).
        if (stage == EnumRenderStage.OIT)
        {
            if (mode == LumOnDebugMode.WorldProbeOrbsPoints)
            {
                EnsureWorldProbeClipmapManagerBound("LumOnDebugRenderer OIT bind");
                EnsureWorldProbeClipmapDebugBuffers();
                UpdateWorldProbeClipmapDebugVerticesForCurrentCameraOrigin();
                RenderWorldProbeClipmapBoundsLive();
                RenderWorldProbeQueuedTraceRaysLive();
                RenderWorldProbeOrbsPointsLive();
            }

            return;
        }

        if (stage != EnumRenderStage.AfterBlit)
        {
            return;
        }

        if (mode != lastMode)
        {
            OnDebugModeChanged(prev: lastMode, current: mode);
            lastMode = mode;
        }

        // VGE-only debug views that do not rely on lumon_debug.fsh.
        if (mode == LumOnDebugMode.VgeNormalDepthAtlas)
        {
            RenderVgeNormalDepthAtlas();
            return;
        }

        if (mode == LumOnDebugMode.WorldProbeOrbsPoints)
        {
            return;
        }

        // Only render when debug mode is active
        if (mode == LumOnDebugMode.Off || quadMeshRef is null)
            return;

        // Most debug modes require the GBuffer to be ready.
        // Direct lighting debug modes do not.
        if (!IsDirectLightingMode(mode))
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
        if (RequiresLumOnBuffers(mode))
        {
            if (!lum.Enabled || bufferManager is null || !bufferManager.IsInitialized)
                return;
        }

        // Phase 16 direct lighting debug modes rely on the direct lighting MRT outputs.
        if (IsDirectLightingMode(mode))
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
        UpdateCurrentViewProjMatrixNoTranslate();

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        shader.EnablePbrComposite = lum.EnablePbrComposite;
        shader.EnableAO = lum.EnableAO;
        shader.EnableShortRangeAo = lum.EnableShortRangeAo;

        // Phase 18 world-probe defines must be set before Use() as well.
        // Make this robust across initialization order / live reloads: if the renderer's buffer manager reference
        // is stale/missing, re-acquire it from the mod system.
        if (worldProbeClipmapBufferManager is null || worldProbeClipmapBufferManager.Resources is null)
        {
            var clipmapMs = capi.ModLoader.GetModSystem<ModSystems.WorldProbeModSystem>();
            SetWorldProbeClipmapBufferManager(clipmapMs.EnsureClipmapResources(capi, "LumOnDebugRenderer bind"));
        }

        // Keep resources alive for debug modes even if the main renderer is temporarily not running.
        // (Defines are compile-time; we want stable behavior when switching debug modes.)
        worldProbeClipmapBufferManager?.EnsureResources();

        bool hasWorldProbeResources = worldProbeClipmapBufferManager?.Resources is not null;

        // Runtime params are optional; debug overlays can still compile the enabled variant from the known topology.
        // (If runtime params are missing, sampling will likely show no contribution, but it should not force-disable.)
        Vec3d wpCamPosWorld = new Vec3d();
        System.Numerics.Vector3 wpCamPosWS = default;
        float wpBaseSpacing = 0;
        int wpLevels = 0;
        int wpResolution = 0;
        System.Numerics.Vector3[]? wpOrigins = null;
        System.Numerics.Vector3[]? wpRings = null;

        bool hasWorldProbeRuntimeParams = false;
        if (hasWorldProbeResources && worldProbeClipmapBufferManager is not null)
        {
            hasWorldProbeRuntimeParams = worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out wpCamPosWorld,
                out wpCamPosWS,
                out wpBaseSpacing,
                out wpLevels,
                out wpResolution,
                out wpOrigins,
                out wpRings);

            float baseSpacing = Math.Max(1e-6f, config.WorldProbeClipmap.ClipmapBaseSpacing);
            int levels = Math.Clamp(worldProbeClipmapBufferManager.Resources!.Levels, 1, MaxWorldProbeLevels);
            int resolution = Math.Max(1, worldProbeClipmapBufferManager.Resources!.Resolution);

            // If defines changed, a recompile has been queued; skip rendering this frame.
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, baseSpacing, levels, resolution))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
        }

        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
        int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        bool prevScissorTest = GL.IsEnabled(EnableCap.ScissorTest);

        int[] prevViewport = new int[4];
        int[] prevScissorBox = new int[4];
        try
        {
            GL.GetInteger(GetPName.Viewport, prevViewport);
            GL.GetInteger(GetPName.ScissorBox, prevScissorBox);
        }
        catch
        {
            prevViewport[0] = 0;
            prevViewport[1] = 0;
            prevViewport[2] = capi.Render.FrameWidth;
            prevViewport[3] = capi.Render.FrameHeight;

            prevScissorBox[0] = 0;
            prevScissorBox[1] = 0;
            prevScissorBox[2] = capi.Render.FrameWidth;
            prevScissorBox[3] = capi.Render.FrameHeight;
        }

        bool shaderUsed = false;
        try
        {
            // Fullscreen overlays should not disturb global GL state even on early-return paths.
            capi.Render.GLDepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            capi.Render.GlToggleBlend(false);
            GL.Disable(EnableCap.ScissorTest);
            GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

            shader.Use();
            shaderUsed = true;

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
            shader.ProbeAtlasTrace = probeAtlasTrace;

            // Phase 18 world-probe debug inputs (only bound if available + active in the compiled shader).
            if (hasWorldProbeResources && worldProbeClipmapBufferManager?.Resources is not null)
            {
                shader.WorldProbeSH0 = worldProbeClipmapBufferManager.Resources.ProbeSh0TextureId;
                shader.WorldProbeSH1 = worldProbeClipmapBufferManager.Resources.ProbeSh1TextureId;
                shader.WorldProbeSH2 = worldProbeClipmapBufferManager.Resources.ProbeSh2TextureId;
                shader.WorldProbeVis0 = worldProbeClipmapBufferManager.Resources.ProbeVis0TextureId;
                shader.WorldProbeDist0 = worldProbeClipmapBufferManager.Resources.ProbeDist0TextureId;
                shader.WorldProbeMeta0 = worldProbeClipmapBufferManager.Resources.ProbeMeta0TextureId;
                shader.WorldProbeDebugState0 = worldProbeClipmapBufferManager.Resources.ProbeDebugState0TextureId;
                shader.WorldProbeSky0 = worldProbeClipmapBufferManager.Resources.ProbeSky0TextureId;
                shader.WorldProbeSkyTint = capi.Render.AmbientColor;

                // Shaders reconstruct world positions in the engine's camera-matrix world space (invViewMatrix).
                // Always derive camera position from the *current* inverse view matrix (matches reconstruction in shaders).
                // Runtime params may be from a different renderer pass / slightly different matrix state.
                if (!hasWorldProbeRuntimeParams || wpOrigins is null || wpRings is null)
                {
                    shader.WorldProbeCameraPosWS = new Vec3f(invViewMatrix[12], invViewMatrix[13], invViewMatrix[14]);

                    for (int i = 0; i < 8; i++)
                    {
                        shader.TrySetWorldProbeLevelParams(i, new Vec3f(0, 0, 0), new Vec3f(0, 0, 0));
                    }
                }
                else
                {
                    shader.WorldProbeCameraPosWS = new Vec3f(invViewMatrix[12], invViewMatrix[13], invViewMatrix[14]);

                    for (int i = 0; i < 8; i++)
                    {
                        var o = wpOrigins[i];
                        var r = wpRings[i];
                        if (!shader.TrySetWorldProbeLevelParams(
                                i,
                                new Vec3f(o.X, o.Y, o.Z),
                                new Vec3f(r.X, r.Y, r.Z)))
                        {
                            break;
                        }
                    }
                }
            }

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
            shader.ProbeSpacing = lum.ProbeSpacingPx;
            shader.ZNear = capi.Render.ShaderUniforms.ZNear;
            shader.ZFar = capi.Render.ShaderUniforms.ZFar;
            shader.DebugMode = (int)mode;
            shader.InvProjectionMatrix = invProjectionMatrix;
            shader.InvViewMatrix = invViewMatrix;
            shader.PrevViewProjMatrix = prevViewProjMatrix;
            shader.TemporalAlpha = lum.TemporalAlpha;
            shader.DepthRejectThreshold = lum.DepthRejectThreshold;
            shader.NormalRejectThreshold = lum.NormalRejectThreshold;
            shader.VelocityRejectThreshold = lum.VelocityRejectThreshold;

            // Phase 15 composite params (now compile-time defines)
            shader.IndirectIntensity = lum.Intensity;
            shader.IndirectTint = new Vec3f(lum.IndirectTint[0], lum.IndirectTint[1], lum.IndirectTint[2]);
            shader.DiffuseAOStrength = Math.Clamp(lum.DiffuseAOStrength, 0f, 1f);
            shader.SpecularAOStrength = Math.Clamp(lum.SpecularAOStrength, 0f, 1f);

            // Render fullscreen quad
            using var cpuScope = Profiler.BeginScope("Debug.LumOn", "Render");
            using (GlGpuProfiler.Instance.Scope("Debug.LumOn"))
            {
                capi.Render.RenderMesh(quadMeshRef);
            }
        }
        finally
        {
            if (shaderUsed)
            {
                shader.Stop();
            }

            if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            capi.Render.GLDepthMask(prevDepthMask);
            capi.Render.GlToggleBlend(prevBlend);
            GL.ActiveTexture((TextureUnit)prevActiveTexture);

            if (prevScissorTest) GL.Enable(EnableCap.ScissorTest);
            else GL.Disable(EnableCap.ScissorTest);

            try
            {
                GL.Scissor(prevScissorBox[0], prevScissorBox[1], prevScissorBox[2], prevScissorBox[3]);
                GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
            }
            catch
            {
                // ignore restore failures
            }
        }

        // Store current matrix for next frame's reprojection
        StorePrevViewProjMatrix();
    }

    private void OnDebugModeChanged(LumOnDebugMode prev, LumOnDebugMode current)
    {
        if (current is >= LumOnDebugMode.WorldProbeIrradianceCombined and <= LumOnDebugMode.WorldProbeOrbsPoints
            || current == LumOnDebugMode.WorldProbeRawConfidences
            || current == LumOnDebugMode.WorldProbeContributionOnly
            || current == LumOnDebugMode.ScreenSpaceContributionOnly)
        {
            worldProbeClipmapDebugDirty = true;

            if (worldProbeClipmapBufferManager is null)
            {
                capi.Logger.Debug("[VGE] World-probe clipmap manager: null (debug renderer)");
            }
            else
            {
                capi.Logger.Debug($"[VGE] World-probe clipmap manager: resources={(worldProbeClipmapBufferManager.Resources is null ? "null" : "ok")}");
            }

            if (worldProbeClipmapBufferManager?.Resources is null)
            {
                capi.Logger.Debug("[VGE] World-probe clipmap resources not available yet.");
            }
            else if (worldProbeClipmapBufferManager.TryGetRuntimeParams(out _, out var camPos, out var baseSpacing, out var levels, out var resolution, out _, out _))
            {
                capi.Logger.Debug($"[VGE] World-probe clipmap params: levels={levels}, res={resolution}, baseSpacing={baseSpacing:0.###}, cam=({camPos.X:0.#},{camPos.Y:0.#},{camPos.Z:0.#})");
            }
            else
            {
                capi.Logger.Debug("[VGE] World-probe clipmap params not published yet (runtime params missing).");
            }

            if (worldProbeClipmapBufferManager is not null && worldProbeClipmapBufferManager.TryGetRuntimeParams(
                    out _,
                    out _,
                    out float baseSpacingL0,
                    out _,
                    out int resolutionL0,
                    out var origins,
                    out var rings))
            {
                var o0 = (origins.Length > 0) ? origins[0] : default;
                var r0 = (rings.Length > 0) ? rings[0] : default;

                float denom = Math.Max(1e-6f, baseSpacingL0);
                float camLocalX = -o0.X / denom;
                float camLocalY = -o0.Y / denom;
                float camLocalZ = -o0.Z / denom;
                float expectedMin = resolutionL0 * 0.5f;
                float expectedMax = expectedMin + 1f;

                capi.Logger.Debug(
                    $"[VGE] World-probe L0: originRel=({o0.X:0.###},{o0.Y:0.###},{o0.Z:0.###}), ring=({r0.X},{r0.Y},{r0.Z}), camLocal=({camLocalX:0.##},{camLocalY:0.##},{camLocalZ:0.##}) expected≈[{expectedMin:0.##},{expectedMax:0.##})");
            }
        }

        // Clipmap bounds overlay is live now; keep frozen capture code around for future use.
    }

    private void CaptureFrozenClipmapBounds()
    {
        hasFrozenClipmapBounds = false;

        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out var camPosWorld,
                out _,
                out frozenBaseSpacing,
                out frozenLevels,
                out frozenResolution,
                out var origins,
                out _))
        {
            return;
        }

        // Store the frozen capture in absolute world space so it stays fixed even when the engine's
        // camera-matrix origin shifts (floating origin).
        frozenCameraPosWorld = camPosWorld;

        frozenLevels = Math.Clamp(frozenLevels, 1, MaxWorldProbeLevels);
        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            // Runtime origins are stored relative to the absolute camera position.
            // Recover absolute world origins at capture time:
            //   originAbs = originRel + cameraAbs
            var oRel = (i < origins.Length) ? origins[i] : default;
            frozenOriginsWorld[i] = new Vec3d(
                frozenCameraPosWorld.X + oRel.X,
                frozenCameraPosWorld.Y + oRel.Y,
                frozenCameraPosWorld.Z + oRel.Z);
        }

        hasFrozenClipmapBounds = true;

        capi.Logger.Notification(
            "[VGE] Frozen world-probe clipmap bounds captured (camera={0:0.0},{1:0.0},{2:0.0}; L0 size={3:0.0}m; levels={4})",
            frozenCameraPosWorld.X, frozenCameraPosWorld.Y, frozenCameraPosWorld.Z,
            frozenBaseSpacing * frozenResolution,
            frozenLevels);

        // Extra diagnostics to catch origin/extent sign mistakes.
        // If probe centers appear to start at the max-corner, these numbers will make it unambiguous.
        if (frozenLevels > 0 && frozenResolution > 0)
        {
            var o0 = frozenOriginsWorld[0];
            double spacing0 = frozenBaseSpacing;
            double size0 = spacing0 * frozenResolution;
            var max0 = new Vec3d(o0.X + size0, o0.Y + size0, o0.Z + size0);
            var firstCenter0 = new Vec3d(o0.X + 0.5 * spacing0, o0.Y + 0.5 * spacing0, o0.Z + 0.5 * spacing0);
            var lastCenter0 = new Vec3d(o0.X + (frozenResolution - 0.5) * spacing0, o0.Y + (frozenResolution - 0.5) * spacing0, o0.Z + (frozenResolution - 0.5) * spacing0);

            capi.Logger.Debug(
                "[VGE] Frozen world-probe L0: originMin=({0:0.###},{1:0.###},{2:0.###}) max=({3:0.###},{4:0.###},{5:0.###}) firstCenter=({6:0.###},{7:0.###},{8:0.###}) lastCenter=({9:0.###},{10:0.###},{11:0.###})",
                o0.X, o0.Y, o0.Z,
                max0.X, max0.Y, max0.Z,
                firstCenter0.X, firstCenter0.Y, firstCenter0.Z,
                lastCenter0.X, lastCenter0.Y, lastCenter0.Z);
        }
    }

    private void RenderWorldProbeClipmapBoundsFrozen()
    {
        if (!hasFrozenClipmapBounds)
        {
            // Try once per frame until data becomes available (e.g. switching modes before Phase 18 has produced params).
            CaptureFrozenClipmapBounds();
            if (!hasFrozenClipmapBounds)
            {
                return;
            }
        }

        var shader = capi.Shader.GetProgramByName("vge_debug_lines") as VgeDebugLinesShaderProgram;
        if (shader is null || shader.LoadError)
        {
            return;
        }

        EnsureClipmapBoundsLineGlObjects();
        if (clipmapBoundsVao is null || !clipmapBoundsVao.IsValid || clipmapBoundsVbo is null || !clipmapBoundsVbo.IsValid)
        {
            return;
        }

        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            return;
        }

        // Convert the frozen absolute world-space bounds into the current camera-relative space every frame,
        // so they appear fixed in world space even when the engine re-centers the floating origin.
        Vec3d camPosWorldNow = player.Entity.CameraPos;

        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            if (i < frozenLevels)
            {
                Vec3d oAbs = frozenOriginsWorld[i];
                frozenOrigins[i] = new System.Numerics.Vector3(
                    (float)(oAbs.X - camPosWorldNow.X),
                    (float)(oAbs.Y - camPosWorldNow.Y),
                    (float)(oAbs.Z - camPosWorldNow.Z));
            }
            else
            {
                frozenOrigins[i] = default;
            }
        }

        var frozenCameraMarkerPos = new System.Numerics.Vector3(
            (float)(frozenCameraPosWorld.X - camPosWorldNow.X),
            (float)(frozenCameraPosWorld.Y - camPosWorldNow.Y),
            (float)(frozenCameraPosWorld.Z - camPosWorldNow.Z));

        UpdateCurrentViewProjMatrixNoTranslate();

        int vertexCount = BuildClipmapBoundsVertices(
            baseSpacing: frozenBaseSpacing,
            levels: frozenLevels,
            resolution: frozenResolution,
            origins: frozenOrigins,
            frozenCameraMarkerPos: frozenCameraMarkerPos);

        if (vertexCount <= 0)
        {
            StorePrevViewProjMatrix();
            return;
        }

        using var cpuScope = Profiler.BeginScope("Debug.WorldProbeClipmapBounds", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.WorldProbeClipmapBounds"))
        {
            bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool prevBlend = GL.IsEnabled(EnableCap.Blend);
            bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            int prevDepthFunc = GL.GetInteger(GetPName.DepthFunc);

            bool shaderUsed = false;
            try
            {
                // Depth-test so the bounds don't draw over all geometry.
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Lequal);
                capi.Render.GlToggleBlend(false);
                capi.Render.GLDepthMask(false);

                shader.Use();
                shaderUsed = true;
                shader.ModelViewProjectionMatrix = currentViewProjMatrix;
                shader.WorldOffset = GetClipmapDebugWorldOffset();

                int stride = Marshal.SizeOf<LineVertex>();
                clipmapBoundsVbo.UploadData(clipmapBoundsVertices, vertexCount * stride);

                clipmapBoundsVao.Bind();
                GL.LineWidth(2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
                GL.LineWidth(1f);

                GL.BindVertexArray(0);
            }
            finally
            {
                if (shaderUsed)
                {
                    shader.Stop();
                }

                if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
                else GL.Disable(EnableCap.DepthTest);

                GL.DepthFunc((DepthFunction)prevDepthFunc);
                capi.Render.GLDepthMask(prevDepthMask);
                capi.Render.GlToggleBlend(prevBlend);
                GL.ActiveTexture((TextureUnit)prevActiveTexture);
            }
        }

        StorePrevViewProjMatrix();
    }

    private void RenderWorldProbeClipmapBoundsLive()
    {
        // Live bounds overlay so we can compare bounds + probe debug visualizations in the same frame.
        if (worldProbeClipmapBufferManager?.Resources is null || clipmapBoundsCount <= 0)
        {
            if (worldProbeClipmapBufferManager?.Resources is not null && clipmapBoundsCount <= 0)
            {
                RateLimitedClipmapDebugLog("World-probe bounds: clipmapBoundsCount=0");
            }
            return;
        }

        var shader = capi.Shader.GetProgramByName("vge_debug_lines") as VgeDebugLinesShaderProgram;
        if (shader is null || shader.LoadError)
        {
            RateLimitedClipmapDebugLog("World-probe bounds: shader missing/load error");
            return;
        }

        if (clipmapBoundsVao is null || !clipmapBoundsVao.IsValid || clipmapBoundsVbo is null || !clipmapBoundsVbo.IsValid)
        {
            RateLimitedClipmapDebugLog("World-probe bounds: missing vao/vbo");
            return;
        }

        // Vertices are rebuilt every frame in camera-relative space (world - renderCameraOrigin).
        UpdateCurrentViewProjMatrixNoTranslate();

        using var cpuScope = Profiler.BeginScope("Debug.WorldProbeClipmapBoundsLive", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.WorldProbeClipmapBoundsLive"))
        {
            bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool prevBlend = GL.IsEnabled(EnableCap.Blend);
            bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            int prevDepthFunc = GL.GetInteger(GetPName.DepthFunc);
            float prevPointSize = GL.GetFloat(GetPName.PointSize);
            int prevBlendSrcRgb = GL.GetInteger(GetPName.BlendSrcRgb);
            int prevBlendDstRgb = GL.GetInteger(GetPName.BlendDstRgb);
            int prevBlendSrcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha);
            int prevBlendDstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);

            bool shaderUsed = false;
            try
            {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Lequal);
                capi.Render.GlToggleBlend(false);
                capi.Render.GLDepthMask(false);

                shader.Use();
                shaderUsed = true;
                shader.ModelViewProjectionMatrix = currentViewProjMatrix;
                shader.WorldOffset = new Vec3f(0, 0, 0);

                clipmapBoundsVao.Bind();

                GL.LineWidth(2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, clipmapBoundsCount);
                GL.LineWidth(1f);

                GL.BindVertexArray(0);

                if (clipmapProbePointsCount > 0 && clipmapProbePointsVao is not null && clipmapProbePointsVao.IsValid)
                {
                    GL.PointSize(3.5f);

                    clipmapProbePointsVao.Bind();
                    GL.DrawArrays(PrimitiveType.Points, 0, clipmapProbePointsCount);
                    GL.BindVertexArray(0);
                }
            }
            finally
            {
                if (shaderUsed)
                {
                    shader.Stop();
                }

                if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
                else GL.Disable(EnableCap.DepthTest);

                GL.DepthFunc((DepthFunction)prevDepthFunc);
                GL.PointSize(prevPointSize);
                GL.BlendFuncSeparate(
                    (BlendingFactorSrc)prevBlendSrcRgb,
                    (BlendingFactorDest)prevBlendDstRgb,
                    (BlendingFactorSrc)prevBlendSrcAlpha,
                    (BlendingFactorDest)prevBlendDstAlpha);
                capi.Render.GLDepthMask(prevDepthMask);
                capi.Render.GlToggleBlend(prevBlend);
                GL.ActiveTexture((TextureUnit)prevActiveTexture);
            }
        }

        var err = GL.GetError();
        if (err != ErrorCode.NoError)
        {
            RateLimitedClipmapDebugLog($"World-probe bounds: GL error {err}");
        }
    }

    private void RenderWorldProbeQueuedTraceRaysLive()
    {
        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetDebugTraceRays(out var rays, out int rayCount, out _))
        {
            clipmapQueuedTraceRayVertexCount = 0;
            return;
        }

        if (rayCount <= 0)
        {
            clipmapQueuedTraceRayVertexCount = 0;
            return;
        }

        if (!TryGetRenderCameraWorldOrigin(out var camWorld))
        {
            return;
        }

        var shader = capi.Shader.GetProgramByName("vge_debug_lines") as VgeDebugLinesShaderProgram;
        if (shader is null || shader.LoadError)
        {
            return;
        }

        EnsureClipmapQueuedTraceRaysGlObjects();
        if (clipmapQueuedTraceRaysVao is null || !clipmapQueuedTraceRaysVao.IsValid || clipmapQueuedTraceRaysVbo is null || !clipmapQueuedTraceRaysVbo.IsValid)
        {
            return;
        }

        int neededVerts = Math.Min(rayCount, LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays) * 2;
        clipmapQueuedTraceRayVertices ??= Array.Empty<LineVertex>();
        if (clipmapQueuedTraceRayVertices.Length < neededVerts)
        {
            clipmapQueuedTraceRayVertices = new LineVertex[neededVerts];
        }

        for (int i = 0; i < rayCount && (i * 2 + 1) < clipmapQueuedTraceRayVertices.Length; i++)
        {
            var r = rays[i];
            float sx = (float)(r.StartWorld.X - camWorld.X);
            float sy = (float)(r.StartWorld.Y - camWorld.Y);
            float sz = (float)(r.StartWorld.Z - camWorld.Z);
            float ex = (float)(r.EndWorld.X - camWorld.X);
            float ey = (float)(r.EndWorld.Y - camWorld.Y);
            float ez = (float)(r.EndWorld.Z - camWorld.Z);

            int vi = i * 2;
            clipmapQueuedTraceRayVertices[vi] = new LineVertex { X = sx, Y = sy, Z = sz, R = r.R, G = r.G, B = r.B, A = r.A };
            clipmapQueuedTraceRayVertices[vi + 1] = new LineVertex { X = ex, Y = ey, Z = ez, R = r.R, G = r.G, B = r.B, A = r.A };
        }

        clipmapQueuedTraceRayVertexCount = neededVerts;

        int stride = Marshal.SizeOf<LineVertex>();
        clipmapQueuedTraceRaysVbo.UploadData(clipmapQueuedTraceRayVertices, clipmapQueuedTraceRayVertexCount * stride);

        UpdateCurrentViewProjMatrixNoTranslate();

        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
        int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        int prevDepthFunc = GL.GetInteger(GetPName.DepthFunc);

        bool shaderUsed = false;
        try
        {
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            capi.Render.GlToggleBlend(false);
            capi.Render.GLDepthMask(false);

            shader.Use();
            shaderUsed = true;
            shader.ModelViewProjectionMatrix = currentViewProjMatrix;
            shader.WorldOffset = new Vec3f(0, 0, 0);

            clipmapQueuedTraceRaysVao.Bind();
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, clipmapQueuedTraceRayVertexCount);
            GL.LineWidth(1f);
            GL.BindVertexArray(0);
        }
        finally
        {
            if (shaderUsed) shader.Stop();

            if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc((DepthFunction)prevDepthFunc);
            capi.Render.GLDepthMask(prevDepthMask);
            capi.Render.GlToggleBlend(prevBlend);
            GL.ActiveTexture((TextureUnit)prevActiveTexture);
        }
    }

    private void EnsureClipmapQueuedTraceRaysGlObjects()
    {
        if (clipmapQueuedTraceRaysVao is not null
            && clipmapQueuedTraceRaysVao.IsValid
            && clipmapQueuedTraceRaysVbo is not null
            && clipmapQueuedTraceRaysVbo.IsValid)
        {
            return;
        }

        clipmapQueuedTraceRaysVao?.Dispose();
        clipmapQueuedTraceRaysVbo?.Dispose();
        clipmapQueuedTraceRaysVao = null;
        clipmapQueuedTraceRaysVbo = null;

        try
        {
            clipmapQueuedTraceRaysVao = GpuVao.Create("VGE_WorldProbeQueuedTraceRays_VAO");
            clipmapQueuedTraceRaysVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeQueuedTraceRays_VBO");

            using var vaoScope = clipmapQueuedTraceRaysVao.BindScope();
            using var vboScope = clipmapQueuedTraceRaysVbo.BindScope();

            int stride = Marshal.SizeOf<LineVertex>();

            // vec3 position
            clipmapQueuedTraceRaysVao.AttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec4 color
            clipmapQueuedTraceRaysVao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 12);
        }
        catch
        {
            clipmapQueuedTraceRaysVao?.Dispose();
            clipmapQueuedTraceRaysVbo?.Dispose();
            clipmapQueuedTraceRaysVao = null;
            clipmapQueuedTraceRaysVbo = null;
        }
    }

    private void EnsureClipmapBoundsLineGlObjects()
    {
        if (clipmapBoundsVao is not null
            && clipmapBoundsVao.IsValid
            && clipmapBoundsVbo is not null
            && clipmapBoundsVbo.IsValid)
        {
            return;
        }

        clipmapBoundsVao?.Dispose();
        clipmapBoundsVbo?.Dispose();
        clipmapBoundsVao = null;
        clipmapBoundsVbo = null;

        try
        {
            clipmapBoundsVao = GpuVao.Create("VGE_WorldProbeClipmapBoundsLines_VAO");
            clipmapBoundsVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeClipmapBoundsLines_VBO");

            using var vaoScope = clipmapBoundsVao.BindScope();
            using var vboScope = clipmapBoundsVbo.BindScope();

            int stride = Marshal.SizeOf<LineVertex>();

            // vec3 position
            clipmapBoundsVao.AttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec4 color
            clipmapBoundsVao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 12);
        }
        catch
        {
            // Best-effort only; fall back to no-op if GL objects can't be created.
            clipmapBoundsVao?.Dispose();
            clipmapBoundsVbo?.Dispose();
            clipmapBoundsVao = null;
            clipmapBoundsVbo = null;
        }
    }

    private void EnsureClipmapProbePointsGlObjects()
    {
        if (clipmapProbePointsVao is not null
            && clipmapProbePointsVao.IsValid
            && clipmapProbePointsColorVbo is not null
            && clipmapProbePointsColorVbo.IsValid)
        {
            return;
        }

        EnsureClipmapProbePositionsGlObjects();
        if (clipmapProbePositionsVbo is null || !clipmapProbePositionsVbo.IsValid)
        {
            return;
        }

        clipmapProbePointsVao?.Dispose();
        clipmapProbePointsColorVbo?.Dispose();
        clipmapProbePointsVao = null;
        clipmapProbePointsColorVbo = null;

        try
        {
            clipmapProbePointsVao = GpuVao.Create("VGE_WorldProbeClipmapProbePoints_VAO");
            clipmapProbePointsColorVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeClipmapProbePoints_Color_VBO");

            using var vaoScope = clipmapProbePointsVao.BindScope();

            int posStride = Marshal.SizeOf<System.Numerics.Vector3>();
            using (clipmapProbePositionsVbo.BindScope())
            {
                clipmapProbePointsVao.AttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, posStride, 0);
            }

            int colorStride = Marshal.SizeOf<ColorVertex>();
            using (clipmapProbePointsColorVbo.BindScope())
            {
                clipmapProbePointsVao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, colorStride, 0);
            }
        }
        catch
        {
            // Best-effort only; fall back to no-op if GL objects can't be created.
            clipmapProbePointsVao?.Dispose();
            clipmapProbePointsColorVbo?.Dispose();
            clipmapProbePointsVao = null;
            clipmapProbePointsColorVbo = null;
        }
    }

    private void EnsureClipmapProbePositionsGlObjects()
    {
        if (clipmapProbePositionsVbo is not null && clipmapProbePositionsVbo.IsValid)
        {
            return;
        }

        clipmapProbePositionsVbo?.Dispose();
        clipmapProbePositionsVbo = null;

        try
        {
            clipmapProbePositionsVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeClipmapProbePositions_VBO");
        }
        catch
        {
            clipmapProbePositionsVbo?.Dispose();
            clipmapProbePositionsVbo = null;
        }
    }

    private static int ComputeClipmapProbeCount(int levels, int resolution)
    {
        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);
        resolution = Math.Max(1, resolution);

        long requested = (long)levels * resolution * resolution * resolution;
        if (requested <= 0 || requested > MaxProbePointVertices)
        {
            return 0;
        }

        return (int)requested;
    }

    private void EnsureClipmapProbePositionsArray(int count)
    {
        if (count <= 0)
        {
            return;
        }

        clipmapProbePositions ??= Array.Empty<System.Numerics.Vector3>();
        if (clipmapProbePositions.Length < count)
        {
            clipmapProbePositions = new System.Numerics.Vector3[count];
        }
    }

    private void BuildClipmapProbePointColors(int levels, int resolution)
    {
        int count = ComputeClipmapProbeCount(levels: levels, resolution: resolution);
        if (count <= 0)
        {
            clipmapProbePointColors = null;
            return;
        }

        clipmapProbePointColors ??= Array.Empty<ColorVertex>();
        if (clipmapProbePointColors.Length < count)
        {
            clipmapProbePointColors = new ColorVertex[count];
        }

        int written = 0;
        for (int level = 0; level < levels; level++)
        {
            (float r, float g, float b, float a) = GetDebugColorForLevel(level);
            (r, g, b, a) = (r * 0.85f, g * 0.85f, b * 0.85f, 1f);

            int levelCount = resolution * resolution * resolution;
            for (int i = 0; i < levelCount; i++)
            {
                if (written >= count)
                {
                    return;
                }

                clipmapProbePointColors[written++] = new ColorVertex { R = r, G = g, B = b, A = a };
            }
        }
    }

    private void EnsureClipmapProbeOrbsGlObjects()
    {
        if (clipmapProbeOrbsVao is not null
            && clipmapProbeOrbsVao.IsValid
            && clipmapProbeOrbsColorVbo is not null
            && clipmapProbeOrbsColorVbo.IsValid
            && clipmapProbeOrbsAtlasVbo is not null
            && clipmapProbeOrbsAtlasVbo.IsValid)
        {
            return;
        }

        EnsureClipmapProbePositionsGlObjects();
        if (clipmapProbePositionsVbo is null || !clipmapProbePositionsVbo.IsValid)
        {
            return;
        }

        clipmapProbeOrbsVao?.Dispose();
        clipmapProbeOrbsColorVbo?.Dispose();
        clipmapProbeOrbsAtlasVbo?.Dispose();
        clipmapProbeOrbsVao = null;
        clipmapProbeOrbsColorVbo = null;
        clipmapProbeOrbsAtlasVbo = null;

        try
        {
            clipmapProbeOrbsVao = GpuVao.Create("VGE_WorldProbeClipmapProbeOrbs_VAO");
            clipmapProbeOrbsColorVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeClipmapProbeOrbs_Color_VBO");
            clipmapProbeOrbsAtlasVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldProbeClipmapProbeOrbs_Atlas_VBO");

            using var vaoScope = clipmapProbeOrbsVao.BindScope();

            int posStride = Marshal.SizeOf<System.Numerics.Vector3>();
            using (clipmapProbePositionsVbo.BindScope())
            {
                clipmapProbeOrbsVao.AttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, posStride, 0);
            }

            int colorStride = Marshal.SizeOf<ColorVertex>();
            using (clipmapProbeOrbsColorVbo.BindScope())
            {
                clipmapProbeOrbsVao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, colorStride, 0);
            }

            int uvStride = Marshal.SizeOf<UvVertex>();
            using (clipmapProbeOrbsAtlasVbo.BindScope())
            {
                clipmapProbeOrbsVao.AttribPointer(2, 2, VertexAttribPointerType.Float, normalized: false, uvStride, 0);
            }
        }
        catch
        {
            clipmapProbeOrbsVao?.Dispose();
            clipmapProbeOrbsColorVbo?.Dispose();
            clipmapProbeOrbsAtlasVbo?.Dispose();
            clipmapProbeOrbsVao = null;
            clipmapProbeOrbsColorVbo = null;
            clipmapProbeOrbsAtlasVbo = null;
        }
    }

    private static int WrapIndex(int index, int resolution)
    {
        int m = index % resolution;
        return m < 0 ? m + resolution : m;
    }

    private void BuildClipmapProbeOrbAttributes(
        int levels,
        int resolution,
        System.Numerics.Vector3[] rings)
    {
        int count = ComputeClipmapProbeCount(levels: levels, resolution: resolution);
        if (count <= 0)
        {
            clipmapProbeOrbColors = null;
            clipmapProbeOrbAtlasCoords = null;
            return;
        }

        clipmapProbeOrbColors ??= Array.Empty<ColorVertex>();
        if (clipmapProbeOrbColors.Length < count)
        {
            clipmapProbeOrbColors = new ColorVertex[count];
        }

        clipmapProbeOrbAtlasCoords ??= Array.Empty<UvVertex>();
        if (clipmapProbeOrbAtlasCoords.Length < count)
        {
            clipmapProbeOrbAtlasCoords = new UvVertex[count];
        }

        int written = 0;
        for (int level = 0; level < levels; level++)
        {
            var ring = (level < rings.Length) ? rings[level] : default;
            int rx = (int)MathF.Round(ring.X);
            int ry = (int)MathF.Round(ring.Y);
            int rz = (int)MathF.Round(ring.Z);

            (float r, float g, float b, float a) = GetDebugColorForLevel(level);

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int sx = WrapIndex(x + rx, resolution);
                        int sy = WrapIndex(y + ry, resolution);
                        int sz = WrapIndex(z + rz, resolution);

                        int u = sx + sz * resolution;
                        int v = sy + level * resolution;

                        if (written >= count)
                        {
                            return;
                        }

                        clipmapProbeOrbColors[written] = new ColorVertex { R = r, G = g, B = b, A = a };
                        clipmapProbeOrbAtlasCoords[written] = new UvVertex { U = u, V = v };
                        written++;
                    }
                }
            }
        }
    }

    private void RenderWorldProbeOrbsPointsLive()
    {
        if (worldProbeClipmapBufferManager?.Resources is null || clipmapProbeOrbsCount <= 0)
        {
            if (worldProbeClipmapBufferManager?.Resources is not null && clipmapProbeOrbsCount <= 0)
            {
                RateLimitedClipmapDebugLog("World-probe orbs: clipmapProbeOrbsCount=0");
            }
            return;
        }

        var shader = capi.Shader.GetProgramByName("vge_worldprobe_orbs_points") as VanillaGraphicsExpanded.Rendering.Shaders.VgeWorldProbeOrbsPointsShaderProgram;
        if (shader is null || shader.LoadError)
        {
            RateLimitedClipmapDebugLog("World-probe orbs: shader missing/load error");
            return;
        }

        if (clipmapProbeOrbsVao is null
            || !clipmapProbeOrbsVao.IsValid
            || clipmapProbePositionsVbo is null
            || !clipmapProbePositionsVbo.IsValid
            || clipmapProbeOrbsColorVbo is null
            || !clipmapProbeOrbsColorVbo.IsValid
            || clipmapProbeOrbsAtlasVbo is null
            || !clipmapProbeOrbsAtlasVbo.IsValid)
        {
            RateLimitedClipmapDebugLog("World-probe orbs: missing vao/vbo");
            return;
        }

        // Vertices are rebuilt every frame in camera-relative space (world - renderCameraOrigin).
        UpdateCurrentViewProjMatrixNoTranslate();
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invViewMatrix);

        using var cpuScope = Profiler.BeginScope("Debug.WorldProbeOrbsPoints", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.WorldProbeOrbsPoints"))
        {
            bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool prevBlend = GL.IsEnabled(EnableCap.Blend);
            bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            int prevDepthFunc = GL.GetInteger(GetPName.DepthFunc);
            float prevPointSize = GL.GetFloat(GetPName.PointSize);
            int prevBlendSrcRgb = GL.GetInteger(GetPName.BlendSrcRgb);
            int prevBlendDstRgb = GL.GetInteger(GetPName.BlendDstRgb);
            int prevBlendSrcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha);
            int prevBlendDstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);

            bool shaderUsed = false;
            try
            {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Lequal);
                capi.Render.GlToggleBlend(true);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                capi.Render.GLDepthMask(true);

                shader.Use();
                shaderUsed = true;

                shader.ModelViewProjectionMatrix = currentViewProjMatrix;
                shader.InvViewMatrix = invViewMatrix;
                shader.WorldOffset = new Vec3f(0, 0, 0);
                shader.CameraPos = new Vec3f(0, 0, 0);
                shader.PointSize = 18f;
                float maxSpacing = clipmapDebugBaseSpacing * (1 << Math.Max(clipmapDebugLevels - 1, 0));
                float maxSize = maxSpacing * clipmapDebugResolution;
                shader.FadeNear = maxSize * 0.5f;
                shader.FadeFar = maxSize * 1.05f;

                // Bind SH textures.
                int sh0 = worldProbeClipmapBufferManager.Resources.ProbeSh0TextureId;
                int sh1 = worldProbeClipmapBufferManager.Resources.ProbeSh1TextureId;
                int sh2 = worldProbeClipmapBufferManager.Resources.ProbeSh2TextureId;
                int sky0 = worldProbeClipmapBufferManager.Resources.ProbeSky0TextureId;
                int vis0 = worldProbeClipmapBufferManager.Resources.ProbeVis0TextureId;

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, sh0);
                shader.WorldProbeSH0 = 0;

                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, sh1);
                shader.WorldProbeSH1 = 1;

                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, sh2);
                shader.WorldProbeSH2 = 2;

                GL.ActiveTexture(TextureUnit.Texture3);
                GL.BindTexture(TextureTarget.Texture2D, sky0);
                shader.WorldProbeSky0 = 3;

                GL.ActiveTexture(TextureUnit.Texture4);
                GL.BindTexture(TextureTarget.Texture2D, vis0);
                shader.WorldProbeVis0 = 4;

                shader.WorldProbeSkyTint = capi.Render.AmbientColor;

                clipmapProbeOrbsVao.Bind();

                GL.PointSize(18f);
                GL.DrawArrays(PrimitiveType.Points, 0, clipmapProbeOrbsCount);

                GL.BindVertexArray(0);
            }
            finally
            {
                if (shaderUsed)
                {
                    shader.Stop();
                }

                if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
                else GL.Disable(EnableCap.DepthTest);

                GL.DepthFunc((DepthFunction)prevDepthFunc);
                GL.PointSize(prevPointSize);
                GL.BlendFuncSeparate(
                    (BlendingFactorSrc)prevBlendSrcRgb,
                    (BlendingFactorDest)prevBlendDstRgb,
                    (BlendingFactorSrc)prevBlendSrcAlpha,
                    (BlendingFactorDest)prevBlendDstAlpha);
                capi.Render.GLDepthMask(prevDepthMask);
                capi.Render.GlToggleBlend(prevBlend);
                GL.ActiveTexture((TextureUnit)prevActiveTexture);
            }
        }

        var err = GL.GetError();
        if (err != ErrorCode.NoError)
        {
            RateLimitedClipmapDebugLog($"World-probe orbs: GL error {err}");
        }
    }

    private bool TryGetRenderCameraWorldOrigin(out Vec3d originWorld)
    {
        // IMPORTANT:
        // `IRenderAPI.CameraMatrixOrigin` / `CameraMatrixOriginf` are 4x4 matrices (double/float[16]),
        // not a world-space origin vector. Using indices [0..2] will read a basis vector and will
        // change with camera rotation (causing "swimming").
        //
        // For stable world-space debug rendering we want the camera world position used by the player.
        var player = capi.World?.Player;
        if (player?.Entity is not null)
        {
            originWorld = player.Entity.CameraPos;
            return true;
        }

        originWorld = new Vec3d();
        return false;
    }

    private void UpdateWorldProbeClipmapDebugVerticesForCurrentCameraOrigin()
    {
        if (clipmapDebugLevels <= 0 || clipmapDebugResolution <= 0 || clipmapDebugBaseSpacing <= 0)
        {
            return;
        }

        if (!TryGetRenderCameraWorldOrigin(out var renderOriginWorld))
        {
            return;
        }

        bool canUpdateBounds = clipmapBoundsVao is not null
            && clipmapBoundsVao.IsValid
            && clipmapBoundsVbo is not null
            && clipmapBoundsVbo.IsValid;
        int probeCount = Math.Max(clipmapProbePointsCount, clipmapProbeOrbsCount);
        bool canUpdateProbes = clipmapProbePositionsVbo is not null
            && clipmapProbePositionsVbo.IsValid
            && clipmapProbePositions is not null
            && probeCount > 0;

        if (!canUpdateBounds && !canUpdateProbes)
        {
            return;
        }

        // Build per-level camera-relative origins for bounds/points.
        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            if (i < clipmapDebugLevels)
            {
                Vec3d oAbs = clipmapDebugOriginsAbsWorld[i];
                clipmapDebugOriginsCameraRelNowF[i] = new System.Numerics.Vector3(
                    (float)(oAbs.X - renderOriginWorld.X),
                    (float)(oAbs.Y - renderOriginWorld.Y),
                    (float)(oAbs.Z - renderOriginWorld.Z));
            }
            else
            {
                clipmapDebugOriginsCameraRelNowF[i] = default;
            }
        }

        if (canUpdateBounds)
        {
            // Rebuild bounds vertex positions (few vertices; cheap) into camera-relative space.
            clipmapBoundsCount = BuildClipmapBoundsVertices(
                baseSpacing: clipmapDebugBaseSpacing,
                levels: clipmapDebugLevels,
                resolution: clipmapDebugResolution,
                origins: clipmapDebugOriginsCameraRelNowF,
                frozenCameraMarkerPos: default);

            int lineStride = Marshal.SizeOf<LineVertex>();
            clipmapBoundsVbo!.UploadData(clipmapBoundsVertices, clipmapBoundsCount * lineStride);
        }

        if (!canUpdateProbes)
        {
            return;
        }

        var probePositions = clipmapProbePositions;
        if (probePositions is null)
        {
            return;
        }

        // Update shared probe positions into camera-relative space.
        int written = 0;
        int resolution = clipmapDebugResolution;
        int levels = clipmapDebugLevels;
        float baseSpacing = clipmapDebugBaseSpacing;

        for (int level = 0; level < levels; level++)
        {
            Vec3d oAbs = clipmapDebugOriginsAbsWorld[level];
            double spacing = baseSpacing * (1 << level);

            for (int z = 0; z < resolution; z++)
            {
                double pz = oAbs.Z + (z + 0.5) * spacing - renderOriginWorld.Z;

                for (int y = 0; y < resolution; y++)
                {
                    double py = oAbs.Y + (y + 0.5) * spacing - renderOriginWorld.Y;

                    for (int x = 0; x < resolution; x++)
                    {
                        double px = oAbs.X + (x + 0.5) * spacing - renderOriginWorld.X;
                        if (written >= probeCount || written >= probePositions.Length)
                        {
                            break;
                        }

                        probePositions[written++] = new System.Numerics.Vector3((float)px, (float)py, (float)pz);
                    }
                }
            }
        }

        int posStride = Marshal.SizeOf<System.Numerics.Vector3>();
        clipmapProbePositionsVbo!.UploadData(probePositions, probeCount * posStride);
    }

    private int BuildClipmapBoundsVertices(
        float baseSpacing,
        int levels,
        int resolution,
        System.Numerics.Vector3[] origins,
        System.Numerics.Vector3 frozenCameraMarkerPos)
    {
        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);

        int written = 0;
        for (int level = 0; level < levels; level++)
        {
            float spacing = baseSpacing * (1 << level);
            float size = spacing * resolution;

            // Coordinates are already in the engine's camera-matrix world space.
            var o = origins[level];
            float minXOuter = o.X;
            float minYOuter = o.Y;
            float minZOuter = o.Z;

            float maxXOuter = minXOuter + size;
            float maxYOuter = minYOuter + size;
            float maxZOuter = minZOuter + size;

            (float r, float g, float b, float a) = GetDebugColorForLevel(level);

            void AddVertex(float x, float y, float z)
            {
                if ((uint)written >= (uint)clipmapBoundsVertices.Length)
                {
                    return;
                }

                clipmapBoundsVertices[written++] = new LineVertex
                {
                    X = x,
                    Y = y,
                    Z = z,
                    R = r,
                    G = g,
                    B = b,
                    A = a
                };
            }

            void AddLine(float ax, float ay, float az, float bx, float by, float bz)
            {
                AddVertex(ax, ay, az);
                AddVertex(bx, by, bz);
            }

            void AddBox(float x0, float y0, float z0, float x1, float y1, float z1)
            {
                // Vintage Story uses Y-up. Draw bottom/top in the XZ plane at Y=y0/y1.
                // bottom (y0)
                AddLine(x0, y0, z0, x1, y0, z0);
                AddLine(x1, y0, z0, x1, y0, z1);
                AddLine(x1, y0, z1, x0, y0, z1);
                AddLine(x0, y0, z1, x0, y0, z0);

                // top (y1)
                AddLine(x0, y1, z0, x1, y1, z0);
                AddLine(x1, y1, z0, x1, y1, z1);
                AddLine(x1, y1, z1, x0, y1, z1);
                AddLine(x0, y1, z1, x0, y1, z0);

                // verticals (along Y)
                AddLine(x0, y0, z0, x0, y1, z0);
                AddLine(x1, y0, z0, x1, y1, z0);
                AddLine(x1, y0, z1, x1, y1, z1);
                AddLine(x0, y0, z1, x0, y1, z1);
            }

            // Outer clip volume bounds: [originMinCorner, originMinCorner + resolution*spacing]
            AddBox(minXOuter, minYOuter, minZOuter, maxXOuter, maxYOuter, maxZOuter);

            // Inner probe-center bounds: [firstProbeCenter, lastProbeCenter]
            // This helps diagnose "probes seem to start at the max corner" reports.
            // Probe centers are at origin + (i + 0.5) * spacing.
            if (resolution >= 2)
            {
                float inset = spacing * 0.5f;
                float minXInner = minXOuter + inset;
                float minYInner = minYOuter + inset;
                float minZInner = minZOuter + inset;

                float maxXInner = maxXOuter - inset;
                float maxYInner = maxYOuter - inset;
                float maxZInner = maxZOuter - inset;

                // Darken the color so it's easy to distinguish.
                (r, g, b, a) = (r * 0.35f, g * 0.35f, b * 0.35f, 1f);
                AddBox(minXInner, minYInner, minZInner, maxXInner, maxYInner, maxZInner);
            }
        }

        // Frozen capture markers:
        // - a small RGB axis tripod at the frozen camera position (so you can move away and still see where it was captured)
        // - a small YUV-ish axis tripod at the L0 clipmap center (to reveal any offset/bias)
        // - a small axis tripod at the L0 min-corner and max-corner (to make it obvious which corner is which)
        // - a small axis tripod at the L0 first/last probe center (to disambiguate "probes start at max-corner")
        // This makes it obvious the overlay is in world space when you move away.
        if ((uint)written + FrozenMarkerVertices <= (uint)clipmapBoundsVertices.Length)
        {
            float axisLen = Math.Clamp(baseSpacing * 2f, 1f, 8f);

            float ox = frozenCameraMarkerPos.X;
            float oy = frozenCameraMarkerPos.Y;
            float oz = frozenCameraMarkerPos.Z;

            void AddMarkerLine(float ax, float ay, float az, float bx, float by, float bz, float r, float g, float b)
            {
                clipmapBoundsVertices[written++] = new LineVertex { X = ax, Y = ay, Z = az, R = r, G = g, B = b, A = 1f };
                clipmapBoundsVertices[written++] = new LineVertex { X = bx, Y = by, Z = bz, R = r, G = g, B = b, A = 1f };
            }

            // X (red), Y (green), Z (blue)
            AddMarkerLine(ox, oy, oz, ox + axisLen, oy, oz, 1f, 0.25f, 0.25f);
            AddMarkerLine(ox, oy, oz, ox, oy + axisLen, oz, 0.25f, 1f, 0.25f);
            AddMarkerLine(ox, oy, oz, ox, oy, oz + axisLen, 0.25f, 0.6f, 1f);

            // L0 clipmap center marker (yellow/purple/cyan-ish), if we have at least one level.
            if (levels > 0)
            {
                float size0 = baseSpacing * resolution;
                var o0 = origins[0];
                float cx = o0.X + size0 * 0.5f;
                float cy = o0.Y + size0 * 0.5f;
                float cz = o0.Z + size0 * 0.5f;

                AddMarkerLine(cx, cy, cz, cx + axisLen, cy, cz, 1f, 1f, 0.25f);
                AddMarkerLine(cx, cy, cz, cx, cy + axisLen, cz, 0.85f, 0.25f, 1f);
                AddMarkerLine(cx, cy, cz, cx, cy, cz + axisLen, 0.25f, 1f, 1f);

                // L0 min-corner marker (dim RGB)
                float minX = o0.X;
                float minY = o0.Y;
                float minZ = o0.Z;
                AddMarkerLine(minX, minY, minZ, minX + axisLen, minY, minZ, 0.65f, 0.15f, 0.15f);
                AddMarkerLine(minX, minY, minZ, minX, minY + axisLen, minZ, 0.15f, 0.65f, 0.15f);
                AddMarkerLine(minX, minY, minZ, minX, minY, minZ + axisLen, 0.15f, 0.35f, 0.65f);

                // L0 max-corner marker (dim white)
                float maxX = o0.X + size0;
                float maxY = o0.Y + size0;
                float maxZ = o0.Z + size0;
                AddMarkerLine(maxX, maxY, maxZ, maxX + axisLen, maxY, maxZ, 0.6f, 0.6f, 0.6f);
                AddMarkerLine(maxX, maxY, maxZ, maxX, maxY + axisLen, maxZ, 0.6f, 0.6f, 0.6f);
                AddMarkerLine(maxX, maxY, maxZ, maxX, maxY, maxZ + axisLen, 0.6f, 0.6f, 0.6f);

                // L0 first probe center marker (bright orange-ish)
                float firstX = o0.X + baseSpacing * 0.5f;
                float firstY = o0.Y + baseSpacing * 0.5f;
                float firstZ = o0.Z + baseSpacing * 0.5f;
                AddMarkerLine(firstX, firstY, firstZ, firstX + axisLen, firstY, firstZ, 1f, 0.55f, 0.2f);
                AddMarkerLine(firstX, firstY, firstZ, firstX, firstY + axisLen, firstZ, 1f, 0.55f, 0.2f);
                AddMarkerLine(firstX, firstY, firstZ, firstX, firstY, firstZ + axisLen, 1f, 0.55f, 0.2f);

                // L0 last probe center marker (bright white)
                float lastX = o0.X + size0 - baseSpacing * 0.5f;
                float lastY = o0.Y + size0 - baseSpacing * 0.5f;
                float lastZ = o0.Z + size0 - baseSpacing * 0.5f;
                AddMarkerLine(lastX, lastY, lastZ, lastX + axisLen, lastY, lastZ, 0.95f, 0.95f, 0.95f);
                AddMarkerLine(lastX, lastY, lastZ, lastX, lastY + axisLen, lastZ, 0.95f, 0.95f, 0.95f);
                AddMarkerLine(lastX, lastY, lastZ, lastX, lastY, lastZ + axisLen, 0.95f, 0.95f, 0.95f);
            }
        }

        return Math.Min(written, clipmapBoundsVertices.Length);
    }

    private static (float r, float g, float b, float a) GetDebugColorForLevel(int level) => level switch
    {
        0 => (1f, 0.25f, 0.25f, 1f),
        1 => (0.25f, 1f, 0.25f, 1f),
        2 => (0.25f, 0.6f, 1f, 1f),
        3 => (1f, 0.85f, 0.25f, 1f),
        4 => (1f, 0.25f, 1f, 1f),
        5 => (0.25f, 1f, 1f, 1f),
        6 => (1f, 0.55f, 0.25f, 1f),
        _ => (0.9f, 0.9f, 0.9f, 1f),
    };

    private void RenderVgeNormalDepthAtlas()
    {
        if (quadMeshRef is null)
        {
            return;
        }

        if (!MaterialAtlasSystem.Instance.IsInitialized)
        {
            return;
        }

        if (!TerrainMaterialParamsTextureBindingHook.TryGetLastBoundNormalDepthTextureId(out int texId, out int _))
        {
            return;
        }

        if (texId == 0)
        {
            return;
        }

        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
        int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);

        var blitShader = capi.Render.GetEngineShader(EnumShaderProgram.Blit);
        blitShader.Use();

        try
        {
            capi.Render.GLDepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            capi.Render.GlToggleBlend(false);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texId);
            blitShader.BindTexture2D("scene", texId, 0);

            using var cpuScope = Profiler.BeginScope("Debug.VGE.NormalDepthAtlas", "Render");
            using (GlGpuProfiler.Instance.Scope("Debug.VGE.NormalDepthAtlas"))
            {
                capi.Render.RenderMesh(quadMeshRef);
            }
        }
        finally
        {
            blitShader.Stop();

            if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            capi.Render.GLDepthMask(prevDepthMask);
            capi.Render.GlToggleBlend(prevBlend);
            GL.ActiveTexture((TextureUnit)prevActiveTexture);
        }
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
            || (mode is >= LumOnDebugMode.VelocityMagnitude and <= LumOnDebugMode.VelocityPrevUv)
            || (mode is >= LumOnDebugMode.WorldProbeIrradianceCombined and <= LumOnDebugMode.WorldProbeOrbsPoints)
            || mode is LumOnDebugMode.WorldProbeRawConfidences
                or LumOnDebugMode.WorldProbeContributionOnly
                or LumOnDebugMode.ScreenSpaceContributionOnly;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (worldProbeClipmapBufferManagerEventSource is not null)
        {
            worldProbeClipmapBufferManagerEventSource.AnchorShifted -= OnWorldProbeClipmapAnchorShifted;
            worldProbeClipmapBufferManagerEventSource = null;
        }

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        clipmapBoundsVbo?.Dispose();
        clipmapBoundsVbo = null;
        clipmapBoundsVao?.Dispose();
        clipmapBoundsVao = null;

        clipmapQueuedTraceRaysVbo?.Dispose();
        clipmapQueuedTraceRaysVbo = null;
        clipmapQueuedTraceRaysVao?.Dispose();
        clipmapQueuedTraceRaysVao = null;

        clipmapProbePointsColorVbo?.Dispose();
        clipmapProbePointsColorVbo = null;
        clipmapProbePointsVao?.Dispose();
        clipmapProbePointsVao = null;

        clipmapProbeOrbsAtlasVbo?.Dispose();
        clipmapProbeOrbsAtlasVbo = null;
        clipmapProbeOrbsColorVbo?.Dispose();
        clipmapProbeOrbsColorVbo = null;
        clipmapProbeOrbsVao?.Dispose();
        clipmapProbeOrbsVao = null;

        clipmapProbePositionsVbo?.Dispose();
        clipmapProbePositionsVbo = null;

        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct LineVertex
    {
        public float X;
        public float Y;
        public float Z;
        public float R;
        public float G;
        public float B;
        public float A;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ColorVertex
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct UvVertex
    {
        public float U;
        public float V;
    }

    #endregion
}
