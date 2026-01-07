# LumOn Radiance Cache & SH Encoding

> **Document**: LumOn.03-Radiance-Cache.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md)  
> **Implements**: SPG-003

---

## 1. Overview

The radiance cache stores incoming light (irradiance) at each probe using **Spherical Harmonics (SH)**. SH provides:

1. **Compact storage**: 4 coefficients capture low-frequency lighting
2. **Efficient evaluation**: Single dot product for any direction
3. **Natural accumulation**: SH coefficients add linearly
4. **Smooth interpolation**: Bilinear blend in coefficient space

### 1.1 Why SH L1?

| SH Order     | Coefficients | Quality              | Cost    |
| ------------ | ------------ | -------------------- | ------- |
| L0 (DC only) | 1            | Flat ambient         | Minimal |
| **L1**       | 4            | Directional gradient | Low     |
| L2           | 9            | Soft shadows         | Medium  |
| L3           | 16           | Sharp features       | High    |

**L1 chosen** because:

- 4 coefficients fit in one RGBA16F texture (per channel)
- Captures primary light direction + ambient
- Sufficient for diffuse indirect lighting
- Matches computational budget of screen-space probes

### 1.2 SH L1 Basis Functions

The 4 L1 basis functions (real, normalized):

| Index | Basis Y_l^m | Formula        | Interpretation |
| ----- | ----------- | -------------- | -------------- |
| 0     | Y_0^0       | `0.282095`     | DC (ambient)   |
| 1     | Y_1^{-1}    | `0.488603 * y` | Y gradient     |
| 2     | Y_1^0       | `0.488603 * z` | Z gradient     |
| 3     | Y_1^1       | `0.488603 * x` | X gradient     |

Where `(x, y, z)` is a unit direction vector.

---

## 2. Texture Layout

### 2.1 Storage Strategy

Each probe stores SH L1 coefficients for RGB independently:

```
Per probe: 4 coefficients × 3 channels = 12 floats
```

**Option A**: 3 textures (R, G, B), each RGBA16F storing 4 coefficients  
**Option B**: 2 textures with packed layout (chosen)

### 2.2 Chosen Layout: 2× RGBA16F

| Texture              | R     | G     | B     | A     |
| -------------------- | ----- | ----- | ----- | ----- |
| **ProbeRadiance[0]** | SH0_R | SH0_G | SH0_B | SH1_R |
| **ProbeRadiance[1]** | SH1_G | SH1_B | SH2_R | SH2_G |
| **ProbeRadiance[2]** | SH2_B | SH3_R | SH3_G | SH3_B |

**Note**: This requires 3 textures for complete storage. Alternative:

### 2.3 Alternative: Packed into 2 Textures

Use interleaved packing with reduced precision for higher indices:

| Texture              | R     | G     | B                | A                |
| -------------------- | ----- | ----- | ---------------- | ---------------- |
| **ProbeRadiance[0]** | SH0_R | SH0_G | SH0_B            | SH1_R            |
| **ProbeRadiance[1]** | SH1_G | SH1_B | SH2_RGB (packed) | SH3_RGB (packed) |

**Final Decision**: Use 2 RGBA16F textures with this layout:

| Texture              | Content                                                              |
| -------------------- | -------------------------------------------------------------------- |
| **ProbeRadiance[0]** | RGB: SH coefficient 0 (DC), A: luminance of SH1                      |
| **ProbeRadiance[1]** | RGB: Dominant direction (normalized SH1-3), A: directional intensity |

This simplified encoding trades full SH fidelity for fewer textures.

### 2.4 Simplified Dominant-Direction Encoding

For M1/M2, use a simpler encoding that captures:

- **Ambient color** (SH0): Average incoming radiance
- **Dominant direction**: Primary light direction
- **Directional intensity**: How directional vs ambient

```glsl
struct ProbeRadiance {
    vec3 ambient;           // SH coefficient 0 (scaled)
    vec3 dominantDir;       // Normalized direction of maximum radiance
    float directionalRatio; // 0 = pure ambient, 1 = fully directional
};
```

**Stored as**:

- Texture 0: RGB = ambient color, A = directional ratio
- Texture 1: RGB = dominant direction (encoded), A = reserved

This is **not true SH** but achieves similar results with simpler math.

---

## 3. True SH L1 Implementation

For completeness, here's the full SH L1 implementation:

### 3.1 GLSL Include File: lumon_sh.ash

```glsl
// lumon_sh.ash
// Spherical Harmonics L1 helper functions for LumOn radiance cache

#ifndef LUMON_SH_ASH
#define LUMON_SH_ASH

// ═══════════════════════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════════════════════

// SH basis constants (real, orthonormalized)
const float SH_C0 = 0.282094792;  // 1 / (2 * sqrt(pi))
const float SH_C1 = 0.488602512;  // sqrt(3) / (2 * sqrt(pi))

// Cosine lobe coefficients for diffuse BRDF convolution
// These are the zonal harmonics coefficients for max(dot(n, l), 0)
const float SH_COSINE_A0 = 3.141592654;        // pi
const float SH_COSINE_A1 = 2.094395102;        // 2*pi/3

// ═══════════════════════════════════════════════════════════════════════════
// Data Structures
// ═══════════════════════════════════════════════════════════════════════════

// SH L1 coefficients for a single color channel
struct SH1 {
    float c[4];  // c0 (DC), c1 (Y), c2 (Z), c3 (X)
};

// SH L1 coefficients for RGB
struct SH1_RGB {
    vec4 coeffs0;  // R: c0_r, G: c0_g, B: c0_b, A: c1_r
    vec4 coeffs1;  // R: c1_g, G: c1_b, B: c2_r, A: c2_g
    vec4 coeffs2;  // R: c2_b, G: c3_r, B: c3_g, A: c3_b
};

// Simplified: Store per-channel in vec4 (4 coefficients per channel)
// Access as shR.x = c0, shR.y = c1, shR.z = c2, shR.w = c3

// ═══════════════════════════════════════════════════════════════════════════
// Basis Evaluation
// ═══════════════════════════════════════════════════════════════════════════

/// Evaluate SH L1 basis functions for a direction
/// @param dir Normalized direction vector
/// @return vec4 containing [Y_0^0, Y_1^{-1}, Y_1^0, Y_1^1]
vec4 SHBasis(vec3 dir) {
    return vec4(
        SH_C0,           // Y_0^0:   constant
        SH_C1 * dir.y,   // Y_1^-1:  y
        SH_C1 * dir.z,   // Y_1^0:   z
        SH_C1 * dir.x    // Y_1^1:   x
    );
}

// ═══════════════════════════════════════════════════════════════════════════
// Projection (Direction + Radiance → SH)
// ═══════════════════════════════════════════════════════════════════════════

/// Project a radiance sample onto SH L1 basis
/// @param dir      Normalized direction the radiance came from
/// @param radiance RGB radiance value
/// @param shR      Output: SH coefficients for red channel
/// @param shG      Output: SH coefficients for green channel
/// @param shB      Output: SH coefficients for blue channel
void SHProject(vec3 dir, vec3 radiance,
               out vec4 shR, out vec4 shG, out vec4 shB) {
    vec4 basis = SHBasis(dir);
    shR = basis * radiance.r;
    shG = basis * radiance.g;
    shB = basis * radiance.b;
}

/// Project with cosine weighting (for hemisphere integration)
/// Multiplies by cos(theta) = dot(normal, dir)
void SHProjectCosine(vec3 dir, vec3 radiance, vec3 normal,
                     out vec4 shR, out vec4 shG, out vec4 shB) {
    float cosTheta = max(dot(normal, dir), 0.0);
    vec3 weighted = radiance * cosTheta;
    SHProject(dir, weighted, shR, shG, shB);
}

// ═══════════════════════════════════════════════════════════════════════════
// Evaluation (SH + Direction → Radiance)
// ═══════════════════════════════════════════════════════════════════════════

/// Evaluate SH at a direction (reconstruct radiance)
/// @param shR Red channel SH coefficients
/// @param shG Green channel SH coefficients
/// @param shB Blue channel SH coefficients
/// @param dir Normalized direction to evaluate
/// @return Reconstructed RGB radiance
vec3 SHEvaluate(vec4 shR, vec4 shG, vec4 shB, vec3 dir) {
    vec4 basis = SHBasis(dir);
    return vec3(
        dot(shR, basis),
        dot(shG, basis),
        dot(shB, basis)
    );
}

/// Evaluate diffuse irradiance (convolved with cosine lobe)
/// This is what you want for Lambertian surfaces
/// @param shR, shG, shB SH coefficients
/// @param normal        Surface normal
/// @return Irradiance (integrate incoming radiance * cos(theta))
vec3 SHEvaluateDiffuse(vec4 shR, vec4 shG, vec4 shB, vec3 normal) {
    // Convolve SH with cosine lobe (zonal harmonic)
    // Result is: A0*c0 + A1*(c1*ny + c2*nz + c3*nx)
    vec4 basis = SHBasis(normal);

    // Apply cosine lobe convolution weights
    vec4 cosineWeights = vec4(
        SH_COSINE_A0,  // DC term
        SH_COSINE_A1,  // Directional terms
        SH_COSINE_A1,
        SH_COSINE_A1
    );

    vec4 convolved = basis * cosineWeights;

    return vec3(
        dot(shR, convolved),
        dot(shG, convolved),
        dot(shB, convolved)
    ) / 3.141592654;  // Divide by pi for diffuse BRDF
}

// ═══════════════════════════════════════════════════════════════════════════
// Accumulation & Blending
// ═══════════════════════════════════════════════════════════════════════════

/// Accumulate SH coefficients (add sample to running sum)
void SHAccumulate(inout vec4 shR, inout vec4 shG, inout vec4 shB,
                  vec4 sampleR, vec4 sampleG, vec4 sampleB) {
    shR += sampleR;
    shG += sampleG;
    shB += sampleB;
}

/// Scale SH coefficients
void SHScale(inout vec4 shR, inout vec4 shG, inout vec4 shB, float scale) {
    shR *= scale;
    shG *= scale;
    shB *= scale;
}

/// Linear interpolation of SH coefficients
void SHLerp(vec4 shR_a, vec4 shG_a, vec4 shB_a,
            vec4 shR_b, vec4 shG_b, vec4 shB_b,
            float t,
            out vec4 shR, out vec4 shG, out vec4 shB) {
    shR = mix(shR_a, shR_b, t);
    shG = mix(shG_a, shG_b, t);
    shB = mix(shB_a, shB_b, t);
}

/// Bilinear interpolation of 4 SH probes
vec3 SHBilinearEvaluate(
    vec4 shR_00, vec4 shG_00, vec4 shB_00,  // Bottom-left
    vec4 shR_10, vec4 shG_10, vec4 shB_10,  // Bottom-right
    vec4 shR_01, vec4 shG_01, vec4 shB_01,  // Top-left
    vec4 shR_11, vec4 shG_11, vec4 shB_11,  // Top-right
    vec2 weights,                            // Bilinear weights
    vec3 evalDir                             // Direction to evaluate
) {
    // Interpolate coefficients
    vec4 shR_0 = mix(shR_00, shR_10, weights.x);
    vec4 shG_0 = mix(shG_00, shG_10, weights.x);
    vec4 shB_0 = mix(shB_00, shB_10, weights.x);

    vec4 shR_1 = mix(shR_01, shR_11, weights.x);
    vec4 shG_1 = mix(shG_01, shG_11, weights.x);
    vec4 shB_1 = mix(shB_01, shB_11, weights.x);

    vec4 shR = mix(shR_0, shR_1, weights.y);
    vec4 shG = mix(shG_0, shG_1, weights.y);
    vec4 shB = mix(shB_0, shB_1, weights.y);

    // Evaluate at direction
    return SHEvaluate(shR, shG, shB, evalDir);
}

// ═══════════════════════════════════════════════════════════════════════════
// Normalization & Clamping
// ═══════════════════════════════════════════════════════════════════════════

/// Normalize accumulated SH by sample count
void SHNormalize(inout vec4 shR, inout vec4 shG, inout vec4 shB,
                 float sampleCount) {
    if (sampleCount > 0.0) {
        float invCount = 1.0 / sampleCount;
        shR *= invCount;
        shG *= invCount;
        shB *= invCount;
    }
}

/// Clamp SH to prevent negative radiance reconstruction
/// Note: L1 SH can produce negative values; this is a soft clamp
void SHClampNegative(inout vec4 shR, inout vec4 shG, inout vec4 shB) {
    // Ensure DC term is positive
    shR.x = max(shR.x, 0.0);
    shG.x = max(shG.x, 0.0);
    shB.x = max(shB.x, 0.0);

    // Limit directional terms to not exceed DC
    // This prevents negative values in reconstruction
    float maxDirR = shR.x * 0.5;  // Heuristic limit
    float maxDirG = shG.x * 0.5;
    float maxDirB = shB.x * 0.5;

    shR.yzw = clamp(shR.yzw, -maxDirR, maxDirR);
    shG.yzw = clamp(shG.yzw, -maxDirG, maxDirG);
    shB.yzw = clamp(shB.yzw, -maxDirB, maxDirB);
}

// ═══════════════════════════════════════════════════════════════════════════
// Utility
// ═══════════════════════════════════════════════════════════════════════════

/// Extract dominant light direction from SH L1
vec3 SHDominantDirection(vec4 shR, vec4 shG, vec4 shB) {
    // Directional components are proportional to direction
    vec3 dirR = shR.wzy;  // x, y, z from coefficients 3, 1, 2
    vec3 dirG = shG.wzy;
    vec3 dirB = shB.wzy;

    // Weight by luminance
    vec3 dir = dirR * 0.2126 + dirG * 0.7152 + dirB * 0.0722;

    float len = length(dir);
    return len > 0.001 ? dir / len : vec3(0.0, 1.0, 0.0);
}

/// Get ambient (DC) term as RGB color
vec3 SHAmbient(vec4 shR, vec4 shG, vec4 shB) {
    return vec3(shR.x, shG.x, shB.x);
}

#endif // LUMON_SH_ASH
```

---

## 4. Texture Read/Write Helpers

### 4.1 Packing SH into 2 Textures

For the 2-texture layout:

```glsl
// lumon_sh_pack.ash
// Packing helpers for 2-texture SH storage

// Pack SH RGB into 2 RGBA16F textures
void SHPack(vec4 shR, vec4 shG, vec4 shB,
            out vec4 tex0, out vec4 tex1) {
    // Texture 0: DC terms + first directional R
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);

    // Texture 1: Remaining directional terms (compressed)
    // Pack Y directions
    tex1.r = shG.y;
    tex1.g = shB.y;

    // Pack Z and X directions (averaged for compression)
    // This loses some precision but fits in 2 textures
    vec3 avgZX = (vec3(shR.z, shG.z, shB.z) + vec3(shR.w, shG.w, shB.w)) * 0.5;
    tex1.b = dot(avgZX, vec3(0.2126, 0.7152, 0.0722));  // Luminance
    tex1.a = 0.0;  // Reserved
}

// Unpack SH RGB from 2 RGBA16F textures
void SHUnpack(vec4 tex0, vec4 tex1,
              out vec4 shR, out vec4 shG, out vec4 shB) {
    // DC terms from texture 0
    shR.x = tex0.r;
    shG.x = tex0.g;
    shB.x = tex0.b;

    // Y directional
    shR.y = tex0.a;
    shG.y = tex1.r;
    shB.y = tex1.g;

    // Z and X (reconstruct from compressed luminance)
    // This is approximate reconstruction
    float lumZX = tex1.b;
    shR.z = shR.w = lumZX * 0.5;
    shG.z = shG.w = lumZX * 0.5;
    shB.z = shB.w = lumZX * 0.5;
}
```

### 4.2 Simplified: Ambient + Direction Encoding

For M1, use the simpler encoding:

```glsl
// lumon_radiance_simple.ash
// Simplified radiance encoding: ambient + dominant direction

struct SimpleRadiance {
    vec3 ambient;           // Average incoming radiance
    vec3 dominantDir;       // Primary light direction (view-space)
    float directionalRatio; // 0 = ambient only, 1 = fully directional
    float intensity;        // Overall brightness multiplier
};

// Encode to 2 textures
void EncodeRadiance(SimpleRadiance rad, out vec4 tex0, out vec4 tex1) {
    tex0.rgb = rad.ambient;
    tex0.a = rad.directionalRatio;

    tex1.rgb = rad.dominantDir * 0.5 + 0.5;  // Encode [-1,1] to [0,1]
    tex1.a = rad.intensity;
}

// Decode from 2 textures
SimpleRadiance DecodeRadiance(vec4 tex0, vec4 tex1) {
    SimpleRadiance rad;
    rad.ambient = tex0.rgb;
    rad.directionalRatio = tex0.a;
    rad.dominantDir = tex1.rgb * 2.0 - 1.0;
    rad.intensity = tex1.a;
    return rad;
}

// Evaluate radiance for a surface normal
vec3 EvaluateRadiance(SimpleRadiance rad, vec3 normal) {
    float dirContrib = max(dot(normal, rad.dominantDir), 0.0);
    vec3 directional = rad.ambient * dirContrib * rad.directionalRatio;
    vec3 ambient = rad.ambient * (1.0 - rad.directionalRatio * 0.5);
    return (ambient + directional) * rad.intensity;
}

// Accumulate radiance sample
void AccumulateRadiance(inout SimpleRadiance accum,
                        vec3 sampleDir, vec3 sampleRadiance,
                        float weight) {
    accum.ambient += sampleRadiance * weight;
    // Use perceptual luminance weights for direction accumulation
    // This gives better results for natural lighting scenarios
    float lum = dot(sampleRadiance, vec3(0.2126, 0.7152, 0.0722));
    accum.dominantDir += sampleDir * lum * weight;
    accum.intensity += weight;
}

// Normalize after accumulation
void NormalizeRadiance(inout SimpleRadiance rad) {
    if (rad.intensity > 0.0) {
        rad.ambient /= rad.intensity;
        float dirLen = length(rad.dominantDir);
        if (dirLen > 0.001) {
            rad.dominantDir /= dirLen;
            rad.directionalRatio = min(dirLen / rad.intensity, 1.0);
        } else {
            rad.dominantDir = vec3(0.0, 1.0, 0.0);
            rad.directionalRatio = 0.0;
        }
        rad.intensity = 1.0;
    }
}

// Lerp between two radiance values
SimpleRadiance LerpRadiance(SimpleRadiance a, SimpleRadiance b, float t) {
    SimpleRadiance result;
    result.ambient = mix(a.ambient, b.ambient, t);
    // Handle zero-length direction to avoid NaN from normalize
    vec3 mixedDir = mix(a.dominantDir, b.dominantDir, t);
    float mixedLen = length(mixedDir);
    result.dominantDir = mixedLen > 0.001 ? mixedDir / mixedLen : vec3(0.0, 1.0, 0.0);
    result.directionalRatio = mix(a.directionalRatio, b.directionalRatio, t);
    result.intensity = mix(a.intensity, b.intensity, t);
    return result;
}
```

---

## 5. Double-Buffer Swap Logic

### 5.1 Buffer Manager Implementation

```csharp
// In LumOnBufferManager.cs

public class LumOnBufferManager
{
    // Current frame writes to "Current", reads history from "History"
    // After temporal pass, they swap roles

    private FrameBufferRef[] radianceBuffers = new FrameBufferRef[2];
    private int writeIndex = 0;

    public FrameBufferRef ProbeRadianceWrite => radianceBuffers[writeIndex];
    public FrameBufferRef ProbeRadianceRead => radianceBuffers[1 - writeIndex];

    private void CreateRadianceBuffers()
    {
        for (int i = 0; i < 2; i++)
        {
            radianceBuffers[i] = CreateFramebuffer(
                $"LumOn_Radiance_{i}",
                ProbeCountX, ProbeCountY,
                new[] { EnumTextureFormat.Rgba16f, EnumTextureFormat.Rgba16f }
            );
        }
    }

    /// <summary>
    /// Swap read/write radiance buffers. Call after temporal pass.
    /// </summary>
    public void SwapRadianceBuffers()
    {
        writeIndex = 1 - writeIndex;
    }

    /// <summary>
    /// Clear history buffer (call on first frame or after teleport).
    /// </summary>
    public void ClearHistory()
    {
        // Bind history buffer and clear to black
        // This forces full recomputation
    }
}
```

### 5.2 Frame Sequence

```
Frame N:
  1. ProbeTrace writes to Radiance[0] (current)
  2. Temporal reads Radiance[1] (history) + Radiance[0] (current)
  3. Temporal writes blended result to Radiance[0]
  4. SwapRadianceBuffers() → writeIndex becomes 1

Frame N+1:
  1. ProbeTrace writes to Radiance[1] (current)
  2. Temporal reads Radiance[0] (history) + Radiance[1] (current)
  3. Temporal writes blended result to Radiance[1]
  4. SwapRadianceBuffers() → writeIndex becomes 0
```

---

## 6. Performance Considerations

### 6.1 Memory Usage

| Buffer           | Size (8px spacing, 1080p) | Format     | Total       |
| ---------------- | ------------------------- | ---------- | ----------- |
| ProbeRadiance[0] | 240 × 135                 | 2× RGBA16F | 0.52 MB     |
| ProbeRadiance[1] | 240 × 135                 | 2× RGBA16F | 0.52 MB     |
| **Total**        |                           |            | **1.04 MB** |

### 6.2 Bandwidth per Frame

| Operation                 | Data Size |
| ------------------------- | --------- |
| Write current radiance    | 0.52 MB   |
| Read history radiance     | 0.52 MB   |
| Read current for temporal | 0.52 MB   |
| Write temporal output     | 0.52 MB   |
| **Total**                 | **~2 MB** |

### 6.3 ALU Considerations

SH operations are lightweight:

- `SHBasis()`: 4 multiplies, 1 normalize
- `SHEvaluate()`: 3 dot products (12 MADs)
- `SHAccumulate()`: 3 vector adds

---

## 7. Upgrade Path to Full SH L1

For M3+, upgrade to full SH L1:

### 7.1 Expand to 3 Textures

```glsl
// Full SH L1 packing (3 RGBA16F textures)
void SHPackFull(vec4 shR, vec4 shG, vec4 shB,
                out vec4 tex0, out vec4 tex1, out vec4 tex2) {
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);  // DC + R.y
    tex1 = vec4(shG.y, shB.y, shR.z, shG.z);  // G.y, B.y, R.z, G.z
    tex2 = vec4(shB.z, shR.w, shG.w, shB.w);  // B.z, R.w, G.w, B.w
}

void SHUnpackFull(vec4 tex0, vec4 tex1, vec4 tex2,
                  out vec4 shR, out vec4 shG, out vec4 shB) {
    shR = vec4(tex0.r, tex0.a, tex1.b, tex2.g);
    shG = vec4(tex0.g, tex1.r, tex1.a, tex2.b);
    shB = vec4(tex0.b, tex1.g, tex2.r, tex2.a);
}
```

### 7.2 Consider SH L2 (Future)

L2 adds 5 more coefficients (9 total) for sharper features:

- Requires 3 RGBA16F textures (12 floats, 3 unused)
- ~3× memory and bandwidth
- Better shadow definition
- Consider for M5+ if quality insufficient

---

## 8. Implementation Checklist

### 8.1 Shader Includes

- [x] Create `lumon_sh.ash` in `shaderincludes/` *(implemented as `lumon_sh.fsh`)*
- [x] Implement `SHBasis()` function *(implemented as `shEncode()`)*
- [x] Implement `SHProject()` function *(implemented as `shProjectOut()` + `shProjectRGB()`)*
- [x] Implement `SHProjectCosine()` function *(implemented as `shProjectCosineRGB()`)*
- [x] Implement `SHEvaluate()` function *(implemented as `shEvaluate()`, `shEvaluateRGB()`)*
- [x] Implement `SHEvaluateDiffuse()` function *(implemented as `shEvaluateDiffuseRGB()`)*
- [x] Implement `SHAccumulate()` function *(implemented as `shAccumulate()`)*
- [x] Implement `SHScale()` function *(implemented as `shScale()`)*
- [x] Implement `SHLerp()` function *(implemented as `shLerp()`)*
- [x] Implement `SHBilinearEvaluate()` function *(implemented as `shBilinearEvaluate()`)*
- [x] Implement `SHNormalize()` function *(implemented as `shNormalize()`)*
- [x] Implement `SHClampNegative()` function *(implemented as `shClampNegative()`)*
- [x] Implement `SHDominantDirection()` function *(implemented as `shDominantDirection()`)*
- [x] Implement `SHAmbient()` function *(implemented as `shAmbient()`)*
- [x] Add pack/unpack helpers for 2-texture layout *(implemented in `lumon_sh.fsh` and `lumon_sh_pack.fsh`)*

### 8.2 Buffer Management

- [x] Create ProbeRadiance framebuffers (read/write pair)
- [x] Each FB has 2 RGBA16F attachments
- [x] Implement `SwapRadianceBuffers()` method
- [x] Add `ClearHistory()` for first frame/teleport *(also added `InvalidateCache()`)*

### 8.3 Simplified Encoding (M1)

- [x] Create `lumon_radiance_simple.ash` alternative *(implemented as `lumon_radiance_simple.fsh`)*
- [x] Implement `EncodeRadiance()` / `DecodeRadiance()` *(implemented as `encodeRadiance()` / `decodeRadiance()`)*
- [ ] Test with simple directional lighting

### 8.4 Testing

- [ ] Unit test: SH project + evaluate roundtrip
- [ ] Verify DC term matches average radiance
- [ ] Check no negative values in reconstruction
- [ ] Compare SH L1 vs simplified encoding quality
- [ ] Verify bilinear SH interpolation works correctly
- [ ] Test SH dominant direction extraction

---

## 9. Next Steps

| Document                                           | Dependency    | Topic                               |
| -------------------------------------------------- | ------------- | ----------------------------------- |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md) | This document | Per-probe screen-space ray marching |
