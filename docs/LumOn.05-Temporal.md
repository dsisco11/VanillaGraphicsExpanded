# LumOn Temporal System

> **Document**: LumOn.05-Temporal.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)  
> **Implements**: SPG-005, SPG-006

---

## 1. Overview

Temporal accumulation is critical for LumOn because:

1. **Few rays per frame**: 8 rays per probe produce noisy results
2. **Convergence over time**: Accumulating ~20 frames ≈ 160 rays worth of data
3. **Stability**: Reduces flickering during camera motion

### 1.1 Challenges

| Challenge     | Solution                                     |
| ------------- | -------------------------------------------- |
| Camera motion | Reproject probes to previous frame positions |
| Disocclusion  | Detect newly visible surfaces, reset history |
| Ghosting      | Reject invalid history based on depth/normal |
| Lag           | Balance accumulation speed vs stability      |

### 1.2 Pipeline

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ Current Frame   │     │ Reproject       │     │ Validate        │
│ Radiance        │────▶│ Probe Position  │────▶│ History Sample  │
│ (from trace)    │     │ to Prev Screen  │     │ (depth/normal)  │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                                                        │
                                                        ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ Output to       │◀────│ Blend Current   │◀────│ Neighborhood    │
│ History Buffer  │     │ + History       │     │ Clamping        │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

---

## 2. Previous Frame Matrix

### 2.1 Storing ViewProj Matrix

Each frame, store the current view and projection matrices (using `System.Numerics.Matrix4x4`) before rendering. Compute combined `prevViewProjMatrix = view * proj` for reprojection.

Provide helper functions `ToMatrix4x4(float[])` and `ToFloatArray(Matrix4x4)` to convert between VS's column-major arrays and System.Numerics format.

### 2.2 First Frame Handling

On first frame (or after teleport), clear history buffers and skip temporal blending. Use `isFirstFrame` flag to detect this state.

---

## 3. Reprojection

### 3.1 World-Space Approach

Probes store view-space positions. To reproject:

```
posWS = invViewMatrix * posVS
prevClip = prevViewProjMatrix * posWS
historyUV = (prevClip.xy / prevClip.w) * 0.5 + 0.5
```

### 3.2 Camera Motion Handling

- **Translation**: Causes parallax (depth-dependent screen motion)—handled automatically by world-space reprojection
- **Rotation**: Screen positions change but world positions don't—may cause many probes to fall outside previous frame bounds

---

## 4. History Validation

History is validated with three checks, returning both validity and confidence (0-1):

### 4.1 Validation Checks

| Check  | Condition                                                                            | Threshold Example |
| ------ | ------------------------------------------------------------------------------------ | ----------------- |
| Bounds | `historyUV` within [0,1]²                                                            | N/A               |
| Depth  | `relDiff < threshold` where relDiff = \|currentDepth - historyDepth\| / currentDepth | 0.1 (10%)         |
| Normal | `dot(current, history) > threshold`                                                  | 0.8               |

### 4.2 Confidence Calculation

```
depthConf = 1 - (depthDiff / depthThreshold)
normalConf = (normalDot - normalThreshold) / (1 - normalThreshold)
confidence = min(depthConf, normalConf)
```

Confidence scales how much history to trust—values near thresholds get reduced weight.

---

## 5. Neighborhood Clamping

History can drift even with validation. Clamping constrains history to the range of current frame's local neighborhood.

### 5.1 Min/Max Clamping

Sample 3×3 neighborhood of current frame, compute min/max per channel, clamp history to that range.

### 5.2 Variance Clamping (More Robust)

Compute mean and standard deviation of neighborhood:

```
clampedHistory = clamp(history, mean - stdDev * gamma, mean + stdDev * gamma)
```

Where `gamma` controls tightness (1.0 = tight, 2.0+ = looser).

---

## 6. Temporal Blend

### 6.1 Exponential Moving Average

```
result = mix(current, history, alpha)  // alpha=0.95 → 95% history, 5% current
```

### 6.2 Adaptive Blend

Adjust alpha based on validation:

- **Invalid history**: Use only current (full reset)
- **Low confidence**: Reduce alpha (more current)
- **Edge probes** (`valid < 0.9`): Reduce alpha by 50%

### 6.3 Disocclusion Handling Options

1. **Hard reset**: Use only current frame (noisy but correct)
2. **Soft reset**: Track `accumCount` per probe; ramp alpha up over ~10 frames
3. **Spatial fill**: Sample nearby valid probes (more complex)

---

## 7. Temporal Shader Structure

### 7.1 SH Mode (lumon_temporal.fsh)

**Key Uniforms:**

| Category | Uniforms                                                         |
| -------- | ---------------------------------------------------------------- |
| Current  | `currentRadiance0/1`, `probeAnchorPos`, `probeAnchorNormal`      |
| History  | `historyRadiance0/1`, `historyDepth`, `historyNormal`            |
| Matrices | `invViewMatrix`, `prevViewProjMatrix`                            |
| Config   | `temporalAlpha`, `depthRejectThreshold`, `normalRejectThreshold` |

**Outputs:**

- `outRadiance0/1`: Blended SH radiance
- `outMeta`: (linearDepth, encodedNormal.xy, accumCount)

**Main Loop (Pseudo Code):**

```
for each probe:
    if invalid: pass through current

    historyUV = reproject(posVS)
    validation = validate(historyUV, depth, normal)

    if validation.valid:
        history = sampleHistory(historyUV)
        history = clampToNeighborhood(history, 3x3 current)
        alpha = temporalAlpha * validation.confidence
        if edgeProbe: alpha *= 0.5
        output = mix(current, history, alpha)
        accumCount = prevAccum + 1
    else:
        output = current
        accumCount = 1

    storeMeta(depth, normal, accumCount)
```

---

## 7.2 Octahedral Temporal Accumulation

When using octahedral radiance storage, temporal accumulation works **per-texel** rather than per-probe:

### 7.2.1 Key Differences from SH Temporal

| Aspect               | SH Mode (per-probe)          | Octahedral Mode (per-texel)        |
| -------------------- | ---------------------------- | ---------------------------------- |
| Unit of operation    | Entire probe (2 SH textures) | Individual texel (1 RGBA value)    |
| Blend trigger        | Every frame for every probe  | Only traced texels this frame      |
| Disocclusion signal  | Probe depth/normal change    | Texel hit-distance delta           |
| History preservation | Blend all history            | Keep non-traced texels unchanged   |
| Neighborhood clamp   | 3×3 probe neighborhood       | 3×3 texel neighborhood within tile |

### 7.2.2 Per-Texel Algorithm

```
for each atlas texel:
    if !wasTracedThisFrame(octTexel, probeIndex):
        output = history  // Preserve unchanged
        return

    // Validate using hit distance (not probe depth)
    historyValid = |currentDist - historyDist| / max(both) < threshold

    if historyValid:
        history = clampToNeighborhood(history, 3x3 within tile)
        output = mix(current, history, alpha)
    else:
        output = current  // Disoccluded
```

### 7.2.3 Hit-Distance Validation

Unlike SH mode which uses probe depth/normal, octahedral uses **per-texel hit distance**:

- Each direction can see different geometry
- A direction that previously hit a nearby wall but now sees distant sky is disoccluded
- More granular than probe-level depth

The trace shader already handles the "preserve history for non-traced texels" case by copying from history when a texel isn't traced. The temporal pass then blends only the fresh texels with their history.

### 7.2.4 Buffer Flow

```
Trace Pass → writes traced texels, copies history for non-traced
    ↓
Temporal Pass → blends traced texels, passes through copied
    ↓
Swap Buffers → current becomes next frame's history
```

---

## 8. C# Integration

The temporal pass requires:

1. **Meta buffers**: Store (linearDepth, encodedNormal, accumCount) per probe
2. **SwapAllBuffers()**: Swap both radiance and meta buffer pairs

**Render pass steps:**

1. Bind current radiance + anchor textures
2. Bind history radiance + meta textures
3. Set matrices (`invViewMatrix`, `prevViewProjMatrix`)
4. Set config uniforms (`temporalAlpha`, `depthRejectThreshold`, `normalRejectThreshold`)
5. Render fullscreen quad at probe resolution

---

## 9. Teleport/Scene Change Detection

Detect large camera jumps (distance > threshold, e.g., 50m) and clear history buffers.
Also register for world unload/load events to reset `isFirstFrame`.

---

## 10. Debug Visualization

Debug visualizations implemented in `lumon_debug.fsh`:

| Debug Mode | Shows           | Visual                                                                    |
| ---------- | --------------- | ------------------------------------------------------------------------- |
| 6          | Temporal Weight | Grayscale (brighter = more history)                                       |
| 7          | Rejection Mask  | Green=valid, Red=out-of-bounds, Yellow=depth-reject, Orange=normal-reject |

---

## 11. Performance Notes

### 11.1 Cost Analysis

| Operation                   | Cost                 |
| --------------------------- | -------------------- |
| Matrix multiply (reproject) | ~64 MADs per probe   |
| History texture samples     | 4 samples per probe  |
| Neighborhood samples (3×3)  | 18 samples per probe |
| Validation logic            | ~20 ALU per probe    |
| Blend                       | ~8 ALU per probe     |

**Total**: ~110 ALU + 22 texture samples per probe
At 32K probes: ~3.5M ALU, 700K texture samples

### 11.2 Optimizations

1. **Skip stable probes**: If accumCount > 50 and no motion, skip temporal
2. **Reduce neighborhood to 2×2**: Faster clamping with slight quality loss
3. **Pack depth/normal into one texture**: Reduce samples

---

## 12. Next Steps

| Document                                                   | Dependency    | Topic                                   |
| ---------------------------------------------------------- | ------------- | --------------------------------------- |
| [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md) | This document | Probe-to-pixel gathering and upsampling |
