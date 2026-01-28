using System;
using System.Numerics;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>
/// Owns the GPU textures and upload plumbing for the world-probe clipmap.
/// Phase 18.8 responsibility: correct allocation/recreation and leak-free disposal.
/// </summary>
internal sealed class LumOnWorldProbeClipmapBufferManager : IDisposable
{
    private const int LayoutVersion = LumOnWorldProbeLayout.WorldProbeLayoutVersion;
    internal const int MaxDebugTraceRays = 512;

    internal readonly struct DebugTraceRay
    {
        public readonly Vec3d StartWorld;
        public readonly Vec3d EndWorld;
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public DebugTraceRay(Vec3d startWorld, Vec3d endWorld, float r, float g, float b, float a)
        {
            StartWorld = startWorld;
            EndWorld = endWorld;
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;

    private LumOnWorldProbeClipmapGpuResources? resources;
    private LumOnWorldProbeClipmapGpuUploader? uploader;

    // Cached per-frame shader parameters (owned by LumOnRenderer).
    // This enables debug overlays and other passes to bind world-probe uniforms
    // without needing direct access to the scheduler.
    private bool hasRuntimeParams;
    private Vec3d runtimeCameraPosWorld = new Vec3d();
    private Vector3 runtimeCameraPosWS;
    private float runtimeBaseSpacing;
    private int runtimeLevels;
    private int runtimeResolution;
    private readonly Vector3[] runtimeOrigins = new Vector3[8];
    private readonly Vector3[] runtimeRings = new Vector3[8];

    // Debug-only: preview rays currently queued for tracing.
    private readonly DebugTraceRay[] debugTraceRays = new DebugTraceRay[MaxDebugTraceRays];
    private int debugTraceRayCount;
    private int debugTraceRayFrameIndex;

    private bool forceRecreate;
    private bool isDisposed;

    private int lastResolution;
    private int lastLevels;
    private int lastLayoutVersion;

    private bool disabledDueToTextureLimits;
    private int disabledResolution;
    private int disabledLevels;
    private int disabledTileSize;
    private int disabledAtlasW;
    private int disabledAtlasH;
    private int disabledMaxTextureSize;

    public LumOnWorldProbeClipmapGpuResources? Resources => resources;
    public LumOnWorldProbeClipmapGpuUploader? Uploader => uploader;

    /// <summary>
    /// Fired whenever the snapped clipmap anchor shifts (origin and ring offsets change).
    /// Intended for debug visualization rebuilds; does not imply any particular GPU upload timing.
    /// </summary>
    public event Action<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>? AnchorShifted;

    public bool TryGetRuntimeParams(
        out Vec3d cameraPosWorld,
        out Vector3 cameraPosWS,
        out float baseSpacing,
        out int levels,
        out int resolution,
        out Vector3[] origins,
        out Vector3[] rings)
    {
        if (!hasRuntimeParams)
        {
            cameraPosWorld = new Vec3d();
            cameraPosWS = default;
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
            origins = Array.Empty<Vector3>();
            rings = Array.Empty<Vector3>();
            return false;
        }

        cameraPosWorld = runtimeCameraPosWorld;
        cameraPosWS = runtimeCameraPosWS;
        baseSpacing = runtimeBaseSpacing;
        levels = runtimeLevels;
        resolution = runtimeResolution;
        origins = runtimeOrigins;
        rings = runtimeRings;
        return true;
    }

    public void UpdateRuntimeParams(
        Vec3d cameraPosWorld,
        Vector3 cameraPosWS,
        float baseSpacing,
        int levels,
        int resolution,
        ReadOnlySpan<Vector3> origins,
        ReadOnlySpan<Vector3> rings)
    {
        bool wasMissing = !hasRuntimeParams;

        runtimeCameraPosWorld = cameraPosWorld;
        runtimeCameraPosWS = cameraPosWS;
        runtimeBaseSpacing = baseSpacing;
        runtimeLevels = levels;
        runtimeResolution = resolution;

        // Fixed-size copy to avoid per-frame allocations and keep references stable.
        for (int i = 0; i < 8; i++)
        {
            runtimeOrigins[i] = (i < origins.Length) ? origins[i] : default;
            runtimeRings[i] = (i < rings.Length) ? rings[i] : default;
        }

        hasRuntimeParams = true;

        if (wasMissing)
        {
            capi.Logger.Debug(
                "[VGE] World-probe clipmap runtime params published (levels={0}, res={1}, baseSpacing={2:0.###})",
                runtimeLevels,
                runtimeResolution,
                runtimeBaseSpacing);
        }
    }

    public void PublishDebugTraceRays(int frameIndex, ReadOnlySpan<DebugTraceRay> rays)
    {
        int count = Math.Clamp(rays.Length, 0, MaxDebugTraceRays);
        for (int i = 0; i < count; i++)
        {
            debugTraceRays[i] = rays[i];
        }

        debugTraceRayCount = count;
        debugTraceRayFrameIndex = frameIndex;
    }

    public void ClearDebugTraceRays(int frameIndex)
    {
        debugTraceRayCount = 0;
        debugTraceRayFrameIndex = frameIndex;
    }

    public bool TryGetDebugTraceRays(out DebugTraceRay[] rays, out int count, out int frameIndex)
    {
        if (debugTraceRayCount <= 0)
        {
            rays = Array.Empty<DebugTraceRay>();
            count = 0;
            frameIndex = debugTraceRayFrameIndex;
            return false;
        }

        rays = debugTraceRays;
        count = debugTraceRayCount;
        frameIndex = debugTraceRayFrameIndex;
        return true;
    }

    internal void NotifyAnchorShifted(in LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent evt)
    {
        AnchorShifted?.Invoke(evt);
    }

    public LumOnWorldProbeClipmapBufferManager(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        // On shader reload, treat clipmap resources as needing rebuild.
        // This is conservative (textures often survive), but keeps attachment state deterministic.
        this.capi.Event.ReloadShader += OnReloadShader;

        lastLayoutVersion = LayoutVersion;
    }

    public void RequestRecreate(string reason)
    {
        forceRecreate = true;
        capi.Logger.Debug("[LumOn][WorldProbes] Clipmap resource recreation requested: {0}", reason);
    }

    /// <summary>
    /// Ensures clipmap textures exist and match current config.
    /// Returns true if resources were already valid, false if they were created/recreated.
    /// </summary>
    public bool EnsureResources()
    {
        if (isDisposed) return false;

        int resolution = config.WorldProbeClipmap.ClipmapResolution;
        int levels = config.WorldProbeClipmap.ClipmapLevels;

        int tileSize = config.WorldProbeClipmap.OctahedralTileSize;
        var (atlasW, atlasH) = LumOnWorldProbeLayout.GetRadianceAtlasSize(resolution, levels, tileSize);
        int maxTexSize = GetMaxTextureSizeSafe();

        // Guardrail: prevent allocating oversized textures.
        // If we can't query max size (0), continue with best-effort allocation.
        bool exceeds = maxTexSize > 0 && (atlasW > maxTexSize || atlasH > maxTexSize);
        if (exceeds)
        {
            bool sameAsLastDisabled =
                disabledDueToTextureLimits &&
                disabledResolution == resolution &&
                disabledLevels == levels &&
                disabledTileSize == tileSize &&
                disabledAtlasW == atlasW &&
                disabledAtlasH == atlasH &&
                disabledMaxTextureSize == maxTexSize;

            disabledDueToTextureLimits = true;
            disabledResolution = resolution;
            disabledLevels = levels;
            disabledTileSize = tileSize;
            disabledAtlasW = atlasW;
            disabledAtlasH = atlasH;
            disabledMaxTextureSize = maxTexSize;

            forceRecreate = false;

            // Drop any existing resources (topology changed or config became invalid).
            resources?.Dispose();
            resources = null;
            uploader?.Dispose();
            uploader = null;

            lastResolution = resolution;
            lastLevels = levels;
            lastLayoutVersion = LayoutVersion;

            if (!sameAsLastDisabled)
            {
                capi.Logger.Warning(
                    "[VGE] World-probe clipmap disabled: radiance atlas exceeds GL max texture size. (N={0}, levels={1}, S={2}) -> (W={3}, H={4}), max={5}",
                    resolution,
                    levels,
                    tileSize,
                    atlasW,
                    atlasH,
                    maxTexSize);
            }

            return false;
        }

        disabledDueToTextureLimits = false;

        bool needsRecreate =
            forceRecreate ||
            resources is null ||
            resolution != lastResolution ||
            levels != lastLevels ||
            lastLayoutVersion != LayoutVersion;

        if (!needsRecreate)
        {
            return true;
        }

        forceRecreate = false;

        resources?.Dispose();
        uploader?.Dispose();

        resources = new LumOnWorldProbeClipmapGpuResources(resolution, levels, tileSize);
        uploader = new LumOnWorldProbeClipmapGpuUploader(capi);

        lastResolution = resolution;
        lastLevels = levels;
        lastLayoutVersion = LayoutVersion;

        return false;
    }

    private static int GetMaxTextureSizeSafe()
    {
        try
        {
            return GL.GetInteger(GetPName.MaxTextureSize);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        capi.Event.ReloadShader -= OnReloadShader;

        uploader?.Dispose();
        uploader = null;

        resources?.Dispose();
        resources = null;
    }

    private bool OnReloadShader()
    {
        RequestRecreate("graphics reload");
        EnsureResources();
        return true;
    }
}
