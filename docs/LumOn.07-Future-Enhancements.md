# LumOn Future Enhancements (M4/M5)

> **Document**: LumOn.07-Future-Enhancements.md  
> **Status**: Draft (Future Work)  
> **Dependencies**: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md) through [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md)  
> **Implements**: M4, M5, SPG-FT-001 through SPG-FT-005

---

## 1. Overview

This document covers planned enhancements beyond the M1-M3 core implementation:

| Milestone | Feature | Priority |
|-----------|---------|----------|
| **M4** | World-space voxel DDA fallback | Medium |
| **M5** | Surface cache integration | Low |
| **SPG-FT-001** | Motion vectors for dynamic objects | Medium |
| **SPG-FT-003** | Emissive injection | Low |
| **SPG-FT-004** | Specular/reflections path | Medium |

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

```csharp
public class VoxelRadianceGrid
{
    /// <summary>
    /// Resolution of the voxel grid (e.g., 128³ or 256³)
    /// </summary>
    public int Resolution { get; set; } = 128;
    
    /// <summary>
    /// World-space size of the grid in meters (centered on camera)
    /// </summary>
    public float WorldSize { get; set; } = 64.0f;
    
    /// <summary>
    /// Voxel size in meters (worldSize / resolution)
    /// </summary>
    public float VoxelSize => WorldSize / Resolution;
    
    /// <summary>
    /// 3D texture storing radiance (RGBA16F)
    /// RGB = radiance, A = opacity/occupancy
    /// </summary>
    public int RadianceTextureId { get; private set; }
    
    /// <summary>
    /// Center of grid in world-space (follows camera)
    /// </summary>
    public Vec3d GridCenter { get; set; }
}
```

### 2.3 Voxelization Pass

Inject scene radiance into voxel grid each frame:

```glsl
// lumon_voxelize.geom (geometry shader for voxelization)
#version 430 core

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

uniform mat4 voxelViewProj[3];  // Orthographic projections for X, Y, Z axes
uniform int dominantAxis;        // Axis with largest triangle projection

in vec3 vWorldPos[];
in vec3 vNormal[];
in vec3 vAlbedo[];

out vec3 gWorldPos;
out vec3 gNormal;
out vec3 gAlbedo;
flat out int gAxis;

void main() {
    // Project triangle along dominant axis for conservative rasterization
    gAxis = dominantAxis;
    
    for (int i = 0; i < 3; i++) {
        gWorldPos = vWorldPos[i];
        gNormal = vNormal[i];
        gAlbedo = vAlbedo[i];
        gl_Position = voxelViewProj[dominantAxis] * vec4(vWorldPos[i], 1.0);
        EmitVertex();
    }
    EndPrimitive();
}
```

```glsl
// lumon_voxelize.fsh
#version 430 core

layout(rgba16f, binding = 0) uniform image3D voxelGrid;

uniform vec3 gridMin;
uniform vec3 gridMax;
uniform int gridResolution;

in vec3 gWorldPos;
in vec3 gNormal;
in vec3 gAlbedo;

void main() {
    // Convert world position to voxel coordinate
    vec3 normalized = (gWorldPos - gridMin) / (gridMax - gridMin);
    ivec3 voxelCoord = ivec3(normalized * float(gridResolution));
    
    // Bounds check
    if (any(lessThan(voxelCoord, ivec3(0))) || 
        any(greaterThanEqual(voxelCoord, ivec3(gridResolution)))) {
        return;
    }
    
    // Compute radiance (simplified: albedo * ambient)
    vec3 radiance = gAlbedo * 0.5;  // TODO: proper lighting
    
    // Atomic average (simplified: just overwrite for now)
    imageStore(voxelGrid, voxelCoord, vec4(radiance, 1.0));
}
```

### 2.4 DDA Ray Marching

```glsl
// lumon_voxel_trace.ash
// 3D DDA algorithm for voxel grid traversal

struct VoxelHit {
    bool hit;
    vec3 radiance;
    float distance;
    ivec3 voxelCoord;
};

uniform sampler3D voxelGrid;
uniform vec3 gridMin;
uniform vec3 gridMax;
uniform int gridResolution;

// Convert world position to grid-normalized [0,1] coordinates
vec3 WorldToGrid(vec3 worldPos) {
    return (worldPos - gridMin) / (gridMax - gridMin);
}

// DDA voxel traversal
VoxelHit TraceVoxelDDA(vec3 originWS, vec3 dirWS, float maxDist) {
    VoxelHit result;
    result.hit = false;
    result.radiance = vec3(0.0);
    result.distance = maxDist;
    
    // Convert to grid space
    vec3 gridOrigin = WorldToGrid(originWS);
    vec3 gridDir = dirWS;  // Direction unchanged, just scaled
    
    // Voxel size in grid space
    float voxelSize = 1.0 / float(gridResolution);
    
    // Current voxel
    ivec3 voxel = ivec3(floor(gridOrigin * float(gridResolution)));
    
    // Step direction
    ivec3 step = ivec3(sign(gridDir));
    
    // Distance to next voxel boundary along each axis
    vec3 tMax;
    vec3 tDelta;
    
    for (int i = 0; i < 3; i++) {
        if (abs(gridDir[i]) < 0.0001) {
            tMax[i] = 1e30;
            tDelta[i] = 1e30;
        } else {
            float voxelBoundary = (float(voxel[i] + (step[i] > 0 ? 1 : 0))) * voxelSize;
            tMax[i] = (voxelBoundary - gridOrigin[i]) / gridDir[i];
            tDelta[i] = voxelSize / abs(gridDir[i]);
        }
    }
    
    // March through voxels
    float t = 0.0;
    int maxSteps = gridResolution * 2;
    
    for (int i = 0; i < maxSteps && t < maxDist; i++) {
        // Check bounds
        if (any(lessThan(voxel, ivec3(0))) || 
            any(greaterThanEqual(voxel, ivec3(gridResolution)))) {
            break;
        }
        
        // Sample voxel
        vec4 voxelData = texelFetch(voxelGrid, voxel, 0);
        
        if (voxelData.a > 0.5) {
            // Hit occupied voxel
            result.hit = true;
            result.radiance = voxelData.rgb;
            result.distance = t;
            result.voxelCoord = voxel;
            return result;
        }
        
        // Advance to next voxel (DDA step)
        if (tMax.x < tMax.y && tMax.x < tMax.z) {
            t = tMax.x;
            tMax.x += tDelta.x;
            voxel.x += step.x;
        } else if (tMax.y < tMax.z) {
            t = tMax.y;
            tMax.y += tDelta.y;
            voxel.y += step.y;
        } else {
            t = tMax.z;
            tMax.z += tDelta.z;
            voxel.z += step.z;
        }
    }
    
    return result;
}
```

### 2.5 Hybrid Screen + Voxel Tracing

```glsl
// In lumon_probe_trace.fsh (modified for M4)

RayResult TraceHybrid(vec3 originVS, vec3 dirVS, vec3 originWS, vec3 dirWS) {
    // First: try screen-space
    RayResult ssResult = MarchRay(originVS, dirVS);
    
    if (ssResult.hit) {
        return ssResult;
    }
    
    // Fallback: world-space voxel DDA
    VoxelHit voxelResult = TraceVoxelDDA(originWS, dirWS, rayMaxDistance);
    
    if (voxelResult.hit) {
        RayResult result;
        result.hit = true;
        result.radiance = voxelResult.radiance;
        result.distance = voxelResult.distance;
        return result;
    }
    
    // Both missed: sky fallback
    RayResult missResult;
    missResult.hit = false;
    return missResult;
}
```

### 2.6 Grid Scrolling (Camera Following)

```csharp
// Update grid center as camera moves
private void UpdateVoxelGridCenter(IRenderAPI render)
{
    Vec3d cameraPos = new Vec3d(
        render.CameraMatrixOrigin[0],
        render.CameraMatrixOrigin[1],
        render.CameraMatrixOrigin[2]
    );
    
    // Snap to voxel boundaries to reduce popping
    float voxelSize = voxelGrid.VoxelSize;
    Vec3d snappedCenter = new Vec3d(
        Math.Floor(cameraPos.X / voxelSize) * voxelSize,
        Math.Floor(cameraPos.Y / voxelSize) * voxelSize,
        Math.Floor(cameraPos.Z / voxelSize) * voxelSize
    );
    
    // Check if we need to scroll
    Vec3d delta = snappedCenter - voxelGrid.GridCenter;
    
    if (delta.Length() > voxelSize * 2) {
        // Scroll grid (copy existing data, clear edges)
        ScrollVoxelGrid(delta);
        voxelGrid.GridCenter = snappedCenter;
    }
}
```

### 2.7 Performance Considerations

| Aspect | Cost |
|--------|------|
| Voxelization | ~1-2ms (geometry-dependent) |
| 3D texture memory | 128³ × RGBA16F = 128 MB |
| DDA traversal | ~50 ALU per step |
| Hybrid overhead | Minimal (branch on miss) |

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

```glsl
// In G-Buffer pass, output motion vectors
layout(location = 5) out vec2 outMotionVector;

uniform mat4 prevModelViewProj;
uniform mat4 currModelViewProj;

void main() {
    // ... existing G-Buffer outputs ...
    
    // Compute screen-space motion
    vec4 currClip = currModelViewProj * vec4(worldPos, 1.0);
    vec4 prevClip = prevModelViewProj * vec4(prevWorldPos, 1.0);  // Animated position
    
    vec2 currScreen = currClip.xy / currClip.w;
    vec2 prevScreen = prevClip.xy / prevClip.w;
    
    outMotionVector = (currScreen - prevScreen) * 0.5;  // In UV space
}
```

### 3.3 Motion-Aware Reprojection

```glsl
// In lumon_temporal.fsh (modified)

uniform sampler2D motionVectors;
uniform bool useMotionVectors;

vec2 ReprojectWithMotion(vec3 posVS, vec2 screenUV) {
    if (useMotionVectors) {
        // Sample motion at probe's screen position
        vec2 motion = texture(motionVectors, screenUV).xy;
        
        // Apply motion to get previous UV
        return screenUV - motion;
    } else {
        // Fallback to camera-only reprojection
        return ReprojectToHistory(posVS);
    }
}
```

### 3.4 Per-Object Previous Transform Storage

```csharp
// Track previous frame transforms for animated entities
public class EntityTransformHistory
{
    private Dictionary<long, Mat4f> prevTransforms = new();
    
    public void StoreTransform(long entityId, Mat4f transform)
    {
        prevTransforms[entityId] = transform;
    }
    
    public Mat4f GetPrevTransform(long entityId)
    {
        return prevTransforms.TryGetValue(entityId, out var t) ? t : Mat4f.Identity;
    }
    
    public void Clear() => prevTransforms.Clear();
}
```

---

## 4. SPG-FT-003: Emissive Injection

### 4.1 Problem Statement

Current implementation treats all hit surfaces equally. Emissive surfaces (torches, lava, glowing blocks) should contribute more radiance.

### 4.2 Emissive-Aware Hit Shading

```glsl
// In lumon_probe_trace.fsh

uniform sampler2D gMaterial;  // R=rough, G=metal, B=emissive, A=reflectivity

vec3 SampleHitRadiance(vec2 hitUV) {
    vec3 sceneColor = texture(sceneTex, hitUV).rgb;
    vec4 material = texture(gMaterial, hitUV);
    
    float emissive = material.b;
    
    // Boost emissive contribution
    // Emissive surfaces emit light regardless of incoming illumination
    vec3 emissiveBoost = sceneColor * emissive * emissiveMultiplier;
    
    return sceneColor + emissiveBoost;
}
```

### 4.3 Direct Light at Hit Approximation

For better bounce lighting, approximate direct light at hit point:

```glsl
vec3 ApproximateDirectLightAtHit(vec2 hitUV, vec3 hitNormal) {
    // Sample shadow map at hit position (if available)
    // Or use screen-space shadow approximation
    
    float shadow = SampleShadowAtUV(hitUV);
    float NdotL = max(dot(hitNormal, sunDirection), 0.0);
    
    vec3 directLight = sunColor * NdotL * shadow * dayLight;
    
    return directLight;
}
```

---

## 5. SPG-FT-004: Specular/Reflections Path

### 5.1 Overview

Extend LumOn to handle specular reflections using:
1. Screen-space reflections (SSR) for near-field
2. Probe-based specular for rough surfaces
3. Voxel fallback for off-screen (M4)

### 5.2 Roughness-Based Probe Sampling

```glsl
// Rough surfaces can use probe SH directly (blurred reflection)
vec3 EvaluateSpecularFromProbes(vec4 shR, vec4 shG, vec4 shB, 
                                  vec3 reflectDir, float roughness) {
    if (roughness > 0.7) {
        // Very rough: use diffuse irradiance approximation
        return SHEvaluateDiffuse(shR, shG, shB, reflectDir);
    }
    
    // Moderate roughness: evaluate SH in reflection direction
    // This gives a soft, blurred reflection
    vec3 specular = SHEvaluate(shR, shG, shB, reflectDir);
    
    // Lerp toward diffuse based on roughness
    vec3 diffuse = SHEvaluateDiffuse(shR, shG, shB, reflectDir);
    
    return mix(specular, diffuse, roughness);
}
```

### 5.3 SSR + Probe Hybrid

```glsl
// Screen-space reflection with probe fallback
vec3 TraceSpecular(vec3 posVS, vec3 reflectDir, float roughness) {
    // Try SSR first (sharp reflections)
    if (roughness < 0.3) {
        RayResult ssr = MarchRay(posVS, reflectDir);
        if (ssr.hit) {
            return ssr.radiance;
        }
    }
    
    // Fallback to probe-based specular
    vec4 shR, shG, shB;
    GatherProbesSH(posVS, shR, shG, shB);
    
    return EvaluateSpecularFromProbes(shR, shG, shB, reflectDir, roughness);
}
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

```csharp
public struct SurfaceCard
{
    public Vec3f Position;      // World-space center
    public Vec3f Normal;        // Average normal
    public Vec2f Size;          // Card dimensions
    public int AtlasIndex;      // Index into radiance atlas
    public int AtlasOffset;     // Offset within atlas tile
}
```

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

```csharp
// Simplified greedy card placement
public List<SurfaceCard> GenerateSurfaceCards(VoxelChunk chunk)
{
    var cards = new List<SurfaceCard>();
    
    // For each visible face, try to expand into largest rectangle
    foreach (var face in chunk.VisibleFaces)
    {
        if (face.Processed) continue;
        
        // Greedy expand in U and V directions
        var card = ExpandCard(face, chunk);
        cards.Add(card);
        
        // Mark covered faces as processed
        MarkProcessed(card, chunk);
    }
    
    return cards;
}
```

### 6.5 Cache Update Strategy

```csharp
// Update a subset of cards each frame
private void UpdateSurfaceCache(IRenderAPI render)
{
    int cardsPerFrame = 256;  // Budget
    
    // Prioritize cards near camera or recently visible
    var cardsToUpdate = priorityQueue.Dequeue(cardsPerFrame);
    
    foreach (var card in cardsToUpdate)
    {
        // Render card's radiance into atlas
        RenderCardRadiance(card);
    }
}
```

### 6.6 Ray Hit → Cache Lookup

```glsl
// When ray hits surface, lookup pre-computed radiance
vec3 SampleSurfaceCache(vec3 hitPos, vec3 hitNormal) {
    // Find nearest card
    int cardIndex = FindNearestCard(hitPos, hitNormal);
    
    if (cardIndex < 0) {
        // No card coverage, fallback to direct sampling
        return texture(sceneTex, ProjectToScreen(hitPos)).rgb;
    }
    
    // Sample from atlas
    SurfaceCard card = cards[cardIndex];
    vec2 localUV = WorldToCardUV(hitPos, card);
    vec2 atlasUV = CardToAtlasUV(localUV, card.AtlasIndex, card.AtlasOffset);
    
    return texture(radianceAtlas, atlasUV).rgb;
}
```

---

## 7. Implementation Roadmap

### 7.1 Suggested Order

| Phase | Feature | Effort | Impact |
|-------|---------|--------|--------|
| 1 | SPG-FT-001 (Motion Vectors) | Medium | High (fixes ghosting) |
| 2 | SPG-FT-003 (Emissive) | Low | Medium (better torches) |
| 3 | M4 (Voxel Fallback) | High | High (off-screen GI) |
| 4 | SPG-FT-004 (Specular) | Medium | Medium (reflections) |
| 5 | M5 (Surface Cache) | Very High | High (multi-bounce) |

### 7.2 Prerequisites

| Feature | Requires |
|---------|----------|
| Motion Vectors | G-Buffer modification, entity transform tracking |
| Voxel Fallback | 3D texture support, geometry shader for voxelization |
| Surface Cache | Compute shaders (recommended), atlas management |

---

## 8. Config Extensions

```csharp
// Additional config for M4/M5 features
public class LumOnAdvancedConfig
{
    // ═══════════════════════════════════════════════════════════════
    // M4: Voxel Fallback
    // ═══════════════════════════════════════════════════════════════
    
    [JsonProperty]
    public bool VoxelFallbackEnabled { get; set; } = false;
    
    [JsonProperty]
    public int VoxelGridResolution { get; set; } = 128;
    
    [JsonProperty]
    public float VoxelGridWorldSize { get; set; } = 64.0f;
    
    // ═══════════════════════════════════════════════════════════════
    // SPG-FT-001: Motion Vectors
    // ═══════════════════════════════════════════════════════════════
    
    [JsonProperty]
    public bool MotionVectorsEnabled { get; set; } = false;
    
    // ═══════════════════════════════════════════════════════════════
    // SPG-FT-004: Specular
    // ═══════════════════════════════════════════════════════════════
    
    [JsonProperty]
    public bool SpecularEnabled { get; set; } = false;
    
    [JsonProperty]
    public float SpecularRoughnessThreshold { get; set; } = 0.3f;
    
    // ═══════════════════════════════════════════════════════════════
    // M5: Surface Cache
    // ═══════════════════════════════════════════════════════════════
    
    [JsonProperty]
    public bool SurfaceCacheEnabled { get; set; } = false;
    
    [JsonProperty]
    public int SurfaceCacheAtlasSize { get; set; } = 2048;
    
    [JsonProperty]
    public int SurfaceCacheCardsPerFrame { get; set; } = 256;
}
```

---

## 9. References

- **Lumen (UE5)**: [Unreal Engine 5 Lumen Technical Details](https://docs.unrealengine.com/5.0/en-US/lumen-global-illumination-and-reflections-in-unreal-engine/)
- **DDGI**: [Dynamic Diffuse Global Illumination](https://morgan3d.github.io/articles/2019-04-01-ddgi/)
- **Sparse Voxel Octrees**: [Efficient Sparse Voxel Octrees](https://research.nvidia.com/publication/efficient-sparse-voxel-octrees)
- **Surface Cache**: [Far Cry 5 GI](https://www.gdcvault.com/play/1025458/Global-Illumination-in-Far-Cry)

---

## 10. Document Index

| Document | Content |
|----------|---------|
| [LumOn.01-Core-Architecture](LumOn.01-Core-Architecture.md) | Config, components, integration |
| [LumOn.02-Probe-Grid](LumOn.02-Probe-Grid.md) | Probe placement, anchoring |
| [LumOn.03-Radiance-Cache](LumOn.03-Radiance-Cache.md) | SH encoding, storage |
| [LumOn.04-Ray-Tracing](LumOn.04-Ray-Tracing.md) | Screen-space ray marching |
| [LumOn.05-Temporal](LumOn.05-Temporal.md) | Reprojection, accumulation |
| [LumOn.06-Gather-Upsample](LumOn.06-Gather-Upsample.md) | Pixel gathering, output |
| **LumOn.07-Future-Enhancements** | M4/M5, voxels, surface cache |

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
