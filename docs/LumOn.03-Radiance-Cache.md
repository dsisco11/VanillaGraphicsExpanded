# LumOn Radiance Cache

> **Document**: LumOn.03-Radiance-Cache.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md)  
> **Implements**: SPG-003

---

## 1. Overview

The radiance cache stores incoming light at each probe. LumOn uses an **Octahedral Radiance Map**
approach matching UE5 Lumen's Screen Space Radiance Cache design. Each probe stores radiance in
64 world-space directions (8×8 octahedral map) with per-texel hit distances for temporal stability.

### 1.1 Why Octahedral Maps?

| Approach       | Storage per Probe  | Angular Res | Temporal Stability | Complexity |
| -------------- | ------------------ | ----------- | ------------------ | ---------- |
| SH L1          | 8 floats (2 tex)   | ~4 dirs     | Poor               | Low        |
| SH L2          | 18 floats          | ~9 dirs     | Poor               | Medium     |
| **Octahedral** | 256 floats (8×8×4) | 64 dirs     | **Excellent**      | Medium     |

**Octahedral chosen** because:

- **Temporal stability**: World-space directions remain valid across camera rotations
- **Hit distance**: Per-texel depth enables leak prevention and disocclusion detection
- **Matches Lumen**: Proven approach from UE5's SIGGRAPH 2022 presentation
- **Higher angular resolution**: 64 directions vs 4-9 for SH L1/L2
- **Simpler accumulation**: Direct radiance storage, no SH rotation needed

### 1.2 Octahedral Mapping

Octahedral mapping projects a unit sphere onto a square via octahedron projection:

```
Unit Sphere → Octahedron (L1 norm) → Square [0,1]²
```

Properties:

- **Uniform distribution**: More uniform than lat-long or cubemaps
- **Bilinear filtering**: Works correctly with hardware filtering (within a probe)
- **Efficient encoding**: Simple math for direction ↔ UV conversion

---

## 2. Texture Layout

### 2.1 2D Atlas Storage

Octahedral radiance is stored in a **2D atlas texture** with dimensions `(probeCountX × 8, probeCountY × 8)`:

| Dimension | Size            | Meaning                |
| --------- | --------------- | ---------------------- |
| Width     | probeCountX × 8 | Probe X × octahedral U |
| Height    | probeCountY × 8 | Probe Y × octahedral V |

Each probe's 8×8 octahedral tile is arranged in a grid matching the screen-space probe layout.
This approach is compatible with GL 3.3 fragment shader output without requiring geometry shaders.

**Addressing**: To access texel (octU, octV) of probe (probeX, probeY):

```glsl
ivec2 atlasCoord = ivec2(probeX * 8 + octU, probeY * 8 + octV);
vec2 atlasUV = (vec2(atlasCoord) + 0.5) / vec2(atlasWidth, atlasHeight);
```

Each texel is RGBA16F:

| Channel | Content                  | Range / Encoding    |
| ------- | ------------------------ | ------------------- |
| R       | Radiance red             | HDR (linear)        |
| G       | Radiance green           | HDR (linear)        |
| B       | Radiance blue            | HDR (linear)        |
| A       | Log-encoded hit distance | `log(distance + 1)` |

### 2.2 Triple Buffering

Three copies for temporal accumulation:

| Buffer              | Written By     | Read By       |
| ------------------- | -------------- | ------------- |
| `OctahedralTrace`   | Trace Pass     | Temporal Pass |
| `OctahedralCurrent` | Temporal Pass  | Gather Pass   |
| `OctahedralHistory` | (Prev Current) | Temporal Pass |

After each frame, Current and History are swapped.

### 2.3 Memory Budget

For a 1920×1080 screen with 8px probe spacing:

```
Probes: 240 × 135 = 32,400
Atlas size: (240 × 8) × (135 × 8) = 1920 × 1080
Bytes per texel: 8 (RGBA16F)
Per buffer: 1920 × 1080 × 8 = 16.6 MB
Triple buffered: ~50 MB total
```

Note: The atlas dimensions conveniently match the screen resolution when using 8px probe spacing.

---

## 3. Octahedral Math Reference

### 3.1 Direction ↔ UV Conversion

**Direction to UV:**

```
1. Project direction onto octahedron: octant = dir / (|x| + |y| + |z|)
2. If lower hemisphere (z < 0): fold via diagonal reflection
3. Map [-1,1] → [0,1]
```

**UV to Direction:**

```
1. Map [0,1] → [-1,1]
2. Reconstruct Z from constraint |x| + |y| + |z| = 1
3. If lower hemisphere: unfold via diagonal reflection
4. Normalize result
```

### 3.2 Hit Distance Encoding

Log encoding provides better precision for near distances:

- **Encode**: `log(distance + 1)`
- **Decode**: `exp(encoded) - 1`

### 3.3 2D Atlas Addressing

```
atlasCoord = probeCoord * 8 + octTexel
atlasUV = (atlasCoord + 0.5) / atlasSize
```

Use `texture()` for hardware-filtered sampling within a probe, `texelFetch()` for exact texel access.

---

## 4. Legacy: SH L1 Encoding (Deprecated)

> **Note**: The SH L1 approach has been replaced by octahedral maps.
> This section is retained as a brief reference only.

### 4.1 SH L1 Summary

SH L1 uses 4 coefficients per color channel (12 floats total for RGB):

- **L0 (DC)**: Ambient/average radiance
- **L1 (3 coefficients)**: Directional gradient (X, Y, Z)

**Limitations that led to deprecation:**

- View-dependent coefficients require rotation on camera movement
- No per-direction depth for leak prevention
- Low angular resolution (~4 effective directions)

### 4.2 SH L1 Basis Functions

| Index | Formula        | Interpretation |
| ----- | -------------- | -------------- |
| 0     | `0.282095`     | DC (ambient)   |
| 1     | `0.488603 * y` | Y gradient     |
| 2     | `0.488603 * z` | Z gradient     |
| 3     | `0.488603 * x` | X gradient     |

### 4.3 Core Operations (Pseudo Code)

```
SHBasis(dir) → [C0, C1*y, C1*z, C1*x]

SHProject(dir, radiance):
    basis = SHBasis(dir)
    return basis * radiance  // per channel

SHEvaluate(shCoeffs, dir):
    return dot(shCoeffs, SHBasis(dir))

SHEvaluateDiffuse(shCoeffs, normal):
    // Convolve with cosine lobe using zonal harmonic weights
    // A0=π for DC, A1=2π/3 for directional terms
    basis = SHBasis(normal) * [A0, A1, A1, A1]
    return dot(shCoeffs, basis) / π
```

---

## 5. Texture Storage Layouts

### 5.1 Full SH L1: 3-Texture Layout

```
Texture 0: [SH0_R, SH0_G, SH0_B, SH1_R]
Texture 1: [SH1_G, SH1_B, SH2_R, SH2_G]
Texture 2: [SH2_B, SH3_R, SH3_G, SH3_B]
```

### 5.2 Simplified: Ambient + Dominant Direction (2 Textures)

For M1, a simpler encoding captures:

- **Ambient color**: Average incoming radiance
- **Dominant direction**: Primary light direction
- **Directional ratio**: How directional vs ambient (0-1)

```
Texture 0: RGB = ambient, A = directionalRatio
Texture 1: RGB = dominantDir (encoded), A = intensity
```

**Evaluation:**

```
dirContrib = max(dot(normal, dominantDir), 0)
result = ambient * (1 - ratio*0.5) + ambient * dirContrib * ratio
```

---

## 6. Double-Buffer Swap Logic

### 6.1 Buffer Management

The radiance cache uses double buffering for temporal stability:

```
Frame N:
  1. Trace → writes to Buffer[0] (current)
  2. Temporal → reads Buffer[1] (history) + Buffer[0], writes blended to Buffer[0]
  3. Swap → writeIndex flips

Frame N+1:
  1. Trace → writes to Buffer[1] (current)
  2. Temporal → reads Buffer[0] (history) + Buffer[1], writes blended to Buffer[1]
  3. Swap → writeIndex flips back
```

**Key operations:**

- `SwapRadianceBuffers()`: Toggle write index after temporal pass
- `ClearHistory()`: Force full recomputation (first frame, teleport, etc.)

---

## 7. Performance Considerations

### 7.1 Memory Usage

| Buffer           | Size (8px spacing, 1080p) | Format     | Total       |
| ---------------- | ------------------------- | ---------- | ----------- |
| ProbeRadiance[0] | 240 × 135                 | 2× RGBA16F | 0.52 MB     |
| ProbeRadiance[1] | 240 × 135                 | 2× RGBA16F | 0.52 MB     |
| **Total**        |                           |            | **1.04 MB** |

### 7.2 Bandwidth & ALU

- **Bandwidth**: ~2 MB/frame (write current, read history, read current for temporal, write output)
- **ALU**: SH operations are lightweight (~4 MADs for basis, ~12 MADs for evaluation)

---

## 8. Upgrade Path

### 8.1 Full SH L1 (3 Textures)

For M3+, expand to 3 RGBA16F textures to store all 12 coefficients without compression losses.

### 8.2 SH L2 (Future - M5+)

9 coefficients total for sharper shadow definition. ~3× memory and bandwidth cost.

---

## 9. Next Steps

| Document                                           | Dependency    | Topic                               |
| -------------------------------------------------- | ------------- | ----------------------------------- |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md) | This document | Per-probe screen-space ray marching |
