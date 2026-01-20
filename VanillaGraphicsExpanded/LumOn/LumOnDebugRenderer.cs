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
    private readonly LumOnConfig config;
    private readonly LumOnBufferManager? bufferManager;
    private readonly GBufferManager? gBufferManager;
    private readonly DirectLightingBufferManager? directLightingBufferManager;

    private LumOnWorldProbeClipmapBufferManager? worldProbeClipmapBufferManager;

    private MeshRef? quadMeshRef;

    private LumOnDebugMode lastMode = LumOnDebugMode.Off;

    private bool hasFrozenClipmapBounds;
    private Vec3d frozenCameraPosWorld;
    private float frozenBaseSpacing;
    private int frozenLevels;
    private int frozenResolution;
    private readonly Vec3d[] frozenOriginsWorld = new Vec3d[MaxWorldProbeLevels];
    private readonly System.Numerics.Vector3[] frozenOrigins = new System.Numerics.Vector3[MaxWorldProbeLevels];

    // World-probe bounds debug line rendering (GL_LINES, camera-relative coords).
    private readonly LineVertex[] clipmapBoundsVertices = new LineVertex[MaxWorldProbeLevels * ClipmapBoundsVerticesPerLevel + FrozenMarkerVertices];
    private int clipmapBoundsVao;
    private int clipmapBoundsVbo;

    // World-probe per-probe point rendering (GL_POINTS, camera-matrix world space).
    private LineVertex[]? clipmapProbePointVertices;
    private int clipmapProbePointsVao;
    private int clipmapProbePointsVbo;

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
        LumOnConfig config,
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
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "lumon_debug_bounds");

        capi.Logger.Notification("[LumOn] Debug renderer initialized");
    }

    internal void SetWorldProbeClipmapBufferManager(LumOnWorldProbeClipmapBufferManager? bufferManager)
    {
        worldProbeClipmapBufferManager = bufferManager;
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
        // Render 3D wireframes after transparency so the depth buffer is available and water/OIT content is present.
        if (stage == EnumRenderStage.AfterOIT)
        {
            if (config.DebugMode == LumOnDebugMode.WorldProbeClipmapBounds)
            {
                RenderWorldProbeClipmapBoundsLive();
            }
            else if (config.DebugMode == LumOnDebugMode.WorldProbeSpheres)
            {
                RenderWorldProbeClipmapBoundsLive();
            }

            return;
        }

        if (stage != EnumRenderStage.AfterBlit)
        {
            return;
        }

        if (config.DebugMode != lastMode)
        {
            OnDebugModeChanged(prev: lastMode, current: config.DebugMode);
            lastMode = config.DebugMode;
        }

        // VGE-only debug views that do not rely on lumon_debug.fsh.
        if (config.DebugMode == LumOnDebugMode.VgeNormalDepthAtlas)
        {
            RenderVgeNormalDepthAtlas();
            return;
        }

        if (config.DebugMode == LumOnDebugMode.WorldProbeClipmapBounds)
        {
            return;
        }

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
        UpdateCurrentViewProjMatrixNoTranslate();

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        shader.EnablePbrComposite = config.EnablePbrComposite;
        shader.EnableAO = config.EnableAO;
        shader.EnableShortRangeAo = config.EnableShortRangeAo;

        // Phase 18 world-probe defines must be set before Use() as well.
        // Make this robust across initialization order / live reloads: if the renderer's buffer manager reference
        // is stale/missing, re-acquire it from the mod system.
        if (worldProbeClipmapBufferManager is null || worldProbeClipmapBufferManager.Resources is null)
        {
            var clipmapMs = capi.ModLoader.GetModSystem<ModSystems.WorldProbeModSystem>();
            worldProbeClipmapBufferManager = clipmapMs.EnsureClipmapResources(capi, "LumOnDebugRenderer bind");
        }

        // Keep resources alive for debug modes even if the main renderer is temporarily not running.
        // (Defines are compile-time; we want stable behavior when switching debug modes.)
        worldProbeClipmapBufferManager?.EnsureResources();

        bool hasWorldProbeResources = worldProbeClipmapBufferManager?.Resources is not null;

        // Runtime params are optional; debug overlays can still compile the enabled variant from the known topology.
        // (If runtime params are missing, sampling will likely show no contribution, but it should not force-disable.)
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

        bool shaderUsed = false;
        try
        {
            // Fullscreen overlays should not disturb global GL state even on early-return paths.
            capi.Render.GLDepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            capi.Render.GlToggleBlend(false);

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
        }

        // Store current matrix for next frame's reprojection
        StorePrevViewProjMatrix();
    }

    private void OnDebugModeChanged(LumOnDebugMode prev, LumOnDebugMode current)
    {
        if (current is >= LumOnDebugMode.WorldProbeIrradianceCombined and <= LumOnDebugMode.WorldProbeSpheres
            || current == LumOnDebugMode.WorldProbeClipmapBounds)
        {
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
            else if (worldProbeClipmapBufferManager.TryGetRuntimeParams(out var camPos, out var baseSpacing, out var levels, out var resolution, out _, out _))
            {
                capi.Logger.Debug($"[VGE] World-probe clipmap params: levels={levels}, res={resolution}, baseSpacing={baseSpacing:0.###}, cam=({camPos.X:0.#},{camPos.Y:0.#},{camPos.Z:0.#})");
            }
            else
            {
                capi.Logger.Debug("[VGE] World-probe clipmap params not published yet (runtime params missing).");
            }

            if (worldProbeClipmapBufferManager is not null && worldProbeClipmapBufferManager.TryGetRuntimeParams(
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

        // WorldProbeClipmapBounds is live now; keep frozen capture code around for future use,
        // but do not auto-capture when entering the mode.
    }

    private void CaptureFrozenClipmapBounds()
    {
        hasFrozenClipmapBounds = false;

        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out _,
                out frozenBaseSpacing,
                out frozenLevels,
                out frozenResolution,
                out var origins,
                out _))
        {
            return;
        }

        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            return;
        }

        // Store the frozen capture in absolute world space so it stays fixed even when the engine's
        // camera-matrix origin shifts (floating origin).
        frozenCameraPosWorld = player.Entity.CameraPos;

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
        if (clipmapBoundsVao == 0 || clipmapBoundsVbo == 0)
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

        UpdateCurrentViewProjMatrix();

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

                GL.BindVertexArray(clipmapBoundsVao);

                int stride = Marshal.SizeOf<LineVertex>();
                GL.BindBuffer(BufferTarget.ArrayBuffer, clipmapBoundsVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vertexCount * stride, clipmapBoundsVertices, BufferUsageHint.StreamDraw);

                GL.LineWidth(2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
                GL.LineWidth(1f);

                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
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
        // Live bounds overlay (for WorldProbeSpheres mode) so we can compare bounds + spheres in the same frame.
        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out var camPosWS,
                out float baseSpacing,
                out int levels,
                out int resolution,
                out var origins,
                out _))
        {
            return;
        }

        var shader = capi.Shader.GetProgramByName("vge_debug_lines") as VgeDebugLinesShaderProgram;
        if (shader is null || shader.LoadError)
        {
            return;
        }

        EnsureClipmapBoundsLineGlObjects();
        if (clipmapBoundsVao == 0 || clipmapBoundsVbo == 0)
        {
            return;
        }

        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);

        // Convert runtime origins (originAbs - cameraAbs) into camera-matrix world space so the wireframe lines
        // match the sphere debug shader (which ray-marches in camera-matrix world space).
        // Use the published camera position from the runtime params so the conversion matches the same frame's
        // world-probe uniforms (avoids per-pass camera bob/weave).
        var camPosMatrix = camPosWS;

        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            frozenOrigins[i] = (i < origins.Length) ? (origins[i] + camPosMatrix) : default;
        }

        UpdateCurrentViewProjMatrix();

        int vertexCount = BuildClipmapBoundsVertices(
            baseSpacing: baseSpacing,
            levels: levels,
            resolution: resolution,
            origins: frozenOrigins,
            frozenCameraMarkerPos: camPosMatrix);

        if (vertexCount <= 0)
        {
            return;
        }

        // Also render per-probe GL_POINTS so we can compare "actual probe centers" against the bounds without
        // any ray-march selection quirks from the sphere debug shader.
        EnsureClipmapProbePointsGlObjects();
        int pointCount = 0;
        if (clipmapProbePointsVao != 0 && clipmapProbePointsVbo != 0)
        {
            pointCount = BuildClipmapProbePointVertices(
                baseSpacing: baseSpacing,
                levels: levels,
                resolution: resolution,
                origins: frozenOrigins);
        }

        using var cpuScope = Profiler.BeginScope("Debug.WorldProbeClipmapBoundsLive", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.WorldProbeClipmapBoundsLive"))
        {
            bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool prevBlend = GL.IsEnabled(EnableCap.Blend);
            bool prevDepthMask = GL.GetBoolean(GetPName.DepthWritemask);
            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            int prevDepthFunc = GL.GetInteger(GetPName.DepthFunc);
            float prevPointSize = GL.GetFloat(GetPName.PointSize);

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

                GL.BindVertexArray(clipmapBoundsVao);

                int stride = Marshal.SizeOf<LineVertex>();
                GL.BindBuffer(BufferTarget.ArrayBuffer, clipmapBoundsVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vertexCount * stride, clipmapBoundsVertices, BufferUsageHint.StreamDraw);

                GL.LineWidth(2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
                GL.LineWidth(1f);

                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.BindVertexArray(0);

                if (pointCount > 0 && clipmapProbePointVertices is not null)
                {
                    GL.PointSize(3.5f);

                    GL.BindVertexArray(clipmapProbePointsVao);

                    GL.BindBuffer(BufferTarget.ArrayBuffer, clipmapProbePointsVbo);
                    GL.BufferData(BufferTarget.ArrayBuffer, pointCount * stride, clipmapProbePointVertices, BufferUsageHint.StreamDraw);
                    GL.DrawArrays(PrimitiveType.Points, 0, pointCount);

                    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
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
                capi.Render.GLDepthMask(prevDepthMask);
                capi.Render.GlToggleBlend(prevBlend);
                GL.ActiveTexture((TextureUnit)prevActiveTexture);
            }
        }
    }

    private void EnsureClipmapBoundsLineGlObjects()
    {
        if (clipmapBoundsVao != 0 && clipmapBoundsVbo != 0)
        {
            return;
        }

        try
        {
            clipmapBoundsVao = GL.GenVertexArray();
            clipmapBoundsVbo = GL.GenBuffer();

            GL.BindVertexArray(clipmapBoundsVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, clipmapBoundsVbo);

            int stride = Marshal.SizeOf<LineVertex>();

            // vec3 position
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec4 color
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 12);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, clipmapBoundsVao, "VGE_WorldProbeClipmapBoundsLines_VAO");
            GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, clipmapBoundsVbo, "VGE_WorldProbeClipmapBoundsLines_VBO");
        }
        catch
        {
            // Best-effort only; fall back to no-op if GL objects can't be created.
            if (clipmapBoundsVbo != 0) GL.DeleteBuffer(clipmapBoundsVbo);
            if (clipmapBoundsVao != 0) GL.DeleteVertexArray(clipmapBoundsVao);
            clipmapBoundsVao = 0;
            clipmapBoundsVbo = 0;
        }
    }

    private void EnsureClipmapProbePointsGlObjects()
    {
        if (clipmapProbePointsVao != 0 && clipmapProbePointsVbo != 0)
        {
            return;
        }

        try
        {
            clipmapProbePointsVao = GL.GenVertexArray();
            clipmapProbePointsVbo = GL.GenBuffer();

            GL.BindVertexArray(clipmapProbePointsVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, clipmapProbePointsVbo);

            int stride = Marshal.SizeOf<LineVertex>();

            // vec3 position
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec4 color
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 12);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, clipmapProbePointsVao, "VGE_WorldProbeClipmapProbePoints_VAO");
            GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, clipmapProbePointsVbo, "VGE_WorldProbeClipmapProbePoints_VBO");
        }
        catch
        {
            // Best-effort only; fall back to no-op if GL objects can't be created.
            if (clipmapProbePointsVbo != 0) GL.DeleteBuffer(clipmapProbePointsVbo);
            if (clipmapProbePointsVao != 0) GL.DeleteVertexArray(clipmapProbePointsVao);
            clipmapProbePointsVao = 0;
            clipmapProbePointsVbo = 0;
        }
    }

    private int BuildClipmapProbePointVertices(
        float baseSpacing,
        int levels,
        int resolution,
        System.Numerics.Vector3[] origins)
    {
        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);
        resolution = Math.Max(1, resolution);

        long requested = (long)levels * resolution * resolution * resolution;
        if (requested <= 0 || requested > MaxProbePointVertices)
        {
            return 0;
        }

        clipmapProbePointVertices ??= Array.Empty<LineVertex>();
        if (clipmapProbePointVertices.Length < requested)
        {
            clipmapProbePointVertices = new LineVertex[requested];
        }

        int written = 0;
        for (int level = 0; level < levels; level++)
        {
            float spacing = baseSpacing * (1 << level);
            var o = origins[level];

            (float r, float g, float b, float a) = GetDebugColorForLevel(level);
            (r, g, b, a) = (r * 0.85f, g * 0.85f, b * 0.85f, 1f);

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float px = o.X + (x + 0.5f) * spacing;
                        float py = o.Y + (y + 0.5f) * spacing;
                        float pz = o.Z + (z + 0.5f) * spacing;

                        clipmapProbePointVertices[written++] = new LineVertex
                        {
                            X = px,
                            Y = py,
                            Z = pz,
                            R = r,
                            G = g,
                            B = b,
                            A = a
                        };
                    }
                }
            }
        }

        return written;
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
            || (mode is >= LumOnDebugMode.WorldProbeIrradianceCombined and <= LumOnDebugMode.WorldProbeSpheres);
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

        if (clipmapBoundsVbo != 0)
        {
            GL.DeleteBuffer(clipmapBoundsVbo);
            clipmapBoundsVbo = 0;
        }

        if (clipmapBoundsVao != 0)
        {
            GL.DeleteVertexArray(clipmapBoundsVao);
            clipmapBoundsVao = 0;
        }

        if (clipmapProbePointsVbo != 0)
        {
            GL.DeleteBuffer(clipmapProbePointsVbo);
            clipmapProbePointsVbo = 0;
        }

        if (clipmapProbePointsVao != 0)
        {
            GL.DeleteVertexArray(clipmapProbePointsVao);
            clipmapProbePointsVao = 0;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
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

    #endregion
}
