# LumOn Gather & Upsample Pipeline

> **Document**: LumOn.06-Gather-Upsample.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.05-Temporal.md](LumOn.05-Temporal.md)  
> **Implements**: SPG-007, SPG-008, SPG-009

---

## 1. Overview

The gather and upsample pipeline transforms probe radiance into per-pixel indirect diffuse lighting:

1. **Gather Pass (SPG-007)**: Interpolate nearby probes to compute irradiance per pixel
2. **Upsample Pass (SPG-008)**: Bilateral upsample from half-res to full resolution
3. **Integrate Pass (SPG-009)**: Combine with direct lighting in final composite

### 1.1 Resolution Strategy

| Resolution                 | Content                    | Purpose                 |
| -------------------------- | -------------------------- | ----------------------- |
| Probe grid (e.g., 240×135) | SH or Octahedral per probe | Ray-traced data         |
| Octahedral atlas           | 8×8 per probe (1920×1080)  | Radiance + hit distance |
| Half-res (e.g., 960×540)   | Indirect diffuse RGB       | Gathered irradiance     |
| Full-res (e.g., 1920×1080) | Final indirect term        | Upsampled output        |

### 1.2 Why Half-Resolution Gather?

- **Cost reduction**: 4× fewer gather operations than full-res
- **Smooth result**: Indirect lighting is inherently low-frequency
- **Bilateral upsample**: Preserves edges using depth/normal guides

### 1.3 Two Gather Modes

LumOn supports two gather modes controlled by `UseOctahedralCache`:

| Mode       | Storage    | Gather Method               | Advantages                               |
| ---------- | ---------- | --------------------------- | ---------------------------------------- |
| SH L1      | 2 textures | `SHEvaluateDiffuse(normal)` | Compact, fast single lookup              |
| Octahedral | 8×8 atlas  | Hemisphere integration      | Per-direction hit distance, less leaking |

**Octahedral mode is recommended** for higher quality with comparable performance.

---

## 2. Gather Pass (SPG-007)

### 2.1 Algorithm

For each half-res pixel:

1. Find the 4 enclosing probes (2×2 grid cell)
2. Compute bilinear interpolation weights
3. Adjust weights for edge-awareness (depth/normal discontinuities)
4. Sample and decode SH from each valid probe
5. Evaluate SH in surface normal direction
6. Blend weighted contributions

### 2.2 Probe Interpolation Geometry

```
Probe Grid:
    ┌───────┬───────┬───────┐
    │ P01   │       │ P11   │
    │   ●───┼───────┼───●   │
    │   │   │   ○   │   │   │  ○ = pixel position
    │   │   │  /│\  │   │   │  ● = probe positions
    │   │   │ / │ \ │   │   │
    │   ●───┼───────┼───●   │
    │ P00   │       │ P10   │
    └───────┴───────┴───────┘

Bilinear weights based on pixel position within cell:
  w00 = (1-fx) * (1-fy)
  w10 = fx * (1-fy)
  w01 = (1-fx) * fy
  w11 = fx * fy
```

### 2.3 Edge-Aware Weight Adjustment

```
EdgeAwareWeight(baseWeight, pixelDepth, probeDepth, pixelNormal, probeNormal):
    depthWeight  = gaussian(|pixelDepth - probeDepth|, depthSigma)
    normalWeight = pow(dot(pixelNormal, probeNormal), normalSigma)
    return baseWeight × depthWeight × normalWeight
```

### 2.4 Gather Shader (Pseudo Code)

```
GatherPass(halfResPixel):
    fullResUV = halfResPixel × 2 + 1  // Center of 2×2 block

    // Early-out for sky
    if depth(fullResUV) > 0.9999: return black

    pixelDepth, pixelNormal = sample G-Buffer at fullResUV

    // Find 4 enclosing probes
    probeCoord = fullResPixel / probeSpacing
    baseProbe  = floor(probeCoord)
    frac       = fract(probeCoord)

    // Load 2×2 probe neighborhood
    probes[4] = LoadProbes(baseProbe, offsets: [0,0], [1,0], [0,1], [1,1])

    // Compute edge-aware weights
    for each probe:
        bilinearWeight = standard bilinear from frac
        weight = EdgeAwareWeight(bilinearWeight, pixelDepth, probe.depth,
                                 pixelNormal, probe.normal)
        if probe.invalid: weight = 0

    weights = normalize(weights)

    // Blend SH and evaluate
    blendedSH = weighted sum of probe SH coefficients
    irradiance = SHEvaluateDiffuse(blendedSH, pixelNormal)

    return max(irradiance, 0) × intensity
```

**Key Uniforms**: `probeRadiance0/1`, `probeAnchorPos/Normal`, G-Buffer depth/normal, `probeGridSize`, `probeSpacing`, `depthSigma`, `normalSigma`, `intensity`

---

## 2.5 Octahedral Gather (UseOctahedralCache = true)

When using octahedral radiance storage, the gather pass replaces SH evaluation with hemisphere integration over the octahedral texture. This provides:

- **Per-direction hit distance**: Enables distance-aware probe weighting and leak prevention
- **Better directional fidelity**: 64 directions vs 4 SH coefficients
- **Temporal stability**: World-space storage eliminates view-dependent artifacts

### 2.5.1 Octahedral Gather Algorithm

For each half-res pixel:

1. Find the 4 enclosing probes (same as SH mode)
2. Compute bilinear weights with edge-awareness
3. **For each probe**, integrate radiance over the upper hemisphere:
   - Sample octahedral texels that lie in the hemisphere aligned to pixel normal
   - Weight each sample by `cos(θ)` (cosine weighting for Lambertian diffuse)
   - Accumulate weighted radiance
4. Apply distance-based probe weighting using average hit distance
5. Implement leak prevention by comparing probe hit distances to pixel depth
6. Blend weighted contributions from all 4 probes

### 2.5.2 Hemisphere Integration

The key difference from SH gather is that we must sample multiple directions from each probe's octahedral tile:

```
Octahedral Tile (8×8):           Hemisphere for Normal N:
┌──────────────────────┐         ┌──────────────────────┐
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ×  ×  ●  ●  ●  ×  ×  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ×  ●  ●  ●  ●  ●  ×  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │    →    │  ●  ●  ●  ●  ●  ●  ●  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ●  ●  ●  ●  ●  ●  ●  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ●  ●  ●  ●  ●  ●  ●  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ×  ●  ●  ●  ●  ●  ×  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ×  ×  ●  ●  ●  ×  ×  │
│  ●  ●  ●  ●  ●  ●  ●  ●  │         │  ×  ×  ×  ×  ×  ×  ×  │
└──────────────────────┘         └──────────────────────┘
  All 64 directions              Only ~32 in upper hemi (●)
                                 Skip lower hemisphere (×)
```

### 2.5.3 Cosine-Weighted Integration

```
IntegrateHemisphere(octAtlas, probeCoord, normalWS) -> (irradiance, avgHitDist):
    atlasOffset = probeCoord × 8  // 8×8 tile per probe

    for each texel (x, y) in 8×8 tile:
        dir = octahedralUVToDirection((x+0.5)/8, (y+0.5)/8)
        cosWeight = dot(dir, normalWS)
        if cosWeight <= 0: continue  // Skip backfacing

        sample = texelFetch(octAtlas, atlasOffset + (x, y))
        irradiance += sample.rgb × cosWeight
        avgHitDist += decodeHitDistance(sample.a)

    return normalize(irradiance), average(hitDistances)
```

### 2.5.4 Distance-Aware Probe Weighting

Hit distance enables smarter probe weighting to reduce light leaking:

```
ComputeProbeWeight(bilinearWeight, pixelDepth, probeDepth,
                   pixelNormal, probeNormal, avgHitDist):
    depthWeight  = gaussian(|pixelDepth - probeDepth| / pixelDepth)
    normalWeight = pow(dot(pixelNormal, probeNormal), 4)

    // Prefer probes with similar scene distance (leak prevention)
    distRatio   = avgHitDist / pixelDepth
    distWeight  = gaussian(|distRatio - 1|)

    return bilinearWeight × depthWeight × normalWeight × distWeight
```

### 2.5.5 Leak Prevention

Light leaking occurs when a probe sees past thin geometry that occludes the pixel. Per-direction hit distance allows detection:

```
// Leak check: probe hit is >50% farther than pixel depth
leakWeight = (probeHitDist > pixelDepth × 1.5) ? 0.1 : 1.0
irradiance += radiance × cosWeight × leakWeight
```

### 2.5.6 Optimized Hemisphere Integration

**Optimization**: Sample 16 directions (4×4 sub-grid) instead of 64 by using stride=2. Provides good quality at ~4× lower cost.

### 2.5.7 Octahedral Gather Shader

**Shader file**: `lumon_gather_octahedral.fsh`

**Key Uniforms**:

- `octahedralAtlas` — Radiance atlas (probeCountX×8, probeCountY×8)
- `probeAnchorPosition/Normal` — Probe placement
- `primaryDepth`, `gBufferNormal` — G-buffer guides
- `leakThreshold` — Depth tolerance for leak prevention (e.g., 0.5 = 50%)

### 2.5.8 Performance Considerations

| Approach                 | Samples/Pixel | Quality   | Cost   |
| ------------------------ | ------------- | --------- | ------ |
| SH Gather                | 4 probes      | Good      | ~0.3ms |
| Octahedral (64 samples)  | 4×64 = 256    | Excellent | ~1.2ms |
| Octahedral (16 samples)  | 4×16 = 64     | Very Good | ~0.5ms |
| Octahedral (prefiltered) | 4 probes      | Excellent | ~0.3ms |

**Recommendation**: Use 16-sample integration (4×4 subgrid) for best quality/performance balance.

---

## 3. Upsample Pass (SPG-008)

### 3.1 Bilateral Upsample

The half-res indirect buffer is upsampled to full resolution using edge-aware bilateral filtering:

```
BilateralUpsample(fullResUV, centerDepth, centerNormal):
    halfResCoord = fullResUV × halfResSize - 0.5
    baseCoord    = floor(halfResCoord)
    frac         = fract(halfResCoord)

    result = 0
    for each neighbor (dx, dy) in 2×2:
        sampleCoord   = clamp(baseCoord + (dx, dy))
        sampleFullUV  = (sampleCoord × 2 + 1) / fullResSize

        sampleDepth   = linearize(gDepth[sampleFullUV])
        sampleNormal  = decode(gNormal[sampleFullUV])

        bilinearW = standard bilinear from frac
        depthW    = gaussian(|centerDepth - sampleDepth| / centerDepth)
        normalW   = pow(dot(centerNormal, sampleNormal), normalSigma)

        result += indirectHalfRes[sampleCoord] × bilinearW × depthW × normalW

    return normalize(result)
```

**Optional Spatial Denoise**: 3×3 edge-aware blur with depth/normal weighting for additional smoothing.

**Key Uniforms**: `indirectHalfRes`, `gDepth`, `gNormal`, `fullResSize`, `halfResSize`, `upsampleDepthSigma`, `upsampleNormalSigma`

---

## 4. Integration Pass (SPG-009)

### 4.1 Combining with Direct Lighting

The final indirect diffuse term is added to the scene's direct lighting:

```
CombineLighting(uv):
    direct   = sceneDirect[uv]
    indirect = indirectDiffuse[uv]
    albedo   = gAlbedo[uv]
    metallic = gMaterial[uv].g

    // Metals don't receive diffuse indirect (only specular, future)
    diffuseWeight = 1 - metallic

    // Apply albedo (diffuse BRDF)
    indirectDiffuse = indirect × albedo × diffuseWeight

    return direct + indirectDiffuse × intensity
```

### 4.2 Integration Points in VS

LumOn output integrates into the existing VGE pipeline via a dedicated combine pass that binds:

- **sceneDirect** — Captured scene before post-processing
- **indirectDiffuse** — LumOn full-res output
- **gAlbedo / gMaterial** — G-Buffer for material modulation

---

## 5. C# Integration

### 5.1 Gather Pass

```
RenderGatherPass(render):
    Set viewport to half-res (width/2, height/2)
    Bind IndirectDiffuseHalfResFB

    Bind textures:
        probeRadiance0/1     (temporal output)
        probeAnchorPos/Normal
        gDepth, gNormal, gPosition

    Set uniforms: probeGridSize, probeSpacing, screenSize,
                  halfResSize, zNear/zFar, depthSigma,
                  normalSigma, intensity

    Draw fullscreen quad
    Restore viewport
```

### 5.2 Upsample Pass

```
RenderUpsamplePass(render):
    Bind IndirectDiffuseFullResFB

    Bind textures:
        indirectHalfRes
        gDepth, gNormal (guides)

    Set uniforms: fullResSize, halfResSize, zNear/zFar,
                  denoiseEnabled, upsampleDepthSigma,
                  upsampleNormalSigma, upsampleSpatialSigma

    Draw fullscreen quad
```

---

## 6. Full Render Pipeline Summary

```
OnRenderFrame(deltaTime, stage):
    if stage != AfterPostProcessing or !enabled: return

    Pass 1: RenderProbeAnchorPass()   // Build probe anchors
    Pass 2: RenderProbeTracePass()    // Trace rays per probe
    Pass 3: RenderTemporalPass()      // Temporal accumulation (skip frame 0)
    Pass 4: RenderGatherPass()        // Gather probes to pixels (half-res)
    Pass 5: RenderUpsamplePass()      // Bilateral upsample (if half-res)

    // Bookkeeping
    StoreViewProjMatrix()
    SwapAllBuffers()
    frameIndex++
```

---

## 7. Performance Budget

### 7.1 Per-Pass Cost Summary

| Pass         | Resolution | Texture Samples | ALU    | Bandwidth                 |
| ------------ | ---------- | --------------- | ------ | ------------------------- |
| Probe Anchor | 240×135    | 4/probe         | Low    | ~40 MB read, 0.5 MB write |
| Probe Trace  | 240×135    | 8×12×3/probe    | High   | ~50 MB                    |
| Temporal     | 240×135    | 22/probe        | Medium | ~3 MB                     |
| Gather       | 960×540    | 4×4 + 4 guides  | Medium | ~20 MB                    |
| Upsample     | 1920×1080  | 4×3             | Low    | ~30 MB                    |
| **Total**    |            |                 |        | **~145 MB**               |

### 7.2 Estimated Frame Time (RTX 3060)

| Pass         | Time (ms)   |
| ------------ | ----------- |
| Probe Anchor | 0.1         |
| Probe Trace  | 1.5         |
| Temporal     | 0.2         |
| Gather       | 0.4         |
| Upsample     | 0.3         |
| **Total**    | **~2.5 ms** |

Compare to per-pixel SSGI: ~5-8 ms

---

## 8. Debug Visualization

### 8.1 Debug Mode: SH Coefficients (DebugMode = 5)

Visualize DC (ambient) as RGB, directional magnitude as brightness.

### 8.2 Debug Mode: Interpolation Weights (DebugMode = 6)

Color pixels by dominant probe weight (R=w00, G=w10, B=w01+w11) to visualize interpolation.

---

## 9. Future Enhancements (M3+)

### 9.1 Variable Rate Gather

Sample more probes (3×3 instead of 2×2) in complex regions with high depth variance.

### 9.2 Specular Path (SPG-FT-004)

Add screen-space reflections by evaluating SH in reflect direction, modulated by roughness/Fresnel.

### 9.3 Async Compute (Vulkan Future)

Run probe trace and temporal passes on async compute queue, overlapping with shadow map rendering.

---

## 10. Implementation Checklist

### 10.1 Gather Pass

- [ ] Create `lumon_gather.vsh`
- [ ] Create `lumon_gather.fsh`
- [ ] Import `lumon_sh.ash` for SH evaluation
- [ ] Implement `LoadProbe()` helper (fetch anchor + radiance)
- [ ] Implement `UnpackSH()` helper
- [ ] Implement bilinear probe interpolation
- [ ] Implement `ComputeWeight()` edge-aware weight adjustment
- [ ] Handle invalid probes (skip with weight=0)
- [ ] Handle sky pixels (output zero)
- [ ] Create `LumOnGatherShaderProgram.cs`

### 10.2 Upsample Pass

- [ ] Create `lumon_upsample.vsh`
- [ ] Create `lumon_upsample.fsh`
- [ ] Implement bilateral upsample
- [ ] Add optional spatial denoise
- [ ] Create `LumOnUpsampleShaderProgram.cs`

### 10.3 Buffer Management

- [ ] Create `IndirectDiffuseHalfResFB` framebuffer
- [ ] Create `IndirectDiffuseFullResFB` framebuffer
- [ ] Handle resolution changes

### 10.4 Integration

- [ ] Implement `RenderGatherPass()` in renderer
- [ ] Implement `RenderUpsamplePass()` in renderer
- [ ] Create `lumon_combine.fsh` for lighting integration
- [ ] Implement `RenderCombinePass()` in renderer
- [ ] Wire up to final composite
- [ ] Ensure proper energy balance (avoid double-albedo)

### 10.5 Testing

- [ ] Verify SH evaluation output (DebugMode = 5)
- [ ] Check interpolation weights (DebugMode = 6)
- [ ] Test at half-res vs full-res
- [ ] Profile gather + upsample cost
- [ ] Check for edge leaking artifacts
- [ ] Verify bilateral weights respect depth discontinuities
- [ ] Test with denoiseEnabled on/off

---

## 11. Conclusion

This completes the LumOn architecture documentation. The six documents together define:

| Document                                              | Content                                  |
| ----------------------------------------------------- | ---------------------------------------- |
| [01-Core-Architecture](LumOn.01-Core-Architecture.md) | Config, component structure, integration |
| [02-Probe-Grid](LumOn.02-Probe-Grid.md)               | Probe placement and anchor generation    |
| [03-Radiance-Cache](LumOn.03-Radiance-Cache.md)       | SH encoding and storage                  |
| [04-Ray-Tracing](LumOn.04-Ray-Tracing.md)             | Per-probe screen-space ray marching      |
| [05-Temporal](LumOn.05-Temporal.md)                   | Reprojection and accumulation            |
| [06-Gather-Upsample](LumOn.06-Gather-Upsample.md)     | Pixel gathering and final output         |

**Next step**: Begin implementation of M1 (SPG v1) following these specifications.
