# LumOn Future Enhancements (M4/M5)

> **Document**: LumOn.07-Future-Enhancements.md  
> **Status**: Draft (Future Work)  
> **Dependencies**: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md) through [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md)  
> **Implements**: M4, M5, SPG-FT-001 through SPG-FT-005

---

## 1. Overview

This document covers planned enhancements beyond the M1-M3 core implementation:

| Milestone      | Feature                            | Priority |
| -------------- | ---------------------------------- | -------- |
| **M4**         | World-space voxel DDA fallback     | Medium   |
| **M5**         | Surface cache integration          | Low      |
| **SPG-FT-001** | Motion vectors for dynamic objects | Medium   |
| **SPG-FT-003** | Emissive injection                 | Low      |
| **SPG-FT-004** | Specular/reflections path          | Medium   |

### 1.1 Dependencies

```
M1-M3 (Core LumOn) ──┬──▶ M4 (Voxel Fallback)
                     │
                     ├──▶ SPG-FT-001 (Motion Vectors)
                     │
                     └──▶ SPG-FT-004 (Specular Path)
                              │
                              ▼
                         M5 (Surface Cache)
```

---

## 2. M4: World-Space Voxel DDA Fallback (SPG-FT-002)

### 2.1 Problem Statement

Screen-space ray marching fails when:

- Ray exits screen bounds
- Target geometry is occluded in screen-space
- Camera looks away from reflective surfaces

**Solution**: Fall back to world-space voxel traversal using DDA (Digital Differential Analyzer).

### 2.2 Voxel Grid Structure

**VoxelRadianceGrid** stores a 3D texture (RGBA16F) centered on the camera:

- **Resolution**: 128³ or 256³
- **WorldSize**: ~64m (configurable)
- **Storage**: RGB=radiance, A=opacity/occupancy
- **VoxelSize**: WorldSize / Resolution

### 2.3 Voxelization Pass

Inject scene radiance into voxel grid each frame:

```
VoxelizeGeometry(triangle):
    // Geometry shader: project along dominant axis for conservative rasterization
    dominantAxis = axis with largest triangle projection
    project triangle via orthographic voxelViewProj[dominantAxis]

    // Fragment shader: write to 3D texture
    voxelCoord = (worldPos - gridMin) / (gridMax - gridMin) × resolution
    if voxelCoord in bounds:
        radiance = albedo × lighting  // simplified
        imageStore(voxelGrid, voxelCoord, (radiance, 1.0))
```

### 2.4 DDA Ray Marching

```
TraceVoxelDDA(originWS, dirWS, maxDist) -> VoxelHit:
    gridOrigin = WorldToGrid(originWS)  // Normalize to [0,1]
    voxel = floor(gridOrigin × resolution)
    step = sign(dirWS)

    // Compute distance to next voxel boundary per axis
    tMax[axis] = distance to next boundary along axis
    tDelta[axis] = voxelSize / |dirWS[axis]|

    for maxSteps iterations:
        if voxel out of bounds: break

        voxelData = texelFetch(voxelGrid, voxel)
        if voxelData.a > 0.5:  // Hit occupied voxel
            return Hit(radiance=voxelData.rgb, distance=t)

        // DDA step: advance along axis with smallest tMax
        minAxis = argmin(tMax)
        t = tMax[minAxis]
        tMax[minAxis] += tDelta[minAxis]
        voxel[minAxis] += step[minAxis]

    return Miss
```

### 2.5 Hybrid Screen + Voxel Tracing

```
TraceHybrid(originVS, dirVS, originWS, dirWS):
    ssResult = MarchRay(originVS, dirVS)  // Screen-space first
    if ssResult.hit: return ssResult

    voxelResult = TraceVoxelDDA(originWS, dirWS)  // Fallback
    if voxelResult.hit: return voxelResult

    return SkyFallback
```

### 2.6 Grid Scrolling (Camera Following)

```
UpdateVoxelGridCenter(cameraPos):
    // Snap to voxel boundaries to reduce popping
    snappedCenter = floor(cameraPos / voxelSize) × voxelSize

    if distance(snappedCenter, gridCenter) > voxelSize × 2:
        ScrollVoxelGrid(delta)  // Copy existing data, clear edges
        gridCenter = snappedCenter
```

### 2.7 Performance Considerations

| Aspect            | Cost                        |
| ----------------- | --------------------------- |
| Voxelization      | ~1-2ms (geometry-dependent) |
| 3D texture memory | 128³ × RGBA16F = 128 MB     |
| DDA traversal     | ~50 ALU per step            |
| Hybrid overhead   | Minimal (branch on miss)    |

**Optimizations**:

- Sparse voxel octree (SVO) for large worlds
- Clipmap for multi-resolution
- Async voxelization (spread over frames)

---

## 3. SPG-FT-001: Motion Vectors for Dynamic Objects

### 3.1 Problem Statement

Current temporal reprojection assumes static geometry. Moving objects (entities, animations) cause:

- Ghosting trails behind moving objects
- Incorrect history rejection (motion mistaken for disocclusion)

### 3.2 Motion Vector Buffer

Add motion vector output to G-Buffer pass:

```
// Per vertex/fragment:
currScreen = (currModelViewProj × worldPos).xy / w
prevScreen = (prevModelViewProj × prevWorldPos).xy / w  // Animated position
outMotionVector = (currScreen - prevScreen) × 0.5  // In UV space
```

### 3.3 Motion-Aware Reprojection

```
ReprojectWithMotion(posVS, screenUV):
    if useMotionVectors:
        motion = sample(motionVectors, screenUV)
        return screenUV - motion  // Previous frame UV
    else:
        return ReprojectToHistory(posVS)  // Camera-only fallback
```

### 3.4 Per-Object Previous Transform Storage

Track previous frame transforms for animated entities using a dictionary (`entityId → prevTransform`). Store current transform at frame end, retrieve for motion vector computation.

---

## 4. SPG-FT-003: Emissive Injection

### 4.1 Problem Statement

Current implementation treats all hit surfaces equally. Emissive surfaces (torches, lava, glowing blocks) should contribute more radiance.

### 4.2 Emissive-Aware Hit Shading

```
SampleHitRadiance(hitUV):
    sceneColor = sample(sceneTex, hitUV)
    emissive = sample(gMaterial, hitUV).b

    // Emissive surfaces emit light regardless of incoming illumination
    emissiveBoost = sceneColor × emissive × emissiveMultiplier
    return sceneColor + emissiveBoost
```

### 4.3 Direct Light at Hit Approximation

For better bounce lighting, approximate direct light at hit point by sampling shadow map (if available) and computing `sunColor × NdotL × shadow × dayLight`.

---

## 5. SPG-FT-004: Specular/Reflections Path

### 5.1 Overview

Extend LumOn to handle specular reflections using:

1. Screen-space reflections (SSR) for near-field
2. Probe-based specular for rough surfaces
3. Voxel fallback for off-screen (M4)

### 5.2 Roughness-Based Probe Sampling

```
EvaluateSpecularFromProbes(SH, reflectDir, roughness):
    if roughness > 0.7:
        return SHEvaluateDiffuse(SH, reflectDir)  // Very rough

    specular = SHEvaluate(SH, reflectDir)  // Sharp direction
    diffuse = SHEvaluateDiffuse(SH, reflectDir)

    return lerp(specular, diffuse, roughness)
```

### 5.3 SSR + Probe Hybrid

```
TraceSpecular(posVS, reflectDir, roughness):
    // Try SSR first for sharp reflections
    if roughness < 0.3:
        ssr = MarchRay(posVS, reflectDir)
        if ssr.hit: return ssr.radiance

    // Fallback to probe-based specular
    SH = GatherProbesSH(posVS)
    return EvaluateSpecularFromProbes(SH, reflectDir, roughness)
```

### 5.4 Roughness Mip Chain (Future)

For high-quality reflections, pre-filter environment into mip chain:

```
Mip 0: Sharp reflection (roughness ~0)
Mip 1: Slight blur (roughness ~0.25)
Mip 2: Medium blur (roughness ~0.5)
Mip 3: Heavy blur (roughness ~0.75)
Mip 4: Fully diffuse (roughness ~1.0)
```

---

## 6. M5: Surface Cache Integration (SPG-FT-005)

### 6.1 Concept

A **Surface Cache** decouples hit shading from ray tracing:

- Store pre-computed radiance on surface "cards" (simplified geometry)
- Ray hits sample from cache instead of computing lighting
- Enables multi-bounce without recursive tracing

### 6.2 Surface Card Structure

**SurfaceCard** contains: Position (world-space center), Normal, Size, AtlasIndex, AtlasOffset.

### 6.3 Radiance Atlas

```
┌────────────────────────────────────────┐
│ Card 0   │ Card 1   │ Card 2   │ ...  │
│ 16×16    │ 16×16    │ 16×16    │      │
├──────────┼──────────┼──────────┼──────┤
│ Card N   │ Card N+1 │ ...      │      │
│          │          │          │      │
└────────────────────────────────────────┘
Atlas: 2048×2048 RGBA16F
Tiles: 16×16 each = 16K cards
```

### 6.4 Card Placement (Greedy Quads)

Use greedy meshing to place cards on voxel surfaces:

1. For each visible face, expand into largest rectangle in U/V directions
2. Create card from expanded region
3. Mark covered faces as processed
4. Repeat until all faces covered

### 6.5 Cache Update Strategy

Update a subset of cards each frame (e.g., 256 per frame). Prioritize cards near camera or recently visible. Render each card's radiance into atlas tile.

### 6.6 Ray Hit → Cache Lookup

```
SampleSurfaceCache(hitPos, hitNormal):
    cardIndex = FindNearestCard(hitPos, hitNormal)
    if cardIndex < 0:
        return sample(sceneTex, ProjectToScreen(hitPos))  // Fallback

    card = cards[cardIndex]
    localUV = WorldToCardUV(hitPos, card)
    atlasUV = CardToAtlasUV(localUV, card.atlasIndex, card.atlasOffset)
    return sample(radianceAtlas, atlasUV)
```

---

## 7. Implementation Roadmap

### 7.1 Suggested Order

| Phase | Feature                     | Effort    | Impact                  |
| ----- | --------------------------- | --------- | ----------------------- |
| 1     | SPG-FT-001 (Motion Vectors) | Medium    | High (fixes ghosting)   |
| 2     | SPG-FT-003 (Emissive)       | Low       | Medium (better torches) |
| 3     | M4 (Voxel Fallback)         | High      | High (off-screen GI)    |
| 4     | SPG-FT-004 (Specular)       | Medium    | Medium (reflections)    |
| 5     | M5 (Surface Cache)          | Very High | High (multi-bounce)     |

### 7.2 Prerequisites

| Feature        | Requires                                             |
| -------------- | ---------------------------------------------------- |
| Motion Vectors | G-Buffer modification, entity transform tracking     |
| Voxel Fallback | 3D texture support, geometry shader for voxelization |
| Surface Cache  | Compute shaders (recommended), atlas management      |

---

## 8. Config Extensions

| Feature                | Config Property              | Default |
| ---------------------- | ---------------------------- | ------- |
| **M4: Voxel Fallback** | `VoxelFallbackEnabled`       | false   |
|                        | `VoxelGridResolution`        | 128     |
|                        | `VoxelGridWorldSize`         | 64.0    |
| **Motion Vectors**     | `MotionVectorsEnabled`       | false   |
| **Specular**           | `SpecularEnabled`            | false   |
|                        | `SpecularRoughnessThreshold` | 0.3     |
| **Surface Cache**      | `SurfaceCacheEnabled`        | false   |
|                        | `SurfaceCacheAtlasSize`      | 2048    |
|                        | `SurfaceCacheCardsPerFrame`  | 256     |

---

## 9. References

- **Lumen (UE5)**: [Unreal Engine 5 Lumen Technical Details](https://docs.unrealengine.com/5.0/en-US/lumen-global-illumination-and-reflections-in-unreal-engine/)
- **DDGI**: [Dynamic Diffuse Global Illumination](https://morgan3d.github.io/articles/2019-04-01-ddgi/)
- **Sparse Voxel Octrees**: [Efficient Sparse Voxel Octrees](https://research.nvidia.com/publication/efficient-sparse-voxel-octrees)
- **Surface Cache**: [Far Cry 5 GI](https://www.gdcvault.com/play/1025458/Global-Illumination-in-Far-Cry)

---

## 10. Document Index

| Document                                                    | Content                         |
| ----------------------------------------------------------- | ------------------------------- |
| [LumOn.01-Core-Architecture](LumOn.01-Core-Architecture.md) | Config, components, integration |
| [LumOn.02-Probe-Grid](LumOn.02-Probe-Grid.md)               | Probe placement, anchoring      |
| [LumOn.03-Radiance-Cache](LumOn.03-Radiance-Cache.md)       | SH encoding, storage            |
| [LumOn.04-Ray-Tracing](LumOn.04-Ray-Tracing.md)             | Screen-space ray marching       |
| [LumOn.05-Temporal](LumOn.05-Temporal.md)                   | Reprojection, accumulation      |
| [LumOn.06-Gather-Upsample](LumOn.06-Gather-Upsample.md)     | Pixel gathering, output         |
| **LumOn.07-Future-Enhancements**                            | M4/M5, voxels, surface cache    |

---

## 11. Implementation Checklist

### 11.1 SPG-FT-001: Motion Vectors (Priority: Medium)

- [ ] Add motion vector output to G-Buffer pass
- [ ] Create `EntityTransformHistory` class
- [ ] Store previous frame transforms per entity
- [ ] Update temporal shader to use motion vectors
- [ ] Add `MotionVectorsEnabled` config option

### 11.2 SPG-FT-003: Emissive Injection (Priority: Low)

- [ ] Sample `gMaterial.b` (emissive) at hit points
- [ ] Add `emissiveMultiplier` config option
- [ ] Boost contribution from emissive surfaces
- [ ] Test with torches, lava, glowing blocks

### 11.3 M4: Voxel DDA Fallback (Priority: Medium)

- [ ] Create `VoxelRadianceGrid` class
- [ ] Allocate 3D texture (128³ RGBA16F)
- [ ] Implement voxelization geometry shader
- [ ] Implement DDA ray marching in `lumon_voxel_trace.ash`
- [ ] Add hybrid screen + voxel trace logic
- [ ] Implement grid scrolling (camera follow)
- [ ] Add `VoxelFallbackEnabled` config option

### 11.4 SPG-FT-004: Specular Path (Priority: Medium)

- [ ] Add roughness-based probe sampling
- [ ] Implement SSR + probe hybrid logic
- [ ] Add `SpecularEnabled` config option
- [ ] Test with various roughness values

### 11.5 M5: Surface Cache (Priority: Low)

- [ ] Design `SurfaceCard` data structure
- [ ] Allocate radiance atlas texture
- [ ] Implement greedy card placement
- [ ] Implement card update scheduling
- [ ] Add ray hit → cache lookup
- [ ] Add `SurfaceCacheEnabled` config option
