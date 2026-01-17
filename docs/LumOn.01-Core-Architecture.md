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

### 2.1 LumOnConfig Properties

`LumOnConfig` is a JSON-serialized class persisted to `ModConfig/vanillagraphicsexpanded-lumon.json`.

| Category        | Property                | Type     | Default | Hot-Reload | Description                                           |
| --------------- | ----------------------- | -------- | ------- | ---------- | ----------------------------------------------------- |
| **Feature**     | `Enabled`               | bool     | true    | ✗          | Master toggle; falls back to legacy SSGI when false   |
| **Probe Grid**  | `ProbeSpacingPx`        | int      | 8       | ✗          | Pixels between probes (4=high, 8=balanced, 16=perf)   |
| **Ray Tracing** | `RaysPerProbePerFrame`  | int      | 8       | ✗          | Rays traced per probe per frame                       |
|                 | `RaySteps`              | int      | 12      | ✗          | Steps per ray during screen-space march               |
|                 | `RayMaxDistance`        | float    | 10.0    | ✗          | Max ray distance in world units                       |
|                 | `RayThickness`          | float    | 0.5     | ✗          | Depth comparison thickness (view-space)               |
| **Temporal**    | `TemporalAlpha`         | float    | 0.95    | ✓          | Blend factor (0.95 = 95% history)                     |
| **Temporal**    | `AnchorJitterEnabled`   | bool     | false   | ✓          | Deterministic per-frame jitter of probe anchors       |
| **Temporal**    | `AnchorJitterScale`     | float    | 0.35    | ✓          | Jitter magnitude as a fraction of probe spacing       |
|                 | `DepthRejectThreshold`  | float    | 0.1     | ✓          | Depth diff threshold for rejection                    |
|                 | `NormalRejectThreshold` | float    | 0.8     | ✓          | Normal dot threshold for rejection                    |
| **Quality**     | `HalfResolution`        | bool     | true    | ✗          | Run gather at half-res                                |
|                 | `DenoiseEnabled`        | bool     | true    | ✓          | Edge-aware denoising on upsample                      |
|                 | `Intensity`             | float    | 1.0     | ✓          | Output intensity multiplier                           |
|                 | `IndirectTint`          | float[3] | [1,1,1] | ✓          | RGB tint for indirect bounce                          |
|                 | `SkyMissWeight`         | float    | 0.5     | ✓          | Weight for sky/miss samples                           |
| **Debug**       | `DebugMode`             | int      | 0       | ✓          | 0=Off, 1=Grid, 2=Radiance, 3=Temporal, 4=Reject, 5=SH |

### 2.2 Configuration Loading

**Initialization Flow (pseudocode):**

```text
StartClientSide:
    config = LoadModConfig("vanillagraphicsexpanded-lumon.json")
    if config is null:
        config = new LumOnConfig()  // defaults
        StoreModConfig(config)

    RegisterHotReloadWatchers()  // for runtime-changeable params

    if config.Enabled:
        RegisterRenderer(LumOnRenderer, AfterPostProcessing)
    else:
        RegisterRenderer(SSGIRenderer, AfterPostProcessing)  // legacy fallback
```

### 2.3 Config File Location

```text
%AppData%/VintagestoryData/ModConfig/vanillagraphicsexpanded-lumon.json
```

Example JSON:

```json
{
  "Enabled": true,
  "ProbeSpacingPx": 8,
  "AnchorJitterEnabled": false,
  "AnchorJitterScale": 0.35,
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

For details on the PMJ sequence backing anchor jitter, see:

- [LumOn.PMJ-Jitter.md](LumOn.PMJ-Jitter.md)

---

## 3. Component Architecture

```text
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

### 4.1 LumOnBufferManager Overview

Manages GPU textures for probe grid and radiance cache. Implements `IDisposable`.

**Key Properties:**

- `ProbeCountX`, `ProbeCountY` — Grid dimensions computed as `ceil(screenSize / probeSpacing)`
- Framebuffers: `ProbeAnchorFB`, `ProbeRadianceCurrentFB`, `ProbeRadianceHistoryFB`, `IndirectDiffuseHalfResFB`, `IndirectDiffuseFullResFB`

**Buffer Creation (pseudocode):**

```text
CreateBuffers:
    probeW = ceil(screenW / probeSpacing)
    probeH = ceil(screenH / probeSpacing)
    halfW = HalfResolution ? screenW/2 : screenW
    halfH = HalfResolution ? screenH/2 : screenH

    ProbeAnchorFB       = CreateFB(probeW, probeH, [RGBA16F, RGBA16F])  // pos+valid, normal
    ProbeRadianceCurrentFB  = CreateFB(probeW, probeH, [RGBA16F, RGBA16F])  // SH coeffs
    ProbeRadianceHistoryFB  = CreateFB(probeW, probeH, [RGBA16F, RGBA16F])  // temporal history
    IndirectDiffuseHalfResFB = CreateFB(halfW, halfH, [RGBA16F])
    IndirectDiffuseFullResFB = CreateFB(screenW, screenH, [RGBA16F])

SwapRadianceBuffers:
    swap(ProbeRadianceCurrentFB, ProbeRadianceHistoryFB)  // double-buffer for temporal

OnWindowResize:
    DisposeBuffers()
    CreateBuffers()  // recreate at new resolution
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

### 5.1 LumOnRenderer Overview

Implements `IRenderer` with `RenderOrder = 0.5`, orchestrating all shader passes.

**State:**

- 5 shader programs (anchor, trace, temporal, gather, upsample)
- `prevViewProjMatrix` for temporal reprojection
- `frameIndex` for ray jitter

**Render Loop (pseudocode):**

```text
OnRenderFrame(AfterPostProcessing):
    if not Enabled: return

    Pass 1 - ProbeAnchor:   G-Buffer → probe positions + normals
    Pass 2 - ProbeTrace:    Trace rays per probe → SH radiance
    Pass 3 - Temporal:      Blend current + history with rejection
    Pass 4 - Gather:        Interpolate probes → half-res indirect
    Pass 5 - Upsample:      Bilateral upsample → full-res output

    StoreViewProjMatrix()   // for next frame's reprojection
    SwapRadianceBuffers()   // double-buffer swap
    frameIndex++
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

```text
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

```text
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

`LumOnDebugCounters` tracks these metrics:

| Category     | Counter                  | Description                                                   |
| ------------ | ------------------------ | ------------------------------------------------------------- |
| **Probes**   | `TotalProbes`            | Grid size (probeW × probeH)                                   |
|              | `ValidProbes`            | Probes marked valid this frame                                |
|              | `EdgeProbes`             | Probes at geometry edges                                      |
| **Rays**     | `RaysTraced`             | Total rays (validProbes × raysPerProbe)                       |
|              | `RayHits` / `RayMisses`  | Geometry hits vs sky fallback                                 |
|              | `HitRate`                | Computed: hits / traced × 100%                                |
| **Temporal** | `TemporalValidProbes`    | Probes with valid history                                     |
|              | `TemporalRejectedProbes` | Disoccluded probes                                            |
| **Timing**   | `*PassMs`                | Per-pass GPU time (Anchor, Trace, Temporal, Gather, Upsample) |
|              | `TotalFrameMs`           | Sum of all passes                                             |

### 8.2 GPU Timing

Use OpenGL `GL_TIME_ELAPSED` queries to measure per-pass GPU time. Wrap each pass with `BeginQuery`/`EndQuery` and read results asynchronously to avoid stalls.

### 8.3 Debug Overlay

When `DebugMode > 0`, render a text overlay showing:

- Probe counts (valid/total, edge)
- Ray stats (traced, hit rate)
- Temporal stats (valid, rejected)
- Per-pass timing breakdown

### 8.4 GPU-Side Stats (Future)

Use GLSL atomic counters (`atomic_uint`) in the trace shader for accurate hit/miss counting without CPU readback stalls.

---

## 9. Next Steps

| Document                                                   | Covers                                      |
| ---------------------------------------------------------- | ------------------------------------------- |
| [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md)           | SPG-001/002: Probe anchor generation        |
| [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md)   | SPG-003: SH encoding and helpers            |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)         | SPG-004: Screen-space ray marching          |
| [LumOn.05-Temporal.md](LumOn.05-Temporal.md)               | SPG-005/006: Reprojection and accumulation  |
| [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md) | SPG-007/008: Pixel gathering and upsampling |
