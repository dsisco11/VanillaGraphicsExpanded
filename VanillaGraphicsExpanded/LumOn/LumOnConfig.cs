using System;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Configuration for LumOn Screen Probe Gather system.
/// Persisted to: ModConfig/vanillagraphicsexpanded-lumon.json
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class LumOnConfig
{
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
    public sealed class WorldProbeClipmapConfig
    {
        /// <summary>
        /// World units between probes at L0.
        /// </summary>
        [JsonProperty]
        public float ClipmapBaseSpacing { get; set; } = 2.0f;

        /// <summary>
        /// Per-level resolution of the clipmap grid.
        /// This initial implementation uses a cubic grid: (N×N×N).
        /// </summary>
        [JsonProperty]
        public int ClipmapResolution { get; set; } = 16;

        /// <summary>
        /// Number of clipmap levels (L0..L{N-1}).
        /// </summary>
        [JsonProperty]
        public int ClipmapLevels { get; set; } = 4;

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
        public int UploadBudgetBytesPerFrame { get; set; } = 2 * 1024 * 1024;

        [JsonIgnore]
        public int LevelsClamped => Math.Clamp(ClipmapLevels, 1, 8);

        internal void Sanitize()
        {
            ClipmapBaseSpacing = Math.Clamp(ClipmapBaseSpacing, 0.25f, 64.0f);
            ClipmapResolution = Math.Clamp(ClipmapResolution, 8, 128);
            ClipmapLevels = Math.Clamp(ClipmapLevels, 1, 8);
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

    // ═══════════════════════════════════════════════════════════════
    // Feature Toggle
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Master enable for LumOn.
    /// </summary>
    [JsonProperty]
    public bool Enabled { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════
    // PBR Sidecar Atlas Textures (VGE)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables progressive async population of the material params atlas (RGB16F).
    /// When disabled, the atlas is populated synchronously during loading.
    /// </summary>
    [JsonProperty]
    public bool EnableMaterialAtlasAsyncBuild { get; set; } = true;

    /// <summary>
    /// Per-frame time budget (ms) for the async material atlas scheduler.
    /// This caps render-thread work (job dispatch + texture sub-region uploads).
    /// </summary>
    [JsonProperty]
    public float MaterialAtlasAsyncBudgetMs { get; set; } = 1.5f;

    /// <summary>
    /// Maximum number of texture sub-region uploads per frame.
    /// Limits GL work and reduces hitching.
    /// </summary>
    [JsonProperty]
    public int MaterialAtlasAsyncMaxUploadsPerFrame { get; set; } = 8;

    /// <summary>
    /// Maximum number of CPU tile jobs dispatched per frame.
    /// Limits task churn and keeps background work paced.
    /// </summary>
    [JsonProperty]
    public int MaterialAtlasAsyncMaxCpuJobsPerFrame { get; set; } = 2;

    /// <summary>
    /// Per-frame time budget (ms) for the async normal+depth atlas jobs.
    /// This caps render-thread work for normal+depth uploads/bakes.
    /// </summary>
    [JsonProperty]
    public float NormalDepthAtlasAsyncBudgetMs { get; set; } = 0.75f;

    /// <summary>
    /// Maximum number of normal+depth atlas jobs executed per frame.
    /// Limits GL work and reduces hitching during the bake.
    /// </summary>
    [JsonProperty]
    public int NormalDepthAtlasAsyncMaxUploadsPerFrame { get; set; } = 2;

    /// <summary>
    /// Enables building and binding the VGE normal+depth sidecar atlas.
    /// Pixels are generated during loading from tileable albedo textures (per texture rect).
    /// Requires restart / re-entering the world to fully apply.
    /// </summary>
    [JsonProperty]
    public bool EnableNormalDepthAtlas { get; set; } = true;

    /// <summary>
    /// Enables the material atlas disk cache.
    /// When enabled, material params and normal+depth tiles can be loaded from and persisted to disk
    /// so subsequent sessions can skip expensive work.
    /// </summary>
    [JsonProperty]
    public bool EnableMaterialAtlasDiskCache { get; set; } = true;

    /// <summary>
    /// Enables debug logging for disk cache hit/miss counters.
    /// </summary>
    [JsonProperty]
    public bool DebugLogMaterialAtlasDiskCache { get; set; } = false;

    /// <summary>
    /// Shows an in-game progress panel while the material atlas is building.
    /// Intended for development/diagnostics; off by default.
    /// </summary>
    [JsonProperty]
    public bool ShowMaterialAtlasProgressPanel { get; set; } = true;

    /// <summary>
    /// Enables parallax mapping in patched vanilla chunk shaders.
    /// Requires shader reload / re-entering the world to fully apply.
    /// </summary>
    [JsonProperty]
    public bool EnableParallaxMapping { get; set; } = false;

    /// <summary>
    /// Parallax UV offset scale (in atlas UV units).
    /// This is intentionally small; typical range: 0.005 .. 0.05.
    /// </summary>
    [JsonProperty]
    public float ParallaxScale { get; set; } = 0.03f;

    /// <summary>
    /// Parameters for generating a tileable height/normal field from albedo.
    /// Applied during loading when <see cref="EnableNormalDepthAtlas"/> is enabled.
    /// </summary>
    [JsonProperty]
    public NormalDepthBakeConfig NormalDepthBake { get; set; } = new();

    /// <summary>
    /// Enables additional debug logging for normal+depth atlas build/bind plumbing.
    /// </summary>
    [JsonProperty]
    public bool DebugLogNormalDepthAtlas { get; set; } = true;

    // ════════════════════════════════════════════════════════════════════════
    // Texture Streaming (PBO)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables PBO-based texture streaming uploads (persistent mapped ring when supported,
    /// otherwise triple-buffered PBOs). When disabled, uploads can still fall back to direct
    /// glTexSubImage* calls if <see cref="TextureStreamingAllowDirectUploads"/> is enabled.
    /// </summary>
    [JsonProperty]
    public bool TextureStreamingEnabled { get; set; } = true;

    /// <summary>
    /// Allows direct (non-PBO) uploads when staging is unavailable or oversized.
    /// </summary>
    [JsonProperty]
    public bool TextureStreamingAllowDirectUploads { get; set; } = true;

    /// <summary>
    /// Forces persistent mapped buffers off even if GL_ARB_buffer_storage is supported.
    /// Useful for driver workarounds.
    /// </summary>
    [JsonProperty]
    public bool TextureStreamingForceDisablePersistent { get; set; } = false;

    /// <summary>
    /// Uses coherent persistent mapping when supported; when false uses explicit flushes.
    /// </summary>
    [JsonProperty]
    public bool TextureStreamingUseCoherentMapping { get; set; } = true;

    /// <summary>
    /// Maximum number of texture sub-region uploads per frame.
    /// </summary>
    [JsonProperty]
    public int TextureStreamingMaxUploadsPerFrame { get; set; } = 8;

    /// <summary>
    /// Maximum total bytes uploaded per frame.
    /// </summary>
    [JsonProperty]
    public int TextureStreamingMaxBytesPerFrame { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Maximum upload byte size eligible for PBO staging.
    /// Larger uploads can fall back to direct upload if enabled.
    /// </summary>
    [JsonProperty]
    public int TextureStreamingMaxStagingBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Persistent-mapped ring buffer size in bytes (when supported).
    /// </summary>
    [JsonProperty]
    public int TextureStreamingPersistentRingBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Per-PBO allocation size in bytes for the triple-buffered fallback backend.
    /// </summary>
    [JsonProperty]
    public int TextureStreamingTripleBufferBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Byte alignment for persistent ring allocations.
    /// </summary>
    [JsonProperty]
    public int TextureStreamingPboAlignment { get; set; } = 256;

    // ═══════════════════════════════════════════════════════════════
    // Phase 18 - World-Space Clipmap Probes (Config)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration for the Phase 18 world-probe clipmap system.
    /// </summary>
    [JsonProperty]
    public WorldProbeClipmapConfig WorldProbeClipmap { get; set; } = new();

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
    public bool AnchorJitterEnabled { get; set; } = false;

    /// <summary>
    /// Jitter amount as a fraction of probe cell size.
    /// The maximum offset in pixels is: ProbeSpacingPx * AnchorJitterScale.
    /// Recommended range: 0.0 .. 0.49
    /// Hot-reloadable.
    /// </summary>
    [JsonProperty]
    public float AnchorJitterScale { get; set; } = 0.15f;

    /// <summary>
    /// PMJ jitter cycle length (number of frames before the sequence repeats).
    /// Higher values reduce visible repetition at the cost of a slightly larger GPU texture.
    /// Requires restart to change.
    /// </summary>
    [JsonProperty]
    public int PmjJitterCycleLength { get; set; } = 1024;

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
    /// Use a screen-probe atlas (directional probe cache) instead of SH L1.
    /// Implementation detail: octahedral mapping per-probe tile.
    /// Provides better temporal stability and per-direction hit distance.
    /// </summary>
    [JsonProperty]
    public bool UseProbeAtlas { get; set; } = true;

    /// <summary>
    /// Gather strategy when <see cref="UseProbeAtlas"/> is enabled.
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
    public int HzbCoarseMip { get; set; } = 4;

    /// <summary>
    /// Number of probe-atlas texels to trace per probe per frame.
    /// With 64 texels total (8×8), 8 texels/frame = full coverage in 8 frames.
    /// Only used when <see cref="UseProbeAtlas"/> is true.
    /// </summary>
    [JsonProperty]
    public int ProbeAtlasTexelsPerFrame { get; set; } = 16;

    /// <summary>
    /// Number of rays traced per probe per frame (SH mode only).
    /// More rays = faster convergence but higher cost.
    /// </summary>
    [JsonProperty]
    public int RaysPerProbePerFrame { get; set; } = 12;

    /// <summary>
    /// Number of steps per ray during screen-space marching.
    /// </summary>
    [JsonProperty]
    public int RaySteps { get; set; } = 10;

    /// <summary>
    /// Maximum ray travel distance in world units (meters).
    /// </summary>
    [JsonProperty]
    public float RayMaxDistance { get; set; } = 4.0f;

    /// <summary>
    /// Thickness of ray for depth comparison (view-space units).
    /// </summary>
    [JsonProperty]
    public float RayThickness { get; set; } = 0.5f;

    // ═══════════════════════════════════════════════════════════════
    // Temporal Settings (SPG-005/006)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Temporal blend factor. Higher = more stable but slower response.
    /// 0.95 = 95% history, 5% new data per frame.
    /// Hot-reloadable.
    /// </summary>
    [JsonProperty]
    public float TemporalAlpha { get; set; } = 0.2f;

    /// <summary>
    /// Depth difference threshold for history rejection (view-space).
    /// Hot-reloadable.
    /// </summary>
    [JsonProperty]
    public float DepthRejectThreshold { get; set; } = 0.1f;

    /// <summary>
    /// Normal angle threshold for history rejection (dot product).
    /// Values below this reject history. Hot-reloadable.
    /// </summary>
    [JsonProperty]
    public float NormalRejectThreshold { get; set; } = 0.8f;

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
    /// Enables using the generated velocity texture (Phase 14) for temporal reprojection.
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
    public float SkyMissWeight { get; set; } = 0.5f;

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
    public int UpsampleHoleFillRadius { get; set; } = 2;

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
    /// Clamps settings to safe bounds. Intended to be called after loading or live reload.
    /// </summary>
    public void Sanitize()
    {
        // Keep existing behavior for NaNs: clamp/guards are conservative.
        ProbeSpacingPx = Math.Clamp(ProbeSpacingPx, 1, 64);
        AnchorJitterScale = Math.Clamp(AnchorJitterScale, 0.0f, 0.49f);
        PmjJitterCycleLength = Math.Clamp(PmjJitterCycleLength, 1, 65_536);
        HzbCoarseMip = Math.Clamp(HzbCoarseMip, 0, 12);
        ProbeAtlasTexelsPerFrame = Math.Clamp(ProbeAtlasTexelsPerFrame, 1, 64);
        RaysPerProbePerFrame = Math.Clamp(RaysPerProbePerFrame, 1, 256);
        RaySteps = Math.Clamp(RaySteps, 1, 512);
        RayMaxDistance = Math.Clamp(RayMaxDistance, 0.25f, 256.0f);
        RayThickness = Math.Clamp(RayThickness, 0.01f, 16.0f);

        TemporalAlpha = Math.Clamp(TemporalAlpha, 0.0f, 1.0f);
        DepthRejectThreshold = Math.Clamp(DepthRejectThreshold, 0.0f, 10.0f);
        NormalRejectThreshold = Math.Clamp(NormalRejectThreshold, -1.0f, 1.0f);
        DepthDiscontinuityThreshold = Math.Clamp(DepthDiscontinuityThreshold, 0.0f, 10.0f);

        VelocityRejectThreshold = Math.Clamp(VelocityRejectThreshold, 0.0f, 1.0f);
        CameraTeleportResetThreshold = Math.Clamp(CameraTeleportResetThreshold, 0.0f, 10_000.0f);

        MaterialAtlasAsyncBudgetMs = Math.Clamp(MaterialAtlasAsyncBudgetMs, 0.0f, 100.0f);
        MaterialAtlasAsyncMaxUploadsPerFrame = Math.Clamp(MaterialAtlasAsyncMaxUploadsPerFrame, 0, 512);
        MaterialAtlasAsyncMaxCpuJobsPerFrame = Math.Clamp(MaterialAtlasAsyncMaxCpuJobsPerFrame, 0, 512);
        NormalDepthAtlasAsyncBudgetMs = Math.Clamp(NormalDepthAtlasAsyncBudgetMs, 0.0f, 100.0f);
        NormalDepthAtlasAsyncMaxUploadsPerFrame = Math.Clamp(NormalDepthAtlasAsyncMaxUploadsPerFrame, 0, 512);

        TextureStreamingMaxUploadsPerFrame = Math.Clamp(TextureStreamingMaxUploadsPerFrame, 0, 8192);
        TextureStreamingMaxBytesPerFrame = Math.Clamp(TextureStreamingMaxBytesPerFrame, 0, 256 * 1024 * 1024);
        TextureStreamingMaxStagingBytes = Math.Clamp(TextureStreamingMaxStagingBytes, 0, 256 * 1024 * 1024);
        TextureStreamingPersistentRingBytes = Math.Clamp(TextureStreamingPersistentRingBytes, 1, 512 * 1024 * 1024);
        TextureStreamingTripleBufferBytes = Math.Clamp(TextureStreamingTripleBufferBytes, 1, 256 * 1024 * 1024);
        TextureStreamingPboAlignment = Math.Clamp(TextureStreamingPboAlignment, 1, 65_536);

        ParallaxScale = Math.Clamp(ParallaxScale, 0.0f, 0.25f);

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

        WorldProbeClipmap ??= new WorldProbeClipmapConfig();
        WorldProbeClipmap.Sanitize();
    }
}
