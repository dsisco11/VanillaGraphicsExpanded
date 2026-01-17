# LumOn Ray Tracing Pass

> **Document**: LumOn.04-Ray-Tracing.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md), [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md)  
> **Implements**: SPG-004

---

## 1. Overview

The ray tracing pass is the core of LumOn. Two modes are supported:

### 1.1 Octahedral Mode (Default)

For each probe's octahedral atlas tile:

1. **Select** a subset of texels to trace this frame (temporal distribution)
2. **Convert** texel coordinates to world-space ray direction
3. **March** the ray through screen-space using depth buffer
4. **Store** radiance + hit distance directly to the atlas texel
5. **Preserve** non-traced texels from history

### 1.2 Legacy SH Mode (Fallback)

For each valid probe:

1. **Generate** `N` ray directions distributed over the hemisphere
2. **March** each ray through screen-space using depth buffer
3. **Sample** hit radiance from the captured scene texture
4. **Accumulate** radiance into SH coefficients
5. **Handle** misses with sky/ambient fallback

### 1.3 Key Differences from Per-Pixel SSGI

| Aspect           | Per-Pixel SSGI | LumOn Probe Tracing   |
| ---------------- | -------------- | --------------------- |
| Rays per frame   | pixels × rays  | probes × rays         |
| At 1080p, 8 rays | 16.6M rays     | 259K rays (64× fewer) |
| Output           | Direct RGB     | Octahedral or SH      |
| Reuse            | Per-pixel      | Shared by ~64 pixels  |

---

## 2. Temporal Ray Distribution (Octahedral Mode)

The octahedral radiance cache stores 64 directions per probe (8×8 texels). Tracing all 64
directions every frame would be expensive. Instead, we use **temporal distribution**:

### 2.1 Concept

- Trace only a **subset** of directions per probe each frame
- Over multiple frames, all 64 directions are updated
- Non-traced texels **preserve their history value**
- Per-probe **jitter** prevents coherent flickering (backed by a PMJ sequence; see [LumOn.PMJ-Jitter.md](LumOn.PMJ-Jitter.md))

### 2.2 Configuration

| Parameter                  | Default  | Description                       |
| -------------------------- | -------- | --------------------------------- |
| `ProbeAtlasTexelsPerFrame` | 8        | Texels traced per probe per frame |
| Full coverage time         | 8 frames | 64 texels ÷ 8 texels/frame        |

### 2.3 Batch Selection Algorithm

```text
texelIndex = octTexel.y * 8 + octTexel.x  // 0-63
batch = texelIndex / texelsPerFrame       // Which batch (0-7)
jitteredFrame = (frameIndex + probeIndex) % numBatches
traceThisFrame = (batch == jitteredFrame)
```

### 2.4 Temporal Distribution Visualization

Frame progression for a single probe (8 texels/frame):

```text
Frame 0: Trace texels 0-7   (batch 0)
Frame 1: Trace texels 8-15  (batch 1)
Frame 2: Trace texels 16-23 (batch 2)
...
Frame 7: Trace texels 56-63 (batch 7)
Frame 8: Back to batch 0 (with updated radiance)
```

With per-probe jitter, neighboring probes trace different batches each frame,
reducing temporal aliasing artifacts.

### 2.5 History Preservation

For non-traced texels, the shader reads from the history texture and outputs it unchanged.
Only texels selected for tracing this frame perform the ray march.

---

## 3. Ray Direction Generation

### 3.1 Octahedral Mode: Fixed World-Space Directions

In octahedral mode, ray directions are **fixed in world-space** and determined by the texel's
position in the octahedral map via `octahedralUVToDirection(texelUV)`.

This provides **temporal stability**: the same texel always represents the same world-space
direction, regardless of camera orientation. When the camera rotates, the probe's radiance
cache remains valid—no rotation or reprojection needed.

### 3.2 Legacy SH Mode: Hemisphere Distribution

Rays should be distributed over the hemisphere oriented by the probe's surface normal. Requirements:

- **Uniform coverage**: No clustering or gaps
- **Low discrepancy**: Quasi-random for smooth convergence
- **Temporal jitter**: Different samples each frame

### 3.3 Ray Distribution (SH Mode)

**Hammersley Sequence**: Low-discrepancy 2D sequence using Van der Corput radical inverse.
Returns `(i/N, radicalInverse(i))` for uniform coverage.

**Cosine-Weighted Hemisphere Mapping**:

```text
phi = 2π * xi.x
cosTheta = sqrt(1 - xi.y)
sinTheta = sqrt(xi.y)
localDir = (sinTheta*cos(phi), sinTheta*sin(phi), cosTheta)
```

**Orient Around Normal**: Build tangent frame from normal, transform local direction to world space.

**Temporal Jittering**: Hash `(probeCoord, frameIndex)` to produce per-probe jitter offset,
rotate Hammersley samples to prevent temporal patterns.

---

## 4. Screen-Space Ray Marching

### 4.1 Algorithm Overview

```text
1. Start at probe position (view-space) with small offset along ray direction
2. For each step:
   a. Advance ray position by stepSize
   b. Project to screen UV; exit if out of bounds
   c. Sample depth buffer; skip if sky
   d. Compare ray depth to scene depth
   e. If ray is behind scene AND within thickness threshold → HIT
3. If no hit found → MISS (use sky fallback)
```

### 4.2 Key Parameters

| Parameter        | Typical Value | Purpose                           |
| ---------------- | ------------- | --------------------------------- |
| `raySteps`       | 12            | Number of march steps             |
| `rayMaxDistance` | 10.0 m        | Maximum ray travel distance       |
| `rayThickness`   | 0.5 m         | Depth tolerance for hit detection |

### 4.3 Hit Detection

```text
rayDepth = -rayPos.z  // Positive depth into screen
sceneDepth = linearizeDepth(depthSample)

if rayDepth > sceneDepth AND rayDepth < sceneDepth + thickness:
    HIT: sample radiance from sceneTex at hitUV
```

### 4.4 Hierarchical Refinement (Optional)

For better precision without increasing step count:

1. **Coarse pass**: Large steps to find potential hit region
2. **Binary search**: Refine hit position within found region (4 iterations)

---

## 5. Hit Shading & Fallback

### 5.1 Hit Radiance

On hit, sample the captured scene texture at `hitUV`. Optionally boost emissive surfaces.

### 5.2 Sky Fallback

For missed rays:

```text
upness = dir.y * 0.5 + 0.5
skyColor = mix(horizonColor, zenithColor, upness)
sunFactor = pow(max(dot(dir, sunDir), 0), 32)
result = skyColor + sunColor * sunFactor * sunIntensity
```

### 5.3 Distance Falloff

Attenuate hit contribution: `falloff = 1 / (1 + distance²)`

---

## 6. Shader Structure

### 6.1 Key Uniforms

| Category     | Uniforms                                                                    |
| ------------ | --------------------------------------------------------------------------- |
| Probe data   | `probeAnchorPos`, `probeAnchorNormal`                                       |
| Scene        | `gDepth`, `sceneTex`, `projection`                                          |
| Ray config   | `raysPerProbe`, `raySteps`, `rayMaxDistance`, `rayThickness`                |
| Sky/lighting | `skyColorZenith`, `skyColorHorizon`, `sunDirection`, `sunColor`, `dayLight` |
| Tuning       | `skyMissWeight`, `indirectTint`                                             |
| Temporal     | `frameIndex`                                                                |

### 6.2 Main Loop (Pseudo Code)

```text
for each probe (fragment):
    load anchor (posVS, normalVS, valid)
    if invalid: output zero

    for i in 0..raysPerProbe:
        xi = jitteredHammersley(i, rayCount, frameIndex, probeCoord)
        rayDir = orientHemisphere(cosineSampleHemisphere(xi), normalVS)

        hit = marchRay(posVS, rayDir)

        if hit:
            radiance = hit.radiance * indirectTint * falloff(hit.distance)
            weight = 1.0
        else:
            radiance = sampleSky(rayDir)
            weight = skyMissWeight

        weight *= dot(normalVS, rayDir)  // cosine weight
        shAccumulate(shR, shG, shB, project(rayDir, radiance * weight))
        totalWeight += weight

    shNormalize(shR, shG, shB, totalWeight)
    shClampNegative(shR, shG, shB)
    packToOutputTextures(shR, shG, shB)
```

### 6.3 Vertex Shader

Simple fullscreen quad: pass-through position to clip space, compute texcoord.

---

## 7. C# Integration

The render pass:

1. Binds radiance output framebuffer (probe resolution)
2. Binds probe anchor textures + depth + scene capture
3. Sets ray config uniforms (`raysPerProbe`, `raySteps`, `rayMaxDistance`, `rayThickness`)
4. Sets sky/lighting uniforms from VS render state
5. Renders fullscreen quad
6. Restores viewport to screen resolution

---

## 8. Performance Budget

### 7.1 Cost Analysis

| Component       | Per-Probe Cost   | Total (32K probes) |
| --------------- | ---------------- | ------------------ |
| Ray directions  | 8 × ~20 ALU      | 5M ALU ops         |
| Ray march steps | 8 × 12 × ~50 ALU | 154M ALU ops       |
| Texture samples | 8 × 12 × 2       | 6M samples         |
| SH accumulation | 8 × ~30 ALU      | 7.7M ALU ops       |

**Total**: ~167M ALU ops, 6M texture samples per frame

Compared to per-pixel SSGI at 1080p:

- SSGI: 2M pixels × 8 rays × 12 steps = 192M ray steps
- LumOn: 32K probes × 8 rays × 12 steps = 3M ray steps
- **64× reduction in ray marching**

### 7.2 Optimization Opportunities

1. **Half-rate tracing**: Trace only half the probes per frame, alternate checkerboard
2. **Variable ray count**: Fewer rays for edge probes (valid = 0.5)
3. **Early exit**: Stop marching if ray depth exceeds max scene depth
4. **Mipmap depth**: Use depth mips for coarse pass

---

## 9. Debug Visualization

**Debug Mode 2 (Raw Radiance)**: Render probe DC term as colored dots at probe centers.

---

## 10. Next Steps

| Document                                     | Dependency    | Topic                                |
| -------------------------------------------- | ------------- | ------------------------------------ |
| [LumOn.05-Temporal.md](LumOn.05-Temporal.md) | This document | Temporal accumulation & reprojection |
