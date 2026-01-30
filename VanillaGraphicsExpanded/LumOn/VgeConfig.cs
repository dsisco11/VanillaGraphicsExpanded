using System;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Configuration for LumOn Screen Probe Gather system.
/// Persisted to: ModConfig/VanillaGraphicsExpanded.json
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class VgeConfig
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DebugConfig
    {
        /// <summary>
        /// Debug viewer persisted state (which views are currently enabled/active).
        /// </summary>
        [JsonProperty]
        public DebugViewsConfig DebugViews { get; set; } = new();

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class DebugViewsConfig
        {
            /// <summary>
            /// The currently active exclusive debug view id (if any).
            /// </summary>
            [JsonProperty]
            public string? ActiveExclusiveViewId { get; set; }

            /// <summary>
            /// The currently active toggle debug view ids.
            /// </summary>
            [JsonProperty]
            public string[] ActiveToggleViewIds { get; set; } = Array.Empty<string>();

            internal void Sanitize()
            {
                ActiveExclusiveViewId = string.IsNullOrWhiteSpace(ActiveExclusiveViewId)
                    ? null
                    : ActiveExclusiveViewId.Trim();

                if (ActiveToggleViewIds is null || ActiveToggleViewIds.Length == 0)
                {
                    ActiveToggleViewIds = Array.Empty<string>();
                    return;
                }

                // Normalize + de-duplicate.
                ActiveToggleViewIds = ActiveToggleViewIds
                    .Where(static s => !string.IsNullOrWhiteSpace(s))
                    .Select(static s => s.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static s => s, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        internal void Sanitize()
        {
            DebugViews ??= new DebugViewsConfig();
            DebugViews.Sanitize();
        }
    }

    public enum ProbeAtlasGatherMode
    {
        /// <summary>
        /// Integrate hemisphere directly from the screen-probe atlas (higher cost).
        /// </summary>
        IntegrateAtlas = 0,

        /// <summary>
        /// Project probe atlas to SH, then evaluate SH in gather (Option B, cheaper).
        /// </summary>
        EvaluateProjectedSH = 1
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NormalDepthBakeConfig
    {
        // ───────────────────────────────────────────────────────────
        // Band-pass detail extraction
        // ───────────────────────────────────────────────────────────

        [JsonProperty]
        public float SigmaBig { get; set; } = 16f;

        [JsonProperty]
        public float Sigma1 { get; set; } = 0.75f;

        [JsonProperty]
        public float Sigma2 { get; set; } = 1.75f;

        [JsonProperty]
        public float Sigma3 { get; set; } = 3.5f;

        [JsonProperty]
        public float Sigma4 { get; set; } = 7.0f;

        [JsonProperty]
        public float W1 { get; set; } = 1.00f;

        [JsonProperty]
        public float W2 { get; set; } = 0.65f;

        [JsonProperty]
        public float W3 { get; set; } = 0.25f;

        // ───────────────────────────────────────────────────────────
        // Desired slope field
        // ───────────────────────────────────────────────────────────

        [JsonProperty]
        public float Gain { get; set; } = 1.25f;

        [JsonProperty]
        public float MaxSlope { get; set; } = 1.0f;

        [JsonProperty]
        public float EdgeT0 { get; set; } = 0.01f;

        [JsonProperty]
        public float EdgeT1 { get; set; } = 0.05f;

        // ───────────────────────────────────────────────────────────
        // Poisson solve (multigrid)
        // ───────────────────────────────────────────────────────────

        [JsonProperty]
        public int MultigridVCycles { get; set; } = 5;

        [JsonProperty]
        public int MultigridPreSmooth { get; set; } = 6;

        [JsonProperty]
        public int MultigridPostSmooth { get; set; } = 6;

        [JsonProperty]
        public int MultigridCoarsestIters { get; set; } = 40;

        // ───────────────────────────────────────────────────────────
        // Post
        // ───────────────────────────────────────────────────────────

        [JsonProperty]
        public float HeightStrength { get; set; } = 1.0f;

        [JsonProperty]
        public float Gamma { get; set; } = 1.0f;

        [JsonProperty]
        public float NormalStrength { get; set; } = 2.0f;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MaterialAtlasConfig
    {
        /// <summary>
        /// Enables progressive async population of the material params atlas (RGB16F).
        /// When disabled, the atlas is populated synchronously during loading.
        /// </summary>
        [JsonProperty]
        public bool EnableAsync { get; set; } = true;

        /// <summary>
        /// Per-frame time budget (ms) for the async material atlas scheduler.
        /// This caps render-thread work (job dispatch + texture sub-region uploads).
        /// </summary>
        [JsonProperty]
        public float AsyncBudgetMs { get; set; } = 1.0f;

        /// <summary>
        /// Maximum number of texture sub-region uploads per frame.
        /// Limits GL work and reduces hitching.
        /// </summary>
        [JsonProperty]
        public int AsyncMaxUploadsPerFrame { get; set; } = 32;

        /// <summary>
        /// Maximum number of CPU tile jobs dispatched per frame.
        /// Limits task churn and keeps background work paced.
        /// </summary>
        [JsonProperty]
        public int AsyncMaxJobsPerFrame { get; set; } = 32;

        /// <summary>
        /// Enables building and binding the VGE normal+depth sidecar atlas.
        /// Pixels are generated during loading from tileable albedo textures (per texture rect).
        /// Requires restart / re-entering the world to fully apply.
        /// </summary>
        [JsonProperty]
        public bool EnableNormalMaps { get; set; } = true;

        /// <summary>
        /// Enables the material atlas disk cache.
        /// When enabled, material params and normal+depth tiles can be loaded from and persisted to disk
        /// so subsequent sessions can skip expensive work.
        /// </summary>
        [JsonProperty]
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// When enabled, the loading-screen cache warmup will be forced to fully drain:
        /// all cache-hit tiles are uploaded before the atlas build starts.
        /// The warmup upload path temporarily uses direct uploads (disables PBO streaming)
        /// to avoid leaving queued uploads to drain during gameplay.
        /// </summary>
        [JsonProperty]
        public bool ForceCacheWarmupDirectUploadsOnWorldLoad { get; set; } = true;

        /// <summary>
        /// Enables debug logging for disk cache hit/miss counters.
        /// </summary>
        [JsonProperty]
        public bool DebugLogMaterialAtlasDiskCache { get; set; } = true;

        /// <summary>
        /// Shows an in-game progress panel while the material atlas is building.
        /// Intended for development/diagnostics; off by default.
        /// </summary>
        [JsonProperty]
        public bool ShowMaterialAtlasProgressPanel { get; set; } = true;

        /// <summary>
        /// Enables Parallax Occlusion Mapping (POM) in patched vanilla chunk shaders.
        /// POM is only applied when a stable per-face UV rect is available (SSBO path).
        /// Requires shader reload / re-entering the world to fully apply.
        /// </summary>
        [JsonProperty]
        public bool EnableParallaxOcclusionMapping { get; set; } = false;

        /// <summary>
        /// POM UV offset scale (in atlas UV units).
        /// Defaults to the parallax scale for convenience.
        /// </summary>
        [JsonProperty]
        public float ParallaxScale { get; set; } = 0.05f;

        /// <summary>
        /// Minimum ray-march steps for POM.
        /// </summary>
        [JsonProperty]
        public int ParallaxMinSteps { get; set; } = 6;

        /// <summary>
        /// Maximum ray-march steps for POM (used at grazing angles).
        /// </summary>
        [JsonProperty]
        public int ParallaxMaxSteps { get; set; } = 24;

        /// <summary>
        /// Binary refinement steps after the initial march.
        /// </summary>
        [JsonProperty]
        public int ParallaxRefinementSteps { get; set; } = 4;

        /// <summary>
        /// Distance fade start (world units, camera-relative).
        /// </summary>
        [JsonProperty]
        public float ParallaxFadeStart { get; set; } = 3.0f;

        /// <summary>
        /// Distance fade end (world units, camera-relative). Beyond this POM is disabled.
        /// </summary>
        [JsonProperty]
        public float ParallaxFadeEnd { get; set; } = 14.0f;

        /// <summary>
        /// Hard clamp for max UV offset, in texels of the normal/depth atlas.
        /// Keeps mip/derivative stability and prevents cross-tile bleeding.
        /// </summary>
        [JsonProperty]
        public float ParallaxMaxTexels { get; set; } = 4.0f;

        /// <summary>
        /// POM debug metric selector (written to gBufferNormal.w for visualization).
        /// 0 = off
        /// 1 = UV rect edge distance (0 near edge .. 1 away)
        /// 2 = clamp hit (0/1)
        /// 3 = effective step count (0..1)
        /// 4 = distance/angle weight (0..1)
        /// </summary>
        [JsonProperty]
        public int ParallaxDebugMode { get; set; } = 3;

        /// <summary>
        /// Parameters for generating a tileable height/normal field from albedo.
        /// Applied during loading when <see cref="EnableNormalMaps"/> is enabled.
        /// </summary>
        [JsonProperty]
        public NormalDepthBakeConfig NormalDepthBake { get; set; } = new();

        /// <summary>
        /// Enables additional debug logging for normal+depth atlas build/bind plumbing.
        /// </summary>
        [JsonProperty]
        public bool DebugLogNormalDepthAtlas { get; set; } = true;

        internal void Sanitize()
        {
            // Keep existing behavior for NaNs: clamp/guards are conservative.
            AsyncBudgetMs = Math.Clamp(AsyncBudgetMs, 0.0f, 100.0f);
            AsyncMaxUploadsPerFrame = Math.Clamp(AsyncMaxUploadsPerFrame, 0, 512);
            AsyncMaxJobsPerFrame = Math.Clamp(AsyncMaxJobsPerFrame, 0, 512);

            ParallaxScale = Math.Clamp(ParallaxScale, 0.0f, 0.25f);
            ParallaxMinSteps = Math.Clamp(ParallaxMinSteps, 1, 128);
            ParallaxMaxSteps = Math.Clamp(ParallaxMaxSteps, ParallaxMinSteps, 256);
            ParallaxRefinementSteps = Math.Clamp(ParallaxRefinementSteps, 0, 16);

            ParallaxFadeStart = Math.Clamp(ParallaxFadeStart, 0.0f, 256.0f);
            ParallaxFadeEnd = Math.Clamp(ParallaxFadeEnd, ParallaxFadeStart, 512.0f);

            ParallaxMaxTexels = Math.Clamp(ParallaxMaxTexels, 0.0f, 16.0f);

            ParallaxDebugMode = Math.Clamp(ParallaxDebugMode, 0, 4);

            NormalDepthBake ??= new NormalDepthBakeConfig();
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WorldProbeClipmapConfig
    {
        /// <summary>
        /// World units between probes at L0.
        /// </summary>
        [JsonProperty]
        public float ClipmapBaseSpacing { get; set; } = 1.5f;

        /// <summary>
        /// Per-level resolution of the clipmap grid.
        /// This initial implementation uses a cubic grid: (N×N×N).
        /// </summary>
        [JsonProperty]
        public int ClipmapResolution { get; set; } = 20;

        /// <summary>
        /// Number of clipmap levels (L0..L{N-1}).
        /// </summary>
        [JsonProperty]
        public int ClipmapLevels { get; set; } = 3;

        /// <summary>
        /// World-probe octahedral tile size (S) used for the radiance atlas.
        /// Must match shader compilation defines and resource allocation.
        /// </summary>
        [JsonProperty]
        public int OctahedralTileSize { get; set; } = 16;

        /// <summary>
        /// Number of octahedral texels traced/uploaded per probe update (K).
        /// Direction slicing uses this to spread S×S updates across multiple frames.
        /// </summary>
        [JsonProperty]
        public int AtlasTexelsPerUpdate { get; set; } = 32;

        /// <summary>
        /// Enables CPU-side direction PIS for world-probe updates (optional).
        /// When enabled, direction selection favors a basis-driven importance proxy while preserving coverage.
        /// Default: false (legacy batch slicing).
        /// </summary>
        [JsonProperty]
        public bool EnableDirectionPIS { get; set; } = true;

        /// <summary>
        /// Exploration fraction (0..1) for world-probe direction PIS.
        /// Used when <see cref="DirectionPISExploreCount"/> is negative.
        /// </summary>
        [JsonProperty]
        public float DirectionPISExploreFraction { get; set; } = 0.25f;

        /// <summary>
        /// Exploration count for world-probe direction PIS.
        /// If negative, derive from <see cref="DirectionPISExploreFraction"/>.
        /// </summary>
        [JsonProperty]
        public int DirectionPISExploreCount { get; set; } = -1;

        /// <summary>
        /// Numerical epsilon for importance weights.
        /// </summary>
        [JsonProperty]
        public float DirectionPISWeightEpsilon { get; set; } = 1e-6f;

        /// <summary>
        /// Per-level max number of probes selected for CPU update per frame.
        /// Expected to be length == <see cref="ClipmapLevels"/>.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int[] PerLevelProbeUpdateBudget { get; set; } = [64, 32, 16, 8];

        /// <summary>
        /// Global CPU cap for the number of probes traced per frame across all levels.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int TraceMaxProbesPerFrame { get; set; } = 128;

        /// <summary>
        /// Global GPU upload bandwidth cap for world-probe updates.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int UploadBudgetBytesPerFrame { get; set; } = 4 * 1024 * 1024;

        [JsonIgnore]
        public int LevelsClamped => Math.Clamp(ClipmapLevels, 1, 8);

        internal void Sanitize()
        {
            ClipmapBaseSpacing = Math.Clamp(ClipmapBaseSpacing, 0.25f, 64.0f);
            ClipmapResolution = Math.Clamp(ClipmapResolution, 8, 128);
            ClipmapLevels = Math.Clamp(ClipmapLevels, 1, 8);
            OctahedralTileSize = Math.Clamp(OctahedralTileSize, 8, 64);
            int dirCount = OctahedralTileSize * OctahedralTileSize;
            AtlasTexelsPerUpdate = Math.Clamp(AtlasTexelsPerUpdate, 1, Math.Max(1, dirCount));

            DirectionPISExploreFraction = Math.Clamp(DirectionPISExploreFraction, 0.0f, 1.0f);
            DirectionPISExploreCount = Math.Clamp(DirectionPISExploreCount, -1, Math.Max(1, dirCount));
            DirectionPISWeightEpsilon = Math.Clamp(DirectionPISWeightEpsilon, 1e-12f, 1.0f);
            TraceMaxProbesPerFrame = Math.Clamp(TraceMaxProbesPerFrame, 0, 65_536);
            UploadBudgetBytesPerFrame = Math.Clamp(UploadBudgetBytesPerFrame, 0, 64 * 1024 * 1024);

            int levels = ClipmapLevels;

            if (PerLevelProbeUpdateBudget is null || PerLevelProbeUpdateBudget.Length == 0)
            {
                PerLevelProbeUpdateBudget = new int[levels];
                int b = 256;
                for (int i = 0; i < levels; i++)
                {
                    PerLevelProbeUpdateBudget[i] = Math.Clamp(b, 0, 65_536);
                    b = Math.Max(1, b >> 1);
                }
                return;
            }

            if (PerLevelProbeUpdateBudget.Length != levels)
            {
                int[] resized = new int[levels];
                int copy = Math.Min(levels, PerLevelProbeUpdateBudget.Length);
                Array.Copy(PerLevelProbeUpdateBudget, resized, copy);

                int fallback = copy > 0 ? resized[copy - 1] : 256;
                for (int i = copy; i < levels; i++)
                {
                    fallback = Math.Max(1, fallback >> 1);
                    resized[i] = fallback;
                }

                PerLevelProbeUpdateBudget = resized;
            }

            for (int i = 0; i < PerLevelProbeUpdateBudget.Length; i++)
            {
                PerLevelProbeUpdateBudget[i] = Math.Clamp(PerLevelProbeUpdateBudget[i], 0, 65_536);
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TextureStreamingConfig
    {
        /// <summary>
        /// Enables PBO-based texture streaming uploads (persistent mapped ring when supported,
        /// otherwise triple-buffered PBOs). When disabled, uploads can still fall back to direct
        /// glTexSubImage* calls if <see cref="AllowDirectUploads"/> is enabled.
        /// </summary>
        [JsonProperty]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Allows direct (non-PBO) uploads when staging is unavailable or oversized.
        /// </summary>
        [JsonProperty]
        public bool AllowDirectUploads { get; set; } = false;

        /// <summary>
        /// Forces persistent mapped buffers off even if GL_ARB_buffer_storage is supported.
        /// Useful for driver workarounds.
        /// </summary>
        [JsonProperty]
        public bool ForceDisablePersistent { get; set; } = false;

        /// <summary>
        /// Uses coherent persistent mapping when supported; when false uses explicit flushes.
        /// </summary>
        [JsonProperty]
        public bool UseCoherentMapping { get; set; } = true;

        /// <summary>
        /// Maximum number of texture sub-region uploads per frame.
        /// </summary>
        [JsonProperty]
        public int MaxUploadsPerFrame { get; set; } = 256;

        /// <summary>
        /// Maximum total bytes uploaded per frame.
        /// </summary>
        [JsonProperty]
        public int MaxBytesPerFrame { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// CPU time budget (ms) for applying staged texture uploads per frame.
        /// This guards against hitching when many small uploads are queued.
        /// Set to 0 to disable time-based limiting.
        /// </summary>
        [JsonProperty]
        public float MaxFrameBudgetMs { get; set; } = 0.5f;

        /// <summary>
        /// Maximum upload byte size eligible for PBO staging.
        /// Larger uploads can fall back to direct upload if enabled.
        /// </summary>
        [JsonProperty]
        public int MaxStagingBytes { get; set; } = 8 * 1024 * 1024;

        /// <summary>
        /// Persistent-mapped ring buffer size in bytes (when supported).
        /// </summary>
        [JsonProperty]
        public int PersistentRingBytes { get; set; } = 32 * 1024 * 1024;

        /// <summary>
        /// Per-PBO allocation size in bytes for the triple-buffered fallback backend.
        /// </summary>
        [JsonProperty]
        public int TripleBufferBytes { get; set; } = 8 * 1024 * 1024;

        /// <summary>
        /// Byte alignment for persistent ring allocations.
        /// </summary>
        [JsonProperty]
        public int PboAlignment { get; set; } = 256;

        internal void Sanitize()
        {
            MaxUploadsPerFrame = Math.Clamp(MaxUploadsPerFrame, 0, 8192);
            MaxBytesPerFrame = Math.Clamp(MaxBytesPerFrame, 0, 256 * 1024 * 1024);
            MaxFrameBudgetMs = Math.Clamp(MaxFrameBudgetMs, 0.0f, 50.0f);
            MaxStagingBytes = Math.Clamp(MaxStagingBytes, 0, 256 * 1024 * 1024);
            PersistentRingBytes = Math.Clamp(PersistentRingBytes, 1, 512 * 1024 * 1024);
            TripleBufferBytes = Math.Clamp(TripleBufferBytes, 1, 256 * 1024 * 1024);
            PboAlignment = Math.Clamp(PboAlignment, 1, 65_536);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LumOnSettingsConfig
    {
        // ═══════════════════════════════════════════════════════════════
        // Feature Toggle
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Master enable for LumOn.
        /// </summary>
        [JsonProperty]
        public bool Enabled { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════
        // Probe Grid Settings (SPG-001)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Spacing between probes in pixels. Lower = more probes = higher quality.
        /// Recommended: 8 (start), 4 (high quality), 16 (performance)
        /// Requires restart to change.
        /// </summary>
        [JsonProperty]
        public int ProbeSpacingPx { get; set; } = 4;

        /// <summary>
        /// Enables deterministic per-frame jitter of the probe anchor sample location.
        /// This reduces structured aliasing and helps temporal accumulation converge.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool AnchorJitterEnabled { get; set; } = true;

        /// <summary>
        /// Jitter amount as a fraction of probe cell size.
        /// The maximum offset in pixels is: ProbeSpacingPx * AnchorJitterScale.
        /// Recommended range: 0.0 .. 0.49
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float AnchorJitterScale { get; set; } = 0.01f;

        /// <summary>
        /// PMJ jitter cycle length (number of frames before the sequence repeats).
        /// Higher values reduce visible repetition at the cost of a slightly larger GPU texture.
        /// Requires restart to change.
        /// </summary>
        [JsonProperty]
        public int PmjJitterCycleLength { get; set; } = 256;

        /// <summary>
        /// Seed for the PMJ jitter sequence.
        /// Requires restart to change.
        /// </summary>
        [JsonProperty]
        public uint PmjJitterSeed { get; set; } = 0xA5B35705u;

        // ═══════════════════════════════════════════════════════════════
        // Ray Tracing Settings (SPG-004)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gather strategy for the screen-probe atlas.
        /// Option B uses an atlas→SH projection pass, then runs the cheap SH gather.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public ProbeAtlasGatherMode ProbeAtlasGather { get; set; } = ProbeAtlasGatherMode.EvaluateProjectedSH;

        /// <summary>
        /// Coarse mip level used for HZB early rejection in the tracer.
        /// 0 = full resolution, higher = coarser.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int HzbCoarseMip { get; set; } = 3;

        /// <summary>
        /// Number of probe-atlas texels to trace per probe per frame.
        /// With 64 texels total (8×8), 8 texels/frame = full coverage in 8 frames.
        /// </summary>
        [JsonProperty]
        public int ProbeAtlasTexelsPerFrame { get; set; } = 16;

        // ═══════════════════════════════════════════════════════════════
        // Phase 22 - LumonScene Surface Cache (Near/Far Fields)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// LumonScene surface cache settings.
        /// </summary>
        [JsonProperty]
        public LumonSceneConfig LumonScene { get; set; } = new();

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class LumonSceneConfig
        {
            /// <summary>
            /// Enables the Phase 22 LumonScene surface cache (experimental).
            /// </summary>
            [JsonProperty]
            public bool Enabled { get; set; } = true;

            /// <summary>
            /// Near-field voxel surface-cache resolution expressed as texels per voxel face edge (mip 0).
            /// Default: 4 (4x4 texels per voxel face).
            /// </summary>
            [JsonProperty]
            public int NearTexelsPerVoxelFaceEdge { get; set; } = 4;

            /// <summary>
            /// Far-field voxel surface-cache resolution expressed as texels per voxel face edge (mip 0).
            /// Default: 1 (1x1 texels per voxel face).
            /// </summary>
            [JsonProperty]
            public int FarTexelsPerVoxelFaceEdge { get; set; } = 1;

            /// <summary>
            /// Near-field radius expressed in chunk distance (Chebyshev distance in chunk coordinates).
            /// </summary>
            [JsonProperty]
            public int NearRadiusChunks { get; set; } = 8;

            /// <summary>
            /// Far-field radius expressed in chunk distance (Chebyshev distance in chunk coordinates).
            /// Must be &gt;= <see cref="NearRadiusChunks"/>.
            /// </summary>
            [JsonProperty]
            public int FarRadiusChunks { get; set; } = 32;

            /// <summary>
            /// Trace scene settings (occupancy clipmap + update budgets).
            /// </summary>
            [JsonProperty]
            public TraceSceneConfig TraceScene { get; set; } = new();

            [JsonObject(MemberSerialization.OptIn)]
            public sealed class TraceSceneConfig
            {
                /// <summary>
                /// Trace scene occupancy clipmap resolution (per level, per axis).
                /// Larger values increase update cost and VRAM. Power-of-two values are recommended.
                /// </summary>
                [JsonProperty]
                public int ClipmapResolution { get; set; } = 64;

                /// <summary>
                /// Trace scene occupancy clipmap level count.
                /// Level spacing is <c>1 &lt;&lt; level</c> blocks (v1).
                /// </summary>
                [JsonProperty]
                public int ClipmapLevels { get; set; } = 5;

                /// <summary>
                /// Max number of clipmap slice uploads per frame.
                /// Higher values converge faster but increase CPU cost and GL traffic.
                /// </summary>
                [JsonProperty]
                public int ClipmapSlicesPerFrame { get; set; } = 8;

                /// <summary>
                /// Max number of 32^3 region payloads to upload per frame for the GPU-built clipmap path (Phase 23).
                /// This is a CPU->GPU bandwidth limiter; it does not directly control compute cost.
                /// </summary>
                [JsonProperty]
                public int ClipmapMaxRegionUploadsPerFrame { get; set; } = 8;

                /// <summary>
                /// Max number of 32^3 region updates to dispatch per frame for the GPU-built clipmap path (Phase 23).
                /// This is a compute limiter; it may be further constrained by the dispatcher batch capacity.
                /// </summary>
                [JsonProperty]
                public int ClipmapMaxRegionsDispatchedPerFrame { get; set; } = 8;

                internal void Sanitize()
                {
                    ClipmapResolution = SanitizeTraceSceneClipmapResolution(ClipmapResolution);
                    ClipmapLevels = Math.Clamp(ClipmapLevels, 1, 8);
                    ClipmapSlicesPerFrame = Math.Clamp(ClipmapSlicesPerFrame, 0, 512);
                    ClipmapMaxRegionUploadsPerFrame = Math.Clamp(ClipmapMaxRegionUploadsPerFrame, 0, 4096);
                    ClipmapMaxRegionsDispatchedPerFrame = Math.Clamp(ClipmapMaxRegionsDispatchedPerFrame, 0, 4096);
                }
            }

            /// <summary>
            /// Max number of surface-cache pages relit per frame (Near field only in v1).
            /// </summary>
            [JsonProperty]
            public int RelightMaxPagesPerFrame { get; set; } = 4;

            /// <summary>
            /// Number of texels to relight per page per frame.
            /// 0 disables relight; values &gt;= tileTexelCount relight all texels every frame.
            /// </summary>
            [JsonProperty]
            public int RelightTexelsPerPagePerFrame { get; set; } = 64;

            /// <summary>
            /// Rays per relit texel (v1 default 1).
            /// </summary>
            [JsonProperty]
            public int RelightRaysPerTexel { get; set; } = 1;

            /// <summary>
            /// Max voxel steps for the DDA tracer (per ray).
            /// </summary>
            [JsonProperty]
            public int RelightMaxDdaSteps { get; set; } = 64;

            internal void Sanitize()
            {
                NearTexelsPerVoxelFaceEdge = SanitizeTexelsPerVoxelFaceEdge(NearTexelsPerVoxelFaceEdge);
                FarTexelsPerVoxelFaceEdge = SanitizeTexelsPerVoxelFaceEdge(FarTexelsPerVoxelFaceEdge);

                NearRadiusChunks = Math.Clamp(NearRadiusChunks, 0, 128);
                FarRadiusChunks = Math.Clamp(FarRadiusChunks, 0, 128);
                if (FarRadiusChunks < NearRadiusChunks)
                {
                    FarRadiusChunks = NearRadiusChunks;
                }

                TraceScene ??= new TraceSceneConfig();
                TraceScene.Sanitize();

                RelightMaxPagesPerFrame = Math.Clamp(RelightMaxPagesPerFrame, 0, 256);
                RelightTexelsPerPagePerFrame = Math.Clamp(RelightTexelsPerPagePerFrame, 0, 4096 * 4096);
                RelightRaysPerTexel = Math.Clamp(RelightRaysPerTexel, 0, 64);
                RelightMaxDdaSteps = Math.Clamp(RelightMaxDdaSteps, 0, 512);
            }

            private static int SanitizeTexelsPerVoxelFaceEdge(int v)
            {
                // Keep tile division exact for 4096x4096 physical atlases:
                // tileSize = (texelsPerVoxelFaceEdge * patchSizeVoxels) must evenly divide 4096.
                // With patchSizeVoxels = 4, this means texelsPerVoxelFaceEdge must divide 1024.
                // v1 restricts to power-of-two values within [1..64].
                v = Math.Clamp(v, 1, 64);
                return (int)BitOperations.RoundUpToPowerOf2((uint)v);
            }

            private static int SanitizeTraceSceneClipmapResolution(int v)
            {
                // v1: keep power-of-two to simplify ring-buffer math and keep memory predictable.
                v = Math.Clamp(v, 16, 128);
                return (int)BitOperations.RoundUpToPowerOf2((uint)v);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Phase 10 - Product Importance Sampling (PIS)
        // ═══════════════════════════════════════════════════════════════

        // Design note:
        // These settings are intentionally shader compilation defines.
        // Hot-reload is supported, but changes will trigger async shader recompiles.

        /// <summary>
        /// Enables Product Importance Sampling (PIS) for selecting which probe-atlas texels to update.
        /// When enabled, direction updates are biased toward high estimated radiance directions while
        /// maintaining a deterministic exploration fraction for long-term coverage.
        /// Hot-reloadable (triggers shader recompile).
        /// </summary>
        [JsonProperty]
        public bool EnableProbePIS { get; set; } = true;

        /// <summary>
        /// Exploration fraction for PIS direction selection. This is converted to an integer number of
        /// exploration texels per probe as: round(ProbeAtlasTexelsPerFrame * ProbePISExploreFraction).
        /// Hot-reloadable (triggers shader recompile).
        /// </summary>
        [JsonProperty]
        public float ProbePISExploreFraction { get; set; } = 0.25f;

        /// <summary>
        /// Optional exploration count override for PIS.
        /// When set to -1, <see cref="ProbePISExploreFraction"/> is used.
        /// Hot-reloadable (triggers shader recompile).
        /// </summary>
        [JsonProperty]
        public int ProbePISExploreCount { get; set; } = -1;

        /// <summary>
        /// Minimum weight applied to history confidence when computing PIS importance.
        /// 0 = confidence can fully zero out weights, 1 = confidence ignored (always 1).
        /// Hot-reloadable (triggers shader recompile).
        /// </summary>
        [JsonProperty]
        public float ProbePISMinConfidenceWeight { get; set; } = 0.1f;

        /// <summary>
        /// Small positive epsilon used for numerical stability in PIS importance computations.
        /// Hot-reloadable (triggers shader recompile).
        /// </summary>
        [JsonProperty]
        public float ProbePISWeightEpsilon { get; set; } = 1e-6f;

        /// <summary>
        /// Number of steps per ray during screen-space marching.
        /// </summary>
        [JsonProperty]
        public int RaySteps { get; set; } = 10;

        /// <summary>
        /// Maximum ray travel distance in world units (meters).
        /// </summary>
        [JsonProperty]
        public float RayMaxDistance { get; set; } = 2.0f;

        /// <summary>
        /// Thickness of ray for depth comparison (view-space units).
        /// </summary>
        [JsonProperty]
        public float RayThickness { get; set; } = 0.1f;

        // ═══════════════════════════════════════════════════════════════
        // Temporal Settings (SPG-005/006)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Temporal blend factor. Higher = more stable but slower response.
        /// 0.95 = 95% history, 5% new data per frame.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float TemporalAlpha { get; set; } = 0.95f;

        /// <summary>
        /// Depth discontinuity threshold for edge detection.
        /// Used to identify edges between probes. Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float DepthDiscontinuityThreshold { get; set; } = 0.05f;

        // ═══════════════════════════════════════════════════════════════
        // Phase 14 - Reprojection / Velocity Settings
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enables using the generated velocity texture for temporal reprojection.
        /// When disabled, temporal passes may fall back to their legacy reprojection path.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool EnableReprojectionVelocity { get; set; } = true;

        /// <summary>
        /// Reject (and/or down-weight) history when the screen-space motion magnitude exceeds this threshold.
        /// Units are UV delta per frame.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float VelocityRejectThreshold { get; set; } = 0.01f;

        /// <summary>
        /// Camera translation distance (in world units) beyond which temporal history is invalidated.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float CameraTeleportResetThreshold { get; set; } = 50.0f;

        // ═══════════════════════════════════════════════════════════════
        // Quality Settings (SPG-007/008)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Run gather pass at half resolution. Recommended for performance.
        /// Requires restart to change.
        /// </summary>
        [JsonProperty]
        public bool HalfResolution { get; set; } = true;

        /// <summary>
        /// Enable edge-aware denoising during upsample.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool DenoiseEnabled { get; set; } = true;

        /// <summary>
        /// Intensity multiplier for final indirect diffuse output.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float Intensity { get; set; } = 1.0f;

        /// <summary>
        /// Additional multiplier applied to emissive when used as an indirect light source.
        /// This does not change the direct emissive buffer (PBR composite); it only affects
        /// probe tracing where emissive becomes radiance.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float EmissiveGiBoost { get; set; } = 3.0f;

        /// <summary>
        /// Tint color applied to indirect bounce lighting.
        /// Use to shift GI color tone. Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float[] IndirectTint { get; set; } = [1.0f, 1.0f, 1.0f];

        /// <summary>
        /// Weight applied to sky/miss samples during ray tracing.
        /// Lower = less sky influence. Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float SkyMissWeight { get; set; } = 0.1f;

        // ═══════════════════════════════════════════════════════════════
        // Edge-Aware Gather Settings (SPG-007 Section 2.3)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Controls depth similarity falloff for edge-aware probe weighting.
        /// Higher values = more lenient depth matching (less edge detection).
        /// Lower values = stricter depth matching (sharper edges).
        /// Default: 0.5
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float GatherDepthSigma { get; set; } = 0.5f;

        /// <summary>
        /// Controls normal similarity power for edge-aware probe weighting.
        /// Higher values = stricter normal matching (sharper creases).
        /// Lower values = more lenient normal matching.
        /// Default: 8.0
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float GatherNormalSigma { get; set; } = 8.0f;

        // ═══════════════════════════════════════════════════════════════
        // Upsample Settings (SPG-008 Section 3.1)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Depth similarity sigma for bilateral upsample.
        /// Controls how strictly depth differences affect upsampling.
        /// Default: 0.1
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float UpsampleDepthSigma { get; set; } = 0.1f;

        /// <summary>
        /// Normal similarity power for bilateral upsample.
        /// Controls how strictly normal differences affect upsampling.
        /// Default: 16.0
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float UpsampleNormalSigma { get; set; } = 16.0f;

        /// <summary>
        /// Spatial kernel sigma for optional spatial denoise.
        /// Controls blur radius of spatial filter.
        /// Default: 1.0
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float UpsampleSpatialSigma { get; set; } = 1.0f;

        /// <summary>
        /// Enables bounded hole filling during upsample for low-confidence indirect values.
        /// Uses the alpha channel written by the gather pass as a confidence metric.
        /// Default: true
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool UpsampleHoleFillEnabled { get; set; } = true;

        /// <summary>
        /// Half-res neighborhood radius used for hole filling.
        /// Default: 2
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int UpsampleHoleFillRadius { get; set; } = 4;

        /// <summary>
        /// Minimum confidence required for neighbor samples to be used during hole filling.
        /// Default: 0.05
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float UpsampleHoleFillMinConfidence { get; set; } = 0.05f;

        // ═══════════════════════════════════════════════════════════════
        // Integration Settings (SPG-009)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Whether to use the full combine pass for lighting integration.
        /// When true: Uses lumon_combine shader with proper albedo/metallic modulation.
        /// When false: Uses simple additive blend in upsample pass (faster, less accurate).
        /// 
        /// The combine pass provides:
        /// - Proper albedo modulation (indirect * albedo)
        /// - Metallic rejection (metals don't receive diffuse indirect)
        /// - More control over tinting and intensity
        /// 
        /// Default: false (additive mode for compatibility)
        /// Requires G-buffer albedo/material textures when enabled.
        /// </summary>
        [JsonProperty]
        public bool UseCombinePass { get; set; } = true;

        /// <summary>
        /// Enables physically-plausible indirect compositing (diffuse/spec split) in the combine pass.
        /// When disabled, uses the legacy diffuse-only indirect modulation.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool EnablePbrComposite { get; set; } = true;

        /// <summary>
        /// Enables applying ambient occlusion during indirect compositing.
        /// NOTE: AO is currently stubbed (no-op). gBufferMaterial.a is reflectivity, not AO.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool EnableAO { get; set; } = true;

        /// <summary>
        /// Enables a short-range AO visibility direction for indirect compositing.
        /// Not yet wired to a dedicated short-range AO source; currently falls back to surface normal.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool EnableBentNormal { get; set; } = true;

        [JsonIgnore]
        public bool EnableShortRangeAo
        {
            get => EnableBentNormal;
            set => EnableBentNormal = value;
        }

        /// <summary>
        /// Strength of AO applied to indirect diffuse.
        /// 0 = disabled, 1 = full AO.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float DiffuseAOStrength { get; set; } = 1.0f;

        /// <summary>
        /// Strength of AO applied to indirect specular.
        /// 0 = disabled, 1 = full AO (also attenuated by roughness).
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float SpecularAOStrength { get; set; } = 0.5f;

        // ═══════════════════════════════════════════════════════════════
        // Probe-Atlas Gather Settings (SPG-007 Section 2.5)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Leak prevention threshold for probe-atlas gather.
        /// If probe hit distance exceeds pixel depth × (1 + threshold),
        /// the contribution is reduced to prevent light leaking.
        /// Default: 0.5 (50% tolerance)
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float ProbeAtlasLeakThreshold { get; set; } = 0.5f;

        /// <summary>
        /// Sample stride for probe-atlas hemisphere integration.
        /// 1 = full quality (64 samples per probe, ~1.2ms)
        /// 2 = performance mode (16 samples per probe, ~0.5ms)
        /// Default: 2
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public int ProbeAtlasSampleStride { get; set; } = 2;

        // ═══════════════════════════════════════════════════════════════
        // Debug Settings
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Debug visualization mode:
        /// See <see cref="LumOnDebugMode"/> for available modes.
        /// </summary>
        [JsonIgnore]
        public LumOnDebugMode DebugMode { get; set; } = LumOnDebugMode.Off;

        /// <summary>
        /// Debug override: forces the probe-atlas trace mask to be uniform (all directions eligible).
        /// Intended for validating mask consumption paths.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool ForceUniformMask { get; set; } = false;

        /// <summary>
        /// Debug override: forces legacy batch slicing even when PIS is enabled.
        /// Intended for A/B comparisons without changing other settings.
        /// Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public bool ForceBatchSlicing { get; set; } = false;

        internal void Sanitize()
        {
            ProbeSpacingPx = Math.Clamp(ProbeSpacingPx, 1, 64);
            AnchorJitterScale = Math.Clamp(AnchorJitterScale, 0.0f, 0.49f);
            PmjJitterCycleLength = Math.Clamp(PmjJitterCycleLength, 1, 65_536);
            HzbCoarseMip = Math.Clamp(HzbCoarseMip, 0, 12);
            ProbeAtlasTexelsPerFrame = Math.Clamp(ProbeAtlasTexelsPerFrame, 1, 64);

            LumonScene ??= new LumonSceneConfig();
            LumonScene.Sanitize();

            ProbePISExploreFraction = Math.Clamp(ProbePISExploreFraction, 0.0f, 1.0f);
            ProbePISExploreCount = Math.Clamp(ProbePISExploreCount, -1, 64);
            ProbePISMinConfidenceWeight = Math.Clamp(ProbePISMinConfidenceWeight, 0.0f, 1.0f);
            ProbePISWeightEpsilon = Math.Clamp(ProbePISWeightEpsilon, 1e-8f, 1.0f);

            RaySteps = Math.Clamp(RaySteps, 1, 512);
            RayMaxDistance = Math.Clamp(RayMaxDistance, 0.25f, 256.0f);
            RayThickness = Math.Clamp(RayThickness, 0.01f, 16.0f);

            TemporalAlpha = Math.Clamp(TemporalAlpha, 0.0f, 1.0f);
            DepthDiscontinuityThreshold = Math.Clamp(DepthDiscontinuityThreshold, 0.0f, 10.0f);

            VelocityRejectThreshold = Math.Clamp(VelocityRejectThreshold, 0.0f, 1.0f);
            CameraTeleportResetThreshold = Math.Clamp(CameraTeleportResetThreshold, 0.0f, 10_000.0f);

            Intensity = Math.Clamp(Intensity, 0.0f, 16.0f);
            EmissiveGiBoost = Math.Clamp(EmissiveGiBoost, 0.0f, 64.0f);
            SkyMissWeight = Math.Clamp(SkyMissWeight, 0.0f, 1.0f);

            GatherDepthSigma = Math.Clamp(GatherDepthSigma, 0.0f, 10.0f);
            GatherNormalSigma = Math.Clamp(GatherNormalSigma, 0.0f, 64.0f);

            UpsampleDepthSigma = Math.Clamp(UpsampleDepthSigma, 0.0f, 10.0f);
            UpsampleNormalSigma = Math.Clamp(UpsampleNormalSigma, 0.0f, 128.0f);
            UpsampleSpatialSigma = Math.Clamp(UpsampleSpatialSigma, 0.0f, 32.0f);
            UpsampleHoleFillRadius = Math.Clamp(UpsampleHoleFillRadius, 0, 16);
            UpsampleHoleFillMinConfidence = Math.Clamp(UpsampleHoleFillMinConfidence, 0.0f, 1.0f);

            DiffuseAOStrength = Math.Clamp(DiffuseAOStrength, 0.0f, 1.0f);
            SpecularAOStrength = Math.Clamp(SpecularAOStrength, 0.0f, 1.0f);

            ProbeAtlasLeakThreshold = Math.Clamp(ProbeAtlasLeakThreshold, 0.0f, 10.0f);
            ProbeAtlasSampleStride = Math.Clamp(ProbeAtlasSampleStride, 1, 8);

            IndirectTint ??= [1.0f, 1.0f, 1.0f];
            if (IndirectTint.Length != 3)
            {
                IndirectTint = [1.0f, 1.0f, 1.0f];
            }
        }
    }

    /// <summary>
    /// Debug settings (including persisted debug-view activation state).
    /// Persisted under: Debug
    /// </summary>
    [JsonProperty]
    public DebugConfig Debug { get; set; } = new();

    /// <summary>
    /// Configuration for LumOn (screen-probe gather) settings.
    /// Persisted under: LumOn
    /// </summary>
    [JsonProperty]
    public LumOnSettingsConfig LumOn { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════
    // PBR Sidecar Atlas Textures (VGE)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration for VGE's material atlas + normal/depth sidecar atlases.
    /// Persisted under: MaterialAtlas
    /// </summary>
    [JsonProperty]
    public MaterialAtlasConfig MaterialAtlas { get; set; } = new();

    // ════════════════════════════════════════════════════════════════════════
    // Texture Streaming (PBO)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration for PBO-based texture streaming uploads.
    /// Persisted to: ModConfig/VanillaGraphicsExpanded.json
    /// </summary>
    [JsonProperty]
    public TextureStreamingConfig TextureStreaming { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════
    // Phase 18 - World-Space Clipmap Probes (Config)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration for the Phase 18 world-probe clipmap system.
    /// </summary>
    [JsonProperty]
    public WorldProbeClipmapConfig WorldProbeClipmap { get; set; } = new();


    /// <summary>
    /// Called after JSON deserialization to ensure nested config objects are initialized.
    /// </summary>
    [OnDeserialized]
    internal void OnDeserializedMethod(StreamingContext context)
    {
        // Ensure nested config objects are initialized
        Debug ??= new DebugConfig();
        Debug.DebugViews ??= new DebugConfig.DebugViewsConfig();
        LumOn ??= new LumOnSettingsConfig();
        TextureStreaming ??= new TextureStreamingConfig();
        WorldProbeClipmap ??= new WorldProbeClipmapConfig();
        MaterialAtlas ??= new MaterialAtlasConfig();
        MaterialAtlas.NormalDepthBake ??= new NormalDepthBakeConfig();
    }

    /// <summary>
    /// Clamps settings to safe bounds. Intended to be called after loading or live reload.
    /// </summary>
    public void Sanitize()
    {
        Debug ??= new DebugConfig();
        Debug.Sanitize();

        LumOn ??= new LumOnSettingsConfig();
        LumOn.Sanitize();

        MaterialAtlas ??= new MaterialAtlasConfig();
        MaterialAtlas.Sanitize();

        TextureStreaming ??= new TextureStreamingConfig();
        TextureStreaming.Sanitize();

        // Debug-view migrations: keep old enum values stable, but redirect removed/deprecated modes.
        WorldProbeClipmap ??= new WorldProbeClipmapConfig();
        WorldProbeClipmap.Sanitize();
    }
}
