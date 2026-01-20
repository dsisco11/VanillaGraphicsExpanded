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

    private const double DEBUG_RENDER_ORDER = 1.1; // After other debug overlays
    private const int RENDER_RANGE = 1;

    private const int MaxWorldProbeLevels = 8;
    private const int ClipmapBoundsVerticesPerLevel = 24; // 12 edges * 2 vertices
    private const int FrozenMarkerVertices = 6; // 3 axes * 2 vertices

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
    private System.Numerics.Vector3 frozenCameraPosWS;
    private float frozenBaseSpacing;
    private int frozenLevels;
    private int frozenResolution;
    private readonly System.Numerics.Vector3[] frozenOrigins = new System.Numerics.Vector3[MaxWorldProbeLevels];

    // World-probe bounds debug line rendering (GL_LINES, camera-relative coords).
    private readonly LineVertex[] clipmapBoundsVertices = new LineVertex[MaxWorldProbeLevels * ClipmapBoundsVerticesPerLevel + FrozenMarkerVertices];
    private int clipmapBoundsVao;
    private int clipmapBoundsVbo;

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

        // Register at AfterBlit stage so debug output renders on top
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "lumon_debug");

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

    #endregion

    #region IRenderer

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
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
            RenderWorldProbeClipmapBoundsFrozen();
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
        UpdateCurrentViewProjMatrix();

        // Disable depth test for fullscreen overlay
        capi.Render.GLDepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        capi.Render.GlToggleBlend(false);

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        shader.EnablePbrComposite = config.EnablePbrComposite;
        shader.EnableAO = config.EnableAO;
        shader.EnableShortRangeAo = config.EnableShortRangeAo;

        // Phase 18 world-probe defines must be set before Use() as well.
        bool wantWorldProbe = false;
        System.Numerics.Vector3 wpCamPosWS = default;
        float wpBaseSpacing = 0;
        int wpLevels = 0;
        int wpResolution = 0;
        System.Numerics.Vector3[]? wpOrigins = null;
        System.Numerics.Vector3[]? wpRings = null;

        if (worldProbeClipmapBufferManager?.Resources is not null
            && worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out var wpCamPos,
                out wpBaseSpacing,
                out wpLevels,
                out wpResolution,
                out wpOrigins,
                out wpRings))
        {
            wantWorldProbe = true;
            wpCamPosWS = wpCamPos;

            // If defines changed, a recompile has been queued; skip rendering this frame.
            if (!shader.EnsureWorldProbeClipmapDefines(enabled: true, wpBaseSpacing, wpLevels, wpResolution))
            {
                return;
            }
        }
        else
        {
            shader.EnsureWorldProbeClipmapDefines(enabled: false, baseSpacing: 0, levels: 0, resolution: 0);
        }

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

        // Phase 18 world-probe debug inputs (only bound if available + active in the compiled shader).
         if (wantWorldProbe && worldProbeClipmapBufferManager?.Resources is not null && wpOrigins != null && wpRings != null)
         {
             shader.WorldProbeSH0 = worldProbeClipmapBufferManager.Resources.ProbeSh0TextureId;
             shader.WorldProbeSH1 = worldProbeClipmapBufferManager.Resources.ProbeSh1TextureId;
            shader.WorldProbeSH2 = worldProbeClipmapBufferManager.Resources.ProbeSh2TextureId;
            shader.WorldProbeVis0 = worldProbeClipmapBufferManager.Resources.ProbeVis0TextureId;
             shader.WorldProbeDist0 = worldProbeClipmapBufferManager.Resources.ProbeDist0TextureId;
             shader.WorldProbeMeta0 = worldProbeClipmapBufferManager.Resources.ProbeMeta0TextureId;
             shader.WorldProbeDebugState0 = worldProbeClipmapBufferManager.Resources.ProbeDebugState0TextureId;

            // Shaders reconstruct world positions in the engine's camera-matrix space via invViewMatrix.
            // Bind clipmap parameters in that same space (do not apply additional camera-relative shifts).
            shader.WorldProbeCameraPosWS = new Vec3f(wpCamPosWS.X, wpCamPosWS.Y, wpCamPosWS.Z);

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

        shader.Stop();

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);

        // Store current matrix for next frame's reprojection
        StorePrevViewProjMatrix();
    }

    private void OnDebugModeChanged(LumOnDebugMode prev, LumOnDebugMode current)
    {
        if (current == LumOnDebugMode.WorldProbeClipmapBounds)
        {
            CaptureFrozenClipmapBounds();
        }
        else if (prev == LumOnDebugMode.WorldProbeClipmapBounds)
        {
            hasFrozenClipmapBounds = false;
        }
    }

    private void CaptureFrozenClipmapBounds()
    {
        hasFrozenClipmapBounds = false;

        if (worldProbeClipmapBufferManager?.Resources is null)
        {
            return;
        }

        if (!worldProbeClipmapBufferManager.TryGetRuntimeParams(
                out frozenCameraPosWS,
                out frozenBaseSpacing,
                out frozenLevels,
                out frozenResolution,
                out var origins,
                out _))
        {
            return;
        }

        frozenLevels = Math.Clamp(frozenLevels, 1, MaxWorldProbeLevels);
        for (int i = 0; i < MaxWorldProbeLevels; i++)
        {
            frozenOrigins[i] = (i < origins.Length) ? origins[i] : default;
        }

        hasFrozenClipmapBounds = true;

        capi.Logger.Notification(
            "[VGE] Frozen world-probe clipmap bounds captured (camera={0:0.0},{1:0.0},{2:0.0}; L0 size={3:0.0}m; levels={4})",
            frozenCameraPosWS.X, frozenCameraPosWS.Y, frozenCameraPosWS.Z,
            frozenBaseSpacing * frozenResolution,
            frozenLevels);
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

        UpdateCurrentViewProjMatrix();

        int vertexCount = BuildClipmapBoundsVertices(
            baseSpacing: frozenBaseSpacing,
            levels: frozenLevels,
            resolution: frozenResolution,
            origins: frozenOrigins);

        if (vertexCount <= 0)
        {
            StorePrevViewProjMatrix();
            return;
        }

        using var cpuScope = Profiler.BeginScope("Debug.WorldProbeClipmapBounds", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.WorldProbeClipmapBounds"))
        {
            GL.Disable(EnableCap.DepthTest);
            capi.Render.GlToggleBlend(false);
            capi.Render.GLDepthMask(false);

            shader.Use();
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

            shader.Stop();

            GL.Enable(EnableCap.DepthTest);
            capi.Render.GLDepthMask(true);
        }

        StorePrevViewProjMatrix();
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

    private int BuildClipmapBoundsVertices(
        float baseSpacing,
        int levels,
        int resolution,
        System.Numerics.Vector3[] origins)
    {
        levels = Math.Clamp(levels, 1, MaxWorldProbeLevels);

        int written = 0;
        for (int level = 0; level < levels; level++)
        {
            float spacing = baseSpacing * (1 << level);
            float size = spacing * resolution;

            // Coordinates are already in the engine's camera-matrix world space.
            var o = origins[level];
            float minX = o.X;
            float minY = o.Y;
            float minZ = o.Z;

            float maxX = minX + size;
            float maxY = minY + size;
            float maxZ = minZ + size;

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

            // 8 corners
            float x0 = minX, x1 = maxX;
            float y0 = minY, y1 = maxY;
            float z0 = minZ, z1 = maxZ;

            // bottom (z0)
            AddLine(x0, y0, z0, x1, y0, z0);
            AddLine(x1, y0, z0, x1, y1, z0);
            AddLine(x1, y1, z0, x0, y1, z0);
            AddLine(x0, y1, z0, x0, y0, z0);

            // top (z1)
            AddLine(x0, y0, z1, x1, y0, z1);
            AddLine(x1, y0, z1, x1, y1, z1);
            AddLine(x1, y1, z1, x0, y1, z1);
            AddLine(x0, y1, z1, x0, y0, z1);

            // verticals
            AddLine(x0, y0, z0, x0, y0, z1);
            AddLine(x1, y0, z0, x1, y0, z1);
            AddLine(x1, y1, z0, x1, y1, z1);
            AddLine(x0, y1, z0, x0, y1, z1);
        }

        // Frozen capture marker: a small RGB axis tripod at the frozen camera position.
        // This makes it obvious the overlay is in world space when you move away.
        if ((uint)written + FrozenMarkerVertices <= (uint)clipmapBoundsVertices.Length)
        {
            float axisLen = Math.Clamp(baseSpacing * 2f, 1f, 8f);

            float ox = frozenCameraPosWS.X;
            float oy = frozenCameraPosWS.Y;
            float oz = frozenCameraPosWS.Z;

            void AddMarkerLine(float ax, float ay, float az, float bx, float by, float bz, float r, float g, float b)
            {
                clipmapBoundsVertices[written++] = new LineVertex { X = ax, Y = ay, Z = az, R = r, G = g, B = b, A = 1f };
                clipmapBoundsVertices[written++] = new LineVertex { X = bx, Y = by, Z = bz, R = r, G = g, B = b, A = 1f };
            }

            // X (red), Y (green), Z (blue)
            AddMarkerLine(ox, oy, oz, ox + axisLen, oy, oz, 1f, 0.25f, 0.25f);
            AddMarkerLine(ox, oy, oz, ox, oy + axisLen, oz, 0.25f, 1f, 0.25f);
            AddMarkerLine(ox, oy, oz, ox, oy, oz + axisLen, 0.25f, 0.6f, 1f);
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

        capi.Render.GLDepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        capi.Render.GlToggleBlend(false);

        var blitShader = capi.Render.GetEngineShader(EnumShaderProgram.Blit);
        blitShader.Use();

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, texId);
        blitShader.BindTexture2D("scene", texId, 0);

        using var cpuScope = Profiler.BeginScope("Debug.VGE.NormalDepthAtlas", "Render");
        using (GlGpuProfiler.Instance.Scope("Debug.VGE.NormalDepthAtlas"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        blitShader.Stop();

        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);
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

        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
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
