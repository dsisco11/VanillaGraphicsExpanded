# LumOn Core Architecture

> **Document**: LumOn.01-Core-Architecture.md  
> **Status**: Draft  
> **Dependencies**: None (root document)  
> **Implements**: Foundation for SPG-001 through SPG-009

---

## 1. Overview

LumOn is a **Screen-Space Radiance Cache** system implementing Screen Probe Gather (SPG) for efficient indirect diffuse lighting in Vintage Story. It replaces per-pixel ray marching with a probe-based approach where:

1. A sparse grid of **screen-space probes** is placed over the viewport
2. Each probe traces rays and accumulates radiance into **Spherical Harmonics (SH L1)**
3. Pixels **gather** irradiance by interpolating neighboring probes
4. **Temporal accumulation** stabilizes results across frames

### 1.1 Design Goals

| Goal                           | Approach                                             |
| ------------------------------ | ---------------------------------------------------- |
| Lower cost than per-pixel SSGI | Probe grid reduces ray count by `probeSpacing²`      |
| Stable indirect lighting       | SH encoding + temporal accumulation                  |
| Soft falloff at edges          | Bilinear probe interpolation with edge-aware weights |
| Extensible to world-space      | Architecture supports future voxel DDA fallback      |

### 1.2 Integration Strategy

LumOn is implemented as **SSGI v2** with a feature flag toggle:

- When `LumOnConfig.Enabled = true`: LumOn pipeline runs
- When `LumOnConfig.Enabled = false`: Existing `SSGIRenderer` runs
- Both share the same G-buffer inputs and output to `IndirectDiffuse` texture

---

## 2. Configuration System

### 2.1 LumOnConfig Class

```csharp
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.LumOn
{
    /// <summary>
    /// Configuration for LumOn Screen Probe Gather system.
    /// Persisted to: ModConfig/vanillagraphicsexpanded-lumon.json
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class LumOnConfig
    {
        // ═══════════════════════════════════════════════════════════════
        // Feature Toggle
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Master enable for LumOn. When false, falls back to legacy SSGI.
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
        public int ProbeSpacingPx { get; set; } = 8;

        // ═══════════════════════════════════════════════════════════════
        // Ray Tracing Settings (SPG-004)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Number of rays traced per probe per frame.
        /// More rays = faster convergence but higher cost.
        /// </summary>
        [JsonProperty]
        public int RaysPerProbePerFrame { get; set; } = 8;

        /// <summary>
        /// Number of steps per ray during screen-space marching.
        /// </summary>
        [JsonProperty]
        public int RaySteps { get; set; } = 12;

        /// <summary>
        /// Maximum ray travel distance in world units (meters).
        /// </summary>
        [JsonProperty]
        public float RayMaxDistance { get; set; } = 10.0f;

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
        public float TemporalAlpha { get; set; } = 0.95f;

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
        /// Tint color applied to indirect bounce lighting.
        /// Use to shift GI color tone. Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float[] IndirectTint { get; set; } = new float[] { 1.0f, 1.0f, 1.0f };

        /// <summary>
        /// Weight applied to sky/miss samples during ray tracing.
        /// Lower = less sky influence. Hot-reloadable.
        /// </summary>
        [JsonProperty]
        public float SkyMissWeight { get; set; } = 0.5f;

        // ═══════════════════════════════════════════════════════════════
        // Debug Settings
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Debug visualization mode:
        /// 0 = Off, 1 = Probe grid, 2 = Raw radiance, 3 = Temporal weight,
        /// 4 = Rejection mask, 5 = SH coefficients
        /// </summary>
        [JsonProperty]
        public int DebugMode { get; set; } = 0;
    }
}
```

### 2.2 Configuration Loading

```csharp
// In VanillaGraphicsExpandedModSystem.cs

private LumOnConfig lumOnConfig;
private const string LumOnConfigFile = "vanillagraphicsexpanded-lumon.json";

public override void StartClientSide(ICoreClientAPI api)
{
    // Load or create config with defaults
    lumOnConfig = api.LoadModConfig<LumOnConfig>(LumOnConfigFile);
    if (lumOnConfig == null)
    {
        lumOnConfig = new LumOnConfig();
        api.StoreModConfig(lumOnConfig, LumOnConfigFile);
        api.Logger.Notification("[VGE] Created default LumOn config");
    }

    // Register hot-reload watchers for supported params
    RegisterConfigWatchers(api);

    // Initialize LumOn or legacy SSGI based on config
    if (lumOnConfig.Enabled)
    {
        lumOnBufferManager = new LumOnBufferManager(api, lumOnConfig);
        lumOnRenderer = new LumOnRenderer(api, lumOnConfig, lumOnBufferManager, gBufferManager);
        api.Event.RegisterRenderer(lumOnRenderer, EnumRenderStage.AfterPostProcessing, "lumon");
    }
    else
    {
        // Existing SSGI path
        ssgiRenderer = new SSGIRenderer(api, ssgiBufferManager, gBufferManager);
        api.Event.RegisterRenderer(ssgiRenderer, EnumRenderStage.AfterPostProcessing, "ssgi");
    }
}

private void RegisterConfigWatchers(ICoreClientAPI api)
{
    // Hot-reloadable parameters (no buffer resize required)
    // These update lumOnConfig in real-time without restart
}

private void SaveConfig(ICoreClientAPI api)
{
    api.StoreModConfig(lumOnConfig, LumOnConfigFile);
}
```

### 2.3 Config File Location

```
%AppData%/VintagestoryData/ModConfig/vanillagraphicsexpanded-lumon.json
```

Example JSON:

```json
{
  "Enabled": true,
  "ProbeSpacingPx": 8,
  "RaysPerProbePerFrame": 8,
  "RaySteps": 12,
  "RayMaxDistance": 10.0,
  "RayThickness": 0.5,
  "TemporalAlpha": 0.95,
  "DepthRejectThreshold": 0.1,
  "NormalRejectThreshold": 0.8,
  "HalfResolution": true,
  "DenoiseEnabled": true,
  "Intensity": 1.0,
  "IndirectTint": [1.0, 1.0, 1.0],
  "SkyMissWeight": 0.5,
  "DebugMode": 0
}
```

---

## 3. Component Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        VanillaGraphicsExpandedModSystem                      │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐  │
│  │  LumOnConfig    │  │  GBufferManager │  │  LumOnBufferManager         │  │
│  │  (JSON persist) │  │  (existing)     │  │  (probe textures)           │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────────┬──────────────┘  │
│           │                    │                          │                  │
│           └────────────────────┼──────────────────────────┘                  │
│                                │                                             │
│                                ▼                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                         LumOnRenderer                                 │   │
│  │  Implements: IRenderer                                                │   │
│  │  RenderStage: AfterPostProcessing                                     │   │
│  │  RenderOrder: 0.5                                                     │   │
│  │                                                                       │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  │   │
│  │  │ ProbeAnchor │  │ ProbeTrace  │  │  Temporal   │  │   Gather    │  │   │
│  │  │    Pass     │──▶    Pass     │──▶    Pass     │──▶    Pass     │  │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └──────┬──────┘  │   │
│  │                                                            │         │   │
│  │                                                            ▼         │   │
│  │                                                     ┌─────────────┐  │   │
│  │                                                     │  Upsample   │  │   │
│  │                                                     │    Pass     │  │   │
│  │                                                     └─────────────┘  │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Component Responsibilities

| Component              | Responsibility                                               |
| ---------------------- | ------------------------------------------------------------ |
| **LumOnConfig**        | Stores all tuning parameters, persists to JSON               |
| **GBufferManager**     | Provides depth, normal, albedo, material textures (existing) |
| **LumOnBufferManager** | Creates/manages probe grid textures and history buffers      |
| **LumOnRenderer**      | Orchestrates shader passes each frame                        |

### 3.2 Shader Pipeline

| Pass            | Shader                   | Input                          | Output                         |
| --------------- | ------------------------ | ------------------------------ | ------------------------------ |
| 1. Probe Anchor | `lumon_probe_anchor.fsh` | G-Buffer depth, normal         | `ProbeAnchor` texture          |
| 2. Probe Trace  | `lumon_probe_trace.fsh`  | ProbeAnchor, CapturedScene     | `ProbeRadiance_Current`        |
| 3. Temporal     | `lumon_temporal.fsh`     | Current, History, PrevMatrix   | `ProbeRadiance_History` (swap) |
| 4. Gather       | `lumon_gather.fsh`       | ProbeRadiance, G-Buffer        | `IndirectDiffuse_HalfRes`      |
| 5. Upsample     | `lumon_upsample.fsh`     | HalfRes, G-Buffer depth/normal | `IndirectDiffuse_FullRes`      |

---

## 4. Buffer Manager

### 4.1 LumOnBufferManager Class

```csharp
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn
{
    /// <summary>
    /// Manages GPU textures for LumOn probe grid and radiance cache.
    /// </summary>
    public class LumOnBufferManager : IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly LumOnConfig config;

        // Probe grid dimensions (computed from screen size and spacing)
        public int ProbeCountX { get; private set; }
        public int ProbeCountY { get; private set; }

        // Framebuffers
        public FrameBufferRef ProbeAnchorFB { get; private set; }
        public FrameBufferRef ProbeRadianceCurrentFB { get; private set; }
        public FrameBufferRef ProbeRadianceHistoryFB { get; private set; }
        public FrameBufferRef IndirectDiffuseHalfResFB { get; private set; }
        public FrameBufferRef IndirectDiffuseFullResFB { get; private set; }

        // Double-buffer swap index (0 or 1)
        private int currentBufferIndex = 0;

        public LumOnBufferManager(ICoreClientAPI api, LumOnConfig config)
        {
            this.api = api;
            this.config = config;

            CreateBuffers();

            // Re-create on window resize
            api.Event.ReloadShader += OnReloadShader;
        }

        private void CreateBuffers()
        {
            var platform = api.Render;
            int screenW = platform.FrameWidth;
            int screenH = platform.FrameHeight;

            // Calculate probe grid dimensions
            ProbeCountX = (int)Math.Ceiling((float)screenW / config.ProbeSpacingPx);
            ProbeCountY = (int)Math.Ceiling((float)screenH / config.ProbeSpacingPx);

            // Half-res dimensions for gather output
            int halfW = config.HalfResolution ? screenW / 2 : screenW;
            int halfH = config.HalfResolution ? screenH / 2 : screenH;

            // Create probe anchor texture (posVS.xyz + valid, normal.xyz + reserved)
            ProbeAnchorFB = CreateFramebuffer(
                "LumOn_ProbeAnchor",
                ProbeCountX, ProbeCountY,
                new[] { EnumTextureFormat.Rgba16f, EnumTextureFormat.Rgba16f }
            );

            // Create radiance cache textures (SH L1 = 4 coefficients per channel)
            // Using 2 RGBA16F textures: RGB SH coefficients 0-2 in first, coeff 3 in second
            ProbeRadianceCurrentFB = CreateFramebuffer(
                "LumOn_RadianceCurrent",
                ProbeCountX, ProbeCountY,
                new[] { EnumTextureFormat.Rgba16f, EnumTextureFormat.Rgba16f }
            );

            ProbeRadianceHistoryFB = CreateFramebuffer(
                "LumOn_RadianceHistory",
                ProbeCountX, ProbeCountY,
                new[] { EnumTextureFormat.Rgba16f, EnumTextureFormat.Rgba16f }
            );

            // Indirect diffuse output buffers
            IndirectDiffuseHalfResFB = CreateFramebuffer(
                "LumOn_IndirectHalf",
                halfW, halfH,
                new[] { EnumTextureFormat.Rgba16f }
            );

            IndirectDiffuseFullResFB = CreateFramebuffer(
                "LumOn_IndirectFull",
                screenW, screenH,
                new[] { EnumTextureFormat.Rgba16f }
            );

            api.Logger.Notification(
                $"[LumOn] Created buffers: {ProbeCountX}x{ProbeCountY} probes, " +
                $"spacing={config.ProbeSpacingPx}px"
            );
        }

        private FrameBufferRef CreateFramebuffer(
            string name,
            int width,
            int height,
            EnumTextureFormat[] colorFormats)
        {
            // Implementation using VS framebuffer API
            // Similar to existing GBufferManager/SSGIBufferManager
            throw new NotImplementedException("See GBufferManager for pattern");
        }

        /// <summary>
        /// Swap current/history radiance buffers for temporal accumulation.
        /// Called after temporal pass completes.
        /// </summary>
        public void SwapRadianceBuffers()
        {
            var temp = ProbeRadianceCurrentFB;
            ProbeRadianceCurrentFB = ProbeRadianceHistoryFB;
            ProbeRadianceHistoryFB = temp;

            currentBufferIndex = 1 - currentBufferIndex;
        }

        private void OnReloadShader(EnumShaderReloadReason reason)
        {
            if (reason == EnumShaderReloadReason.WindowResized)
            {
                DisposeBuffers();
                CreateBuffers();
            }
        }

        private void DisposeBuffers()
        {
            // Dispose all framebuffers
        }

        public void Dispose()
        {
            api.Event.ReloadShader -= OnReloadShader;
            DisposeBuffers();
        }
    }
}
```

### 4.2 Texture Formats Summary

| Texture          | Dimensions        | Format  | Content                                                     |
| ---------------- | ----------------- | ------- | ----------------------------------------------------------- |
| ProbeAnchor[0]   | probeW × probeH   | RGBA16F | posVS.xyz, valid (0/1)                                      |
| ProbeAnchor[1]   | probeW × probeH   | RGBA16F | normalVS.xyz, reserved                                      |
| ProbeRadiance[0] | probeW × probeH   | RGBA16F | SH coeff 0 (R,G,B), coeff 1.R                               |
| ProbeRadiance[1] | probeW × probeH   | RGBA16F | SH coeff 1 (G,B), coeff 2 (R,G), coeff 2.B, coeff 3 (R,G,B) |
| IndirectHalf     | halfW × halfH     | RGBA16F | Indirect diffuse RGB, AO                                    |
| IndirectFull     | screenW × screenH | RGBA16F | Final upsampled result                                      |

---

## 5. Renderer Implementation

### 5.1 LumOnRenderer Class Skeleton

```csharp
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn
{
    /// <summary>
    /// Main renderer orchestrating LumOn shader passes.
    /// </summary>
    public class LumOnRenderer : IRenderer, IDisposable
    {
        public double RenderOrder => 0.5;
        public int RenderRange => int.MaxValue;

        private readonly ICoreClientAPI api;
        private readonly LumOnConfig config;
        private readonly LumOnBufferManager bufferManager;
        private readonly GBufferManager gBufferManager;

        // Shaders
        private IShaderProgram probeAnchorShader;
        private IShaderProgram probeTraceShader;
        private IShaderProgram temporalShader;
        private IShaderProgram gatherShader;
        private IShaderProgram upsampleShader;

        // Previous frame matrix for reprojection
        private float[] prevViewProjMatrix = new float[16];

        // Frame counter for ray jittering
        private int frameIndex = 0;

        public LumOnRenderer(
            ICoreClientAPI api,
            LumOnConfig config,
            LumOnBufferManager bufferManager,
            GBufferManager gBufferManager)
        {
            this.api = api;
            this.config = config;
            this.bufferManager = bufferManager;
            this.gBufferManager = gBufferManager;

            LoadShaders();
        }

        private void LoadShaders()
        {
            probeAnchorShader = api.Shader.NewShaderProgram();
            // ... load from assets/vanillagraphicsexpanded/shaders/lumon_*.fsh
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.AfterPostProcessing) return;
            if (!config.Enabled) return;

            var render = api.Render;

            // Pass 1: Build probe anchors from G-Buffer
            RenderProbeAnchorPass(render);

            // Pass 2: Trace rays per probe, accumulate SH radiance
            RenderProbeTracePass(render);

            // Pass 3: Temporal accumulation with history
            RenderTemporalPass(render);

            // Pass 4: Gather probes to pixels (half-res)
            RenderGatherPass(render);

            // Pass 5: Upsample to full resolution
            RenderUpsamplePass(render);

            // Store current view-proj for next frame's reprojection
            StoreViewProjMatrix(render);

            // Swap radiance buffers for next frame
            bufferManager.SwapRadianceBuffers();

            frameIndex++;
        }

        private void RenderProbeAnchorPass(IRenderAPI render)
        {
            // Bind ProbeAnchorFB, render fullscreen quad sampling G-Buffer
            // Output: probe positions and normals
        }

        private void RenderProbeTracePass(IRenderAPI render)
        {
            // Bind ProbeRadianceCurrentFB, render fullscreen quad
            // Sample ProbeAnchor, trace rays, accumulate SH
        }

        private void RenderTemporalPass(IRenderAPI render)
        {
            // Bind ProbeRadianceHistoryFB as output
            // Sample Current + History, blend with rejection
        }

        private void RenderGatherPass(IRenderAPI render)
        {
            // Bind IndirectDiffuseHalfResFB
            // For each pixel, interpolate 4 surrounding probes
        }

        private void RenderUpsamplePass(IRenderAPI render)
        {
            // Bind IndirectDiffuseFullResFB
            // Bilateral upsample from half-res
        }

        private void StoreViewProjMatrix(IRenderAPI render)
        {
            // Copy current view-proj to prevViewProjMatrix
            // Used by temporal pass for reprojection
        }

        public void Dispose()
        {
            probeAnchorShader?.Dispose();
            probeTraceShader?.Dispose();
            temporalShader?.Dispose();
            gatherShader?.Dispose();
            upsampleShader?.Dispose();
        }
    }
}
```

---

## 6. Integration Points

### 6.1 Required Uniforms (New)

Add to shader uniform system:

| Uniform                 | Type  | Source             | Used By             |
| ----------------------- | ----- | ------------------ | ------------------- |
| `prevViewProjMatrix`    | mat4  | LumOnRenderer      | Temporal pass       |
| `probeSpacing`          | int   | LumOnConfig        | All passes          |
| `probeGridSize`         | ivec2 | LumOnBufferManager | All passes          |
| `frameIndex`            | int   | LumOnRenderer      | Trace pass (jitter) |
| `temporalAlpha`         | float | LumOnConfig        | Temporal pass       |
| `depthRejectThreshold`  | float | LumOnConfig        | Temporal pass       |
| `normalRejectThreshold` | float | LumOnConfig        | Temporal pass       |

### 6.2 G-Buffer Inputs (Existing)

| Sampler     | Source               | Content                                     |
| ----------- | -------------------- | ------------------------------------------- |
| `gDepth`    | Primary FB depth     | View-space depth                            |
| `gNormal`   | GBuffer attachment 4 | World-space normals                         |
| `gMaterial` | GBuffer attachment 5 | Roughness, metallic, emissive, reflectivity |
| `sceneTex`  | Captured scene       | Lit geometry before post-processing         |

### 6.3 Render Stage Placement

```
EnumRenderStage.Opaque          → Terrain, entities (G-Buffer filled)
EnumRenderStage.OIT             → Transparent geometry
EnumRenderStage.AfterOIT        → Held item
EnumRenderStage.AfterPostProcessing → ★ LumOn runs here (RenderOrder 0.5)
                                     → SSAO at 0.3 (before LumOn)
                                     → Existing SSGI at 0.5 (disabled when LumOn enabled)
EnumRenderStage.AfterBlit       → Final composite to screen
```

---

## 7. File Structure

```
VanillaGraphicsExpanded/
├── LumOn/
│   ├── LumOnConfig.cs              ← Configuration class
│   ├── LumOnBufferManager.cs       ← GPU buffer management
│   └── LumOnRenderer.cs            ← Render pass orchestration
├── assets/vanillagraphicsexpanded/
│   └── shaders/
│       ├── lumon_probe_anchor.fsh  ← Pass 1: Build probe anchors
│       ├── lumon_probe_anchor.vsh
│       ├── lumon_probe_trace.fsh   ← Pass 2: Ray tracing per probe
│       ├── lumon_probe_trace.vsh
│       ├── lumon_temporal.fsh      ← Pass 3: Temporal accumulation
│       ├── lumon_temporal.vsh
│       ├── lumon_gather.fsh        ← Pass 4: Gather probes to pixels
│       ├── lumon_gather.vsh
│       ├── lumon_upsample.fsh      ← Pass 5: Bilateral upsample
│       ├── lumon_upsample.vsh
│       └── lumon_sh.ash            ← SH helper functions (include)
└── docs/
    ├── LumOn.planning.md           ← Original planning doc
    ├── LumOn.01-Core-Architecture.md ← This document
    ├── LumOn.02-Probe-Grid.md
    ├── LumOn.03-Radiance-Cache.md
    ├── LumOn.04-Ray-Tracing.md
    ├── LumOn.05-Temporal.md
    └── LumOn.06-Gather-Upsample.md
```

---

## 8. Debug & Profiling

### 8.1 Performance Counters

Track these metrics for profiling and debugging:

```csharp
// In LumOnRenderer.cs

public class LumOnDebugCounters
{
    /// <summary>Total probes in grid (probeW × probeH)</summary>
    public int TotalProbes { get; set; }

    /// <summary>Probes marked valid this frame</summary>
    public int ValidProbes { get; set; }

    /// <summary>Probes marked as edge (partial validity)</summary>
    public int EdgeProbes { get; set; }

    /// <summary>Total rays traced this frame (validProbes × raysPerProbe)</summary>
    public int RaysTraced { get; set; }

    /// <summary>Rays that hit geometry</summary>
    public int RayHits { get; set; }

    /// <summary>Rays that missed (sky fallback)</summary>
    public int RayMisses { get; set; }

    /// <summary>Hit rate percentage (hits / traced)</summary>
    public float HitRate => RaysTraced > 0 ? (float)RayHits / RaysTraced * 100f : 0f;

    /// <summary>Probes with valid temporal history</summary>
    public int TemporalValidProbes { get; set; }

    /// <summary>Probes rejected (disoccluded)</summary>
    public int TemporalRejectedProbes { get; set; }

    /// <summary>Time spent in each pass (ms)</summary>
    public float ProbeAnchorPassMs { get; set; }
    public float ProbeTracePassMs { get; set; }
    public float TemporalPassMs { get; set; }
    public float GatherPassMs { get; set; }
    public float UpsamplePassMs { get; set; }
    public float TotalFrameMs { get; set; }

    public void Reset()
    {
        ValidProbes = EdgeProbes = RaysTraced = RayHits = RayMisses = 0;
        TemporalValidProbes = TemporalRejectedProbes = 0;
    }
}
```

### 8.2 GPU Query Timing

```csharp
// Use OpenGL timer queries for accurate GPU timing
private int[] queryIds = new int[5];
private bool queryPending = false;

private void BeginTimerQuery(int passIndex)
{
    GL.BeginQuery(QueryTarget.TimeElapsed, queryIds[passIndex]);
}

private void EndTimerQuery()
{
    GL.EndQuery(QueryTarget.TimeElapsed);
}

private float GetQueryResultMs(int passIndex)
{
    GL.GetQueryObject(queryIds[passIndex], GetQueryObjectParam.QueryResult, out long nanoseconds);
    return nanoseconds / 1_000_000f;
}
```

### 8.3 Debug Overlay Display

```csharp
// Show counters in debug HUD (when DebugMode > 0)
private void DrawDebugOverlay(IRenderAPI render)
{
    if (config.DebugMode == 0) return;

    var lines = new[]
    {
        $"LumOn Probes: {counters.ValidProbes}/{counters.TotalProbes} valid, {counters.EdgeProbes} edge",
        $"Rays: {counters.RaysTraced} traced, {counters.HitRate:F1}% hit rate",
        $"Temporal: {counters.TemporalValidProbes} valid, {counters.TemporalRejectedProbes} rejected",
        $"Time: {counters.TotalFrameMs:F2}ms (A:{counters.ProbeAnchorPassMs:F2} T:{counters.ProbeTracePassMs:F2} " +
        $"Tp:{counters.TemporalPassMs:F2} G:{counters.GatherPassMs:F2} U:{counters.UpsamplePassMs:F2})"
    };

    // Render text overlay...
}
```

### 8.4 Atomic Counter for GPU-Side Stats (Future)

For accurate hit/miss counting without readback stalls:

```glsl
// In lumon_probe_trace.fsh (future enhancement)
layout(binding = 0, offset = 0) uniform atomic_uint rayHitCounter;
layout(binding = 0, offset = 4) uniform atomic_uint rayMissCounter;

// On hit:
atomicCounterIncrement(rayHitCounter);

// On miss:
atomicCounterIncrement(rayMissCounter);
```

---

## 9. Next Steps

| Document                                                   | Covers                                      |
| ---------------------------------------------------------- | ------------------------------------------- |
| [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md)           | SPG-001/002: Probe anchor generation        |
| [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md)   | SPG-003: SH encoding and helpers            |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)         | SPG-004: Screen-space ray marching          |
| [LumOn.05-Temporal.md](LumOn.05-Temporal.md)               | SPG-005/006: Reprojection and accumulation  |
| [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md) | SPG-007/008: Pixel gathering and upsampling |

---

## 10. Implementation Checklist

### 10.1 Configuration

- [x] Create `LumOnConfig.cs` with all properties
- [x] Add JSON serialization attributes
- [x] Implement config load/save in mod system
- [x] Add hot-reload support for runtime-changeable params
- [x] Create default config file on first run
- [x] Add `DepthDiscontinuityThreshold` property (edge detection)

### 10.2 Core Components

- [x] Create `LumOn/` folder structure
- [x] Implement `LumOnBufferManager.cs`
- [x] Implement `LumOnRenderer.cs` skeleton
- [x] Register renderer with VS event system
- [x] Add feature toggle (LumOn vs legacy SSGI)
- [x] Implement `LoadShaders()` method
- [x] Implement `GetSkyZenithColor()` / `GetSkyHorizonColor()` helpers
- [x] Implement `GetSunColor()` helper
- [x] Implement `RenderFullscreenQuad()` utility

### 10.3 Debug Infrastructure

- [x] Implement `LumOnDebugCounters` class
- [x] Add GPU timer query support
- [x] Create debug overlay rendering
- [x] Add debug mode switching (0-5)

### 10.4 Integration

- [x] Wire up G-Buffer texture access
- [x] Store previous frame ViewProj matrix
- [x] Connect to final lighting combine pass
- [ ] Test enable/disable toggle works correctly
