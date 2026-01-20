# LumOn Probe Grid System

> **Document**: LumOn.02-Probe-Grid.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md)  
> **Implements**: SPG-001, SPG-002

---

## 1. Overview

The probe grid is a sparse 2D array of sample points placed over the screen. Each probe:

1. **Anchors** to a surface visible in the G-Buffer (position + normal)
2. **Validates** whether it covers meaningful geometry (not sky, not edge)
3. **Serves** as the center for ray tracing and radiance accumulation

### 1.1 Why Screen-Space Probes?

| Approach                | Pros                             | Cons                          |
| ----------------------- | -------------------------------- | ----------------------------- |
| Per-pixel rays          | Highest quality                  | O(pixels × rays) cost         |
| World-space probes      | View-independent                 | Complex placement, streaming  |
| **Screen-space probes** | Adapts to view, O(probes × rays) | Temporal instability at edges |

LumOn uses screen-space probes because:

- Probes are automatically placed where geometry is visible
- Cost scales with screen coverage, not scene complexity
- Natural integration with G-Buffer data

---

## 2. Grid Dimensions (SPG-001)

### 2.1 Calculation

```
ProbeCountX = ceil(screenW / spacing)   // 1920/8 = 240
ProbeCountY = ceil(screenH / spacing)   // 1080/8 = 135
Total probes = 240 × 135 = 32,400       // vs 2,073,600 pixels at 1080p
```

### 2.2 Spacing Trade-offs

| Spacing  | Probe Count (1080p) | Quality      | Cost       |
| -------- | ------------------- | ------------ | ---------- |
| 4 px     | 480 × 270 = 129,600 | High         | High       |
| **8 px** | 240 × 135 = 32,400  | **Balanced** | **Medium** |
| 16 px    | 120 × 68 = 8,160    | Low          | Low        |

**Recommendation**: Start with 8px spacing. Users can adjust via `LumOn.ProbeSpacingPx` config.

### 2.3 Screen-to-Probe Mapping

**Key conversions:**

| Function                | Formula                                                                                                         |
| ----------------------- | --------------------------------------------------------------------------------------------------------------- |
| Screen → Probe          | `probeCoord = screenPixel / spacing`                                                                            |
| Probe → Screen (center) | `screenPixel = probeCoord * spacing + spacing/2`                                                                |
| Bilinear weights        | `probeUV = screenUV * screenSize / spacing - 0.5`<br>`baseProbe = floor(probeUV)`<br>`weights = fract(probeUV)` |

---

## 3. ProbeAnchor Texture Layout

### 3.1 Texture Format

Two RGBA16F textures store probe anchor data:

| Attachment         | Channel | Content    | Range                      |
| ------------------ | ------- | ---------- | -------------------------- |
| **ProbeAnchor[0]** | R       | posWS.x    | world-space meters         |
|                    | G       | posWS.y    | world-space meters         |
|                    | B       | posWS.z    | world-space meters         |
|                    | A       | valid      | 0.0 = invalid, 1.0 = valid |
| **ProbeAnchor[1]** | R       | normalWS.x | [-1, 1] world-space        |
|                    | G       | normalWS.y | [-1, 1] world-space        |
|                    | B       | normalWS.z | [-1, 1] world-space        |
|                    | A       | reserved   | future: material flags     |

### 3.2 Why World-Space?

LumOn stores probe positions and normals in **world-space**, matching UE5 Lumen's
Screen Space Radiance Cache design. This provides critical benefits for temporal stability:

- **Temporal coherence**: World-space directions remain valid across camera rotations.
  View-space directions would require re-tracing or SH rotation when the camera turns.
- **Simpler temporal accumulation**: Radiance stored per world-space direction can be
  directly blended without coordinate transforms.
- **Matches Lumen**: UE5's SIGGRAPH 2022 presentation confirms screen-space probes
  store radiance in world-space directions for exactly these reasons.

> **Note**: Vintage Story's G-buffer stores positions in view-space and normals in world-space.
> The probe anchor pass transforms view-space positions to world-space using `inverseViewMatrix`.
> Normals are already world-space in the G-buffer and can be used directly.

### 3.3 GLSL Output Declaration

```glsl
// lumon_probe_anchor.fsh
layout(location = 0) out vec4 outProbePos;    // posWS.xyz, valid
layout(location = 1) out vec4 outProbeNormal; // normalWS.xyz, reserved
```

---

## 4. Probe Anchor Build Pass (SPG-002)

### 4.1 Pass Overview

The probe anchor pass runs once per frame, before ray tracing:

1. **Input**: G-Buffer depth texture, G-Buffer normal texture
2. **Output**: ProbeAnchor textures (probeW × probeH)
3. **Method**: Fragment shader with probe-resolution viewport

### 4.2 Shader Implementation

**Vertex shader**: Standard fullscreen quad pass-through.

**Fragment shader (pseudocode):**

```
Inputs:  gDepth, gNormal, gPosition (G-Buffer)
         inverseViewMatrix, screenSize, probeSpacing
         depthDiscontinuityThreshold (0.1 recommended)

Outputs: outProbePos    (posWS.xyz, valid)
         outProbeNormal (normalWS.xyz encoded to [0,1], reserved)

main:
    probeCoord = gl_FragCoord.xy
    screenPixel = probeCoord * spacing + spacing/2   // center of probe cell
    screenUV = screenPixel / screenSize

    // Sample G-Buffer, transform position VS→WS
    depth = sampleDepth(screenUV)
    posWS = inverseViewMatrix * samplePositionVS(screenUV)
    normalWS = sampleNormalWS(screenUV)  // already world-space in VS

    // Validation
    valid = 1.0
    if isSky(depth):           valid = 0.0      // no surface
    if hasDepthDiscontinuity:  valid = 0.5      // edge (partial)
    if length(normalWS) < 0.5: valid = 0.0      // invalid normal

    output (posWS, valid), (normalWS encoded, 0)
```

**Helper functions:**

- `linearizeDepth(d)`: Convert [0,1] depth to view-space Z
- `isSky(depth)`: Returns true if depth ≥ 0.9999
- `hasDepthDiscontinuity()`: Sample 4 neighbors, compare linearized depths against relative threshold

### 4.4 Validation Criteria Summary

| Condition           | Result      | Rationale                     |
| ------------------- | ----------- | ----------------------------- |
| Depth ≥ 0.9999      | valid = 0   | Sky has no surface            |
| Depth discontinuity | valid = 0.5 | Edges are temporally unstable |
| Normal length < 0.5 | valid = 0   | Invalid G-Buffer data         |
| Otherwise           | valid = 1   | Good probe                    |

---

## 5. C# Integration

### 5.1 Render Pass (pseudocode)

```
RenderProbeAnchorPass:
    BindFramebuffer(ProbeAnchorFB)
    SetViewport(0, 0, probeCountX, probeCountY)  // probe-resolution
    Clear()

    probeAnchorShader.Use()
    BindTextures: gDepth, gNormal, gPosition
    SetUniforms:  screenSize, probeGridSize, probeSpacing,
                  zNear, zFar, depthDiscontinuityThreshold, inverseViewMatrix
    RenderFullscreenQuad()

    RestoreViewport(screenW, screenH)
```

### 5.2 Shader Program Class

`LumOnProbeAnchorShaderProgram` extends `ShaderProgram` with typed property setters:

| Category      | Uniforms                                       |
| ------------- | ---------------------------------------------- |
| **Textures**  | `GDepth`, `GNormal`, `GPosition` (units 0-2)   |
| **Grid**      | `ScreenSize`, `ProbeGridSize`, `ProbeSpacing`  |
| **Depth**     | `ZNear`, `ZFar`, `DepthDiscontinuityThreshold` |
| **Transform** | `InverseViewMatrix`                            |

Registered via static `Register(api)` method during mod initialization.

---

## 6. Debug Visualization

### 6.1 Debug Mode: Probe Grid (LumOn.DebugMode = 1)

Overlays probe centers colored by validity:

- **Green** (valid > 0.9): Good probe with stable surface
- **Yellow** (valid > 0.4): Edge probe, reduced temporal weight
- **Red** (valid ≤ 0.4): Invalid, sky or bad normal

### 6.2 Debug Output Example

```
┌────────────────────────────────────────────┐
│ ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ●  │
│ ● ● ● ● ● ○ ○ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  ● = valid (green)
│ ● ● ● ● ○ ✕ ✕ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  ○ = edge (yellow)
│ ● ● ● ○ ✕     ✕ ○ ● ● ● ● ● ● ● ● ● ● ●  │  ✕ = invalid (red)
│ ● ● ○ ✕   SKY   ✕ ○ ● ● ● ● ● ● ● ● ● ●  │
│ ● ● ○ ✕         ✕ ○ ● ● ● ● ● ● ● ● ● ●  │  Sky region has no
│ ● ● ● ○ ✕     ✕ ○ ● ● ● ● ● ● ● ● ● ● ●  │  valid probes; edges
│ ● ● ● ● ○ ✕ ✕ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  are marked for
│ ● ● ● ● ● ○ ○ ● ● ● ● ● ● ● ● ● ● ● ● ●  │  reduced temporal
│ ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ●  │  weight
└────────────────────────────────────────────┘
```

---

## 7. Performance Considerations

### 7.1 Texture Fetch Efficiency

The probe anchor pass samples G-Buffer at probe centers only:

- **1080p, 8px spacing**: 32,400 texture fetches (vs 2M for per-pixel)
- **1080p, 4px spacing**: 129,600 texture fetches

### 7.2 Bandwidth

| Texture        | Read/Write | Size (8px, 1080p)           |
| -------------- | ---------- | --------------------------- |
| gDepth         | Read       | 1920×1080 × R32F = 8 MB     |
| gNormal        | Read       | 1920×1080 × RGBA16F = 16 MB |
| gPosition      | Read       | 1920×1080 × RGBA16F = 16 MB |
| ProbeAnchor[0] | Write      | 240×135 × RGBA16F = 0.26 MB |
| ProbeAnchor[1] | Write      | 240×135 × RGBA16F = 0.26 MB |

**Total bandwidth per pass**: ~40 MB read, ~0.5 MB write

### 7.3 Future: Compute Shader Migration (M3)

Migrate to compute shader (`local_size 8×8`) for:

- No rasterization overhead
- Better occupancy for small output sizes
- Potential to combine with probe trace pass

---

## 8. Edge Cases

### 8.1 Window Resize

On `WindowResized` event: dispose buffers → recreate with new `ProbeCountX/Y`.

### 8.2 Sub-Pixel Jittering (Future)

Jitter probe sample position within cell using Squirrel3 hash (±0.25 cells). Blue noise would provide more uniform temporal coverage but requires a precomputed texture.

### 8.3 Invalid Probe Handling

Downstream passes must check `probeData.a < 0.5` and skip invalid probes or use sky fallback.

---

## 9. Next Steps

| Document                                                 | Dependency    | Topic                          |
| -------------------------------------------------------- | ------------- | ------------------------------ |
| [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md) | This document | SH encoding for probe radiance |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)       | 02, 03        | Per-probe ray marching         |
