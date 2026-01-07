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

| Resolution | Content | Purpose |
|------------|---------|---------|
| Probe grid (e.g., 240×135) | SH radiance per probe | Ray-traced data |
| Half-res (e.g., 960×540) | Indirect diffuse RGB | Gathered irradiance |
| Full-res (e.g., 1920×1080) | Final indirect term | Upsampled output |

### 1.2 Why Half-Resolution Gather?

- **Cost reduction**: 4× fewer gather operations than full-res
- **Smooth result**: Indirect lighting is inherently low-frequency
- **Bilateral upsample**: Preserves edges using depth/normal guides

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

```glsl
// Reduce weight for probes with very different depth or normal
float EdgeAwareWeight(float baseWeight, 
                      float pixelDepth, float probeDepth,
                      vec3 pixelNormal, vec3 probeNormal,
                      float depthSigma, float normalSigma) {
    // Depth similarity
    float depthDiff = abs(pixelDepth - probeDepth);
    float depthWeight = exp(-depthDiff * depthDiff / (2.0 * depthSigma * depthSigma));
    
    // Normal similarity
    float normalDot = max(dot(pixelNormal, probeNormal), 0.0);
    float normalWeight = pow(normalDot, normalSigma);
    
    return baseWeight * depthWeight * normalWeight;
}
```

### 2.4 Full Gather Shader

```glsl
// lumon_gather.fsh
#version 330 core

@import "lumon_sh.ash"

// ═══════════════════════════════════════════════════════════════════════════
// Uniforms
// ═══════════════════════════════════════════════════════════════════════════

// Probe data (temporally accumulated)
uniform sampler2D probeRadiance0;
uniform sampler2D probeRadiance1;
uniform sampler2D probeAnchorPos;
uniform sampler2D probeAnchorNormal;

// G-Buffer (full resolution)
uniform sampler2D gDepth;
uniform sampler2D gNormal;
uniform sampler2D gPosition;

// Config
uniform ivec2 probeGridSize;
uniform int probeSpacing;
uniform ivec2 screenSize;
uniform ivec2 halfResSize;

uniform float zNear;
uniform float zFar;

uniform float depthSigma;    // e.g., 0.5
uniform float normalSigma;   // e.g., 8.0

uniform float intensity;     // Output multiplier

// ═══════════════════════════════════════════════════════════════════════════
// Inputs / Outputs
// ═══════════════════════════════════════════════════════════════════════════

in vec2 vTexCoord;

layout(location = 0) out vec4 outIndirect;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

// Unpack SH from 2-texture encoding
void UnpackSH(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB) {
    // DC terms
    shR.x = tex0.r;
    shG.x = tex0.g;
    shB.x = tex0.b;
    
    // Directional Y
    shR.y = tex0.a;
    shG.y = tex1.r;
    shB.y = tex1.g;
    
    // Directional Z and X (reconstructed from averages)
    float avgZ = tex1.b;
    float avgX = tex1.a;
    shR.z = avgZ;
    shG.z = avgZ;
    shB.z = avgZ;
    shR.w = avgX;
    shG.w = avgX;
    shB.w = avgX;
}

// ═══════════════════════════════════════════════════════════════════════════
// Gather Logic
// ═══════════════════════════════════════════════════════════════════════════

struct ProbeData {
    vec4 shR, shG, shB;
    vec3 posVS;
    vec3 normalVS;
    float valid;
    float depth;
};

ProbeData LoadProbe(ivec2 probeCoord) {
    ProbeData p;
    
    // Clamp to grid bounds
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSize - 1);
    
    // Load anchor
    vec4 anchorPos = texelFetch(probeAnchorPos, probeCoord, 0);
    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);
    
    p.posVS = anchorPos.xyz;
    p.valid = anchorPos.a;
    p.normalVS = normalize(anchorNormal.xyz * 2.0 - 1.0);
    p.depth = -anchorPos.z;  // Linearized depth (positive)
    
    // Load radiance SH
    vec4 rad0 = texelFetch(probeRadiance0, probeCoord, 0);
    vec4 rad1 = texelFetch(probeRadiance1, probeCoord, 0);
    UnpackSH(rad0, rad1, p.shR, p.shG, p.shB);
    
    return p;
}

float ComputeWeight(float bilinearWeight, 
                    float pixelDepth, float probeDepth,
                    vec3 pixelNormal, vec3 probeNormal,
                    float probeValid) {
    if (probeValid < 0.5) {
        return 0.0;  // Invalid probe
    }
    
    // Depth similarity
    float depthDiff = abs(pixelDepth - probeDepth) / max(pixelDepth, 0.01);
    float depthWeight = exp(-depthDiff * depthDiff / (2.0 * depthSigma * depthSigma));
    
    // Normal similarity
    float normalDot = max(dot(pixelNormal, probeNormal), 0.0);
    float normalWeight = pow(normalDot, normalSigma);
    
    // Reduce weight for edge probes
    float edgeFactor = probeValid;  // 0.5 for edges, 1.0 for solid
    
    return bilinearWeight * depthWeight * normalWeight * edgeFactor;
}

// ═══════════════════════════════════════════════════════════════════════════
// Main
// ═══════════════════════════════════════════════════════════════════════════

void main() {
    // Half-res pixel position
    ivec2 halfResPixel = ivec2(gl_FragCoord.xy);
    
    // Corresponding full-res position (center of 2x2 block)
    ivec2 fullResPixel = halfResPixel * 2 + 1;
    vec2 fullResUV = (vec2(fullResPixel) + 0.5) / vec2(screenSize);
    
    // Sample G-Buffer at full-res
    float depthSample = texture(gDepth, fullResUV).r;
    
    // Sky check
    if (depthSample > 0.9999) {
        outIndirect = vec4(0.0);
        return;
    }
    
    float pixelDepth = LinearizeDepth(depthSample);
    vec3 pixelNormal = normalize(texture(gNormal, fullResUV).xyz * 2.0 - 1.0);
    vec3 pixelPosVS = texture(gPosition, fullResUV).xyz;
    
    // Find enclosing probes
    // Convert full-res pixel to probe-space coordinates
    vec2 probeCoordF = vec2(fullResPixel) / float(probeSpacing);
    ivec2 baseProbe = ivec2(floor(probeCoordF));
    vec2 fracCoord = fract(probeCoordF);
    
    // Load 4 surrounding probes
    ProbeData p00 = LoadProbe(baseProbe + ivec2(0, 0));
    ProbeData p10 = LoadProbe(baseProbe + ivec2(1, 0));
    ProbeData p01 = LoadProbe(baseProbe + ivec2(0, 1));
    ProbeData p11 = LoadProbe(baseProbe + ivec2(1, 1));
    
    // Bilinear base weights
    float bw00 = (1.0 - fracCoord.x) * (1.0 - fracCoord.y);
    float bw10 = fracCoord.x * (1.0 - fracCoord.y);
    float bw01 = (1.0 - fracCoord.x) * fracCoord.y;
    float bw11 = fracCoord.x * fracCoord.y;
    
    // Compute edge-aware weights
    float w00 = ComputeWeight(bw00, pixelDepth, p00.depth, pixelNormal, p00.normalVS, p00.valid);
    float w10 = ComputeWeight(bw10, pixelDepth, p10.depth, pixelNormal, p10.normalVS, p10.valid);
    float w01 = ComputeWeight(bw01, pixelDepth, p01.depth, pixelNormal, p01.normalVS, p01.valid);
    float w11 = ComputeWeight(bw11, pixelDepth, p11.depth, pixelNormal, p11.normalVS, p11.valid);
    
    float totalWeight = w00 + w10 + w01 + w11;
    
    // If no valid probes, output zero
    if (totalWeight < 0.001) {
        outIndirect = vec4(0.0);
        return;
    }
    
    // Normalize weights
    w00 /= totalWeight;
    w10 /= totalWeight;
    w01 /= totalWeight;
    w11 /= totalWeight;
    
    // Interpolate SH coefficients
    vec4 shR = p00.shR * w00 + p10.shR * w10 + p01.shR * w01 + p11.shR * w11;
    vec4 shG = p00.shG * w00 + p10.shG * w10 + p01.shG * w01 + p11.shG * w11;
    vec4 shB = p00.shB * w00 + p10.shB * w10 + p01.shB * w01 + p11.shB * w11;
    
    // Evaluate diffuse irradiance at pixel's normal
    vec3 irradiance = SHEvaluateDiffuse(shR, shG, shB, pixelNormal);
    
    // Clamp negative values
    irradiance = max(irradiance, vec3(0.0));
    
    // Apply intensity
    irradiance *= intensity;
    
    outIndirect = vec4(irradiance, 1.0);
}
```

---

## 3. Upsample Pass (SPG-008)

### 3.1 Bilateral Upsample

The half-res indirect buffer is upsampled to full resolution using edge-aware bilateral filtering:

```glsl
// lumon_upsample.fsh
#version 330 core

// ═══════════════════════════════════════════════════════════════════════════
// Uniforms
// ═══════════════════════════════════════════════════════════════════════════

uniform sampler2D indirectHalfRes;  // Half-res indirect diffuse
uniform sampler2D gDepth;            // Full-res depth (guide)
uniform sampler2D gNormal;           // Full-res normal (guide)

uniform ivec2 fullResSize;
uniform ivec2 halfResSize;

uniform float zNear;
uniform float zFar;

uniform bool denoiseEnabled;

uniform float upsampleDepthSigma;   // e.g., 0.1
uniform float upsampleNormalSigma;  // e.g., 16.0
uniform float upsampleSpatialSigma; // e.g., 1.0

// ═══════════════════════════════════════════════════════════════════════════
// Inputs / Outputs
// ═══════════════════════════════════════════════════════════════════════════

in vec2 vTexCoord;

layout(location = 0) out vec4 outIndirect;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

// ═══════════════════════════════════════════════════════════════════════════
// Bilateral Upsample
// ═══════════════════════════════════════════════════════════════════════════

vec3 BilateralUpsample(vec2 fullResUV, float centerDepth, vec3 centerNormal) {
    // Map to half-res coordinates
    vec2 halfResUV = fullResUV;
    vec2 halfResCoord = halfResUV * vec2(halfResSize) - 0.5;
    ivec2 baseCoord = ivec2(floor(halfResCoord));
    vec2 fracCoord = fract(halfResCoord);
    
    vec3 result = vec3(0.0);
    float totalWeight = 0.0;
    
    // Sample 2x2 neighborhood in half-res
    for (int dy = 0; dy <= 1; dy++) {
        for (int dx = 0; dx <= 1; dx++) {
            ivec2 sampleCoord = baseCoord + ivec2(dx, dy);
            
            // Clamp to bounds
            sampleCoord = clamp(sampleCoord, ivec2(0), halfResSize - 1);
            
            // Map back to full-res for guide sampling
            vec2 sampleFullResUV = (vec2(sampleCoord) * 2.0 + 1.0) / vec2(fullResSize);
            
            // Sample guides at corresponding full-res location
            float sampleDepth = LinearizeDepth(texture(gDepth, sampleFullResUV).r);
            vec3 sampleNormal = normalize(texture(gNormal, sampleFullResUV).xyz * 2.0 - 1.0);
            
            // Bilinear weight
            float bx = (dx == 0) ? (1.0 - fracCoord.x) : fracCoord.x;
            float by = (dy == 0) ? (1.0 - fracCoord.y) : fracCoord.y;
            float bilinearWeight = bx * by;
            
            // Depth weight
            float depthDiff = abs(centerDepth - sampleDepth) / max(centerDepth, 0.01);
            float depthWeight = exp(-depthDiff * depthDiff / 
                                    (2.0 * upsampleDepthSigma * upsampleDepthSigma));
            
            // Normal weight
            float normalDot = max(dot(centerNormal, sampleNormal), 0.0);
            float normalWeight = pow(normalDot, upsampleNormalSigma);
            
            float weight = bilinearWeight * depthWeight * normalWeight;
            
            // Sample half-res indirect
            vec3 indirect = texelFetch(indirectHalfRes, sampleCoord, 0).rgb;
            
            result += indirect * weight;
            totalWeight += weight;
        }
    }
    
    if (totalWeight > 0.001) {
        result /= totalWeight;
    }
    
    return result;
}

// ═══════════════════════════════════════════════════════════════════════════
// Optional: Edge-Aware Spatial Denoise
// ═══════════════════════════════════════════════════════════════════════════

vec3 SpatialDenoise(vec2 fullResUV, vec3 centerColor, float centerDepth, vec3 centerNormal) {
    vec3 result = centerColor;
    float totalWeight = 1.0;
    
    // 3x3 kernel
    vec2 texelSize = 1.0 / vec2(fullResSize);
    
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            
            vec2 sampleUV = fullResUV + vec2(dx, dy) * texelSize;
            
            // Bounds check
            if (sampleUV.x < 0.0 || sampleUV.x > 1.0 ||
                sampleUV.y < 0.0 || sampleUV.y > 1.0) {
                continue;
            }
            
            float sampleDepth = LinearizeDepth(texture(gDepth, sampleUV).r);
            vec3 sampleNormal = normalize(texture(gNormal, sampleUV).xyz * 2.0 - 1.0);
            
            // Spatial weight (Gaussian)
            float dist = length(vec2(dx, dy));
            float spatialWeight = exp(-dist * dist / 
                                      (2.0 * upsampleSpatialSigma * upsampleSpatialSigma));
            
            // Depth weight
            float depthDiff = abs(centerDepth - sampleDepth) / max(centerDepth, 0.01);
            float depthWeight = exp(-depthDiff * depthDiff / 
                                    (2.0 * upsampleDepthSigma * upsampleDepthSigma));
            
            // Normal weight
            float normalDot = max(dot(centerNormal, sampleNormal), 0.0);
            float normalWeight = pow(normalDot, upsampleNormalSigma);
            
            float weight = spatialWeight * depthWeight * normalWeight;
            
            // Sample indirect at neighbor (already upsampled, or use half-res)
            // For efficiency, sample from current output (requires ping-pong or separate pass)
            // Here we sample half-res directly
            ivec2 halfResCoord = ivec2(sampleUV * vec2(halfResSize));
            vec3 sampleColor = texelFetch(indirectHalfRes, halfResCoord, 0).rgb;
            
            result += sampleColor * weight;
            totalWeight += weight;
        }
    }
    
    return result / totalWeight;
}

// ═══════════════════════════════════════════════════════════════════════════
// Main
// ═══════════════════════════════════════════════════════════════════════════

void main() {
    vec2 fullResUV = vTexCoord;
    
    // Sample guides at full resolution
    float depthSample = texture(gDepth, fullResUV).r;
    
    // Sky: no indirect
    if (depthSample > 0.9999) {
        outIndirect = vec4(0.0);
        return;
    }
    
    float centerDepth = LinearizeDepth(depthSample);
    vec3 centerNormal = normalize(texture(gNormal, fullResUV).xyz * 2.0 - 1.0);
    
    // Bilateral upsample from half-res
    vec3 indirect = BilateralUpsample(fullResUV, centerDepth, centerNormal);
    
    // Optional spatial denoise
    if (denoiseEnabled) {
        indirect = SpatialDenoise(fullResUV, indirect, centerDepth, centerNormal);
    }
    
    outIndirect = vec4(indirect, 1.0);
}
```

---

## 4. Integration Pass (SPG-009)

### 4.1 Combining with Direct Lighting

The final indirect diffuse term is added to the scene's direct lighting in the final composite shader:

```glsl
// In final.fsh or lighting combine pass

uniform sampler2D sceneDirect;       // Scene with direct lighting only
uniform sampler2D indirectDiffuse;   // LumOn output
uniform sampler2D gAlbedo;           // Surface albedo
uniform sampler2D gMaterial;         // Material properties

uniform float indirectIntensity;     // Global multiplier
uniform bool lumOnEnabled;

vec3 CombineLighting(vec2 uv) {
    vec3 direct = texture(sceneDirect, uv).rgb;
    
    if (!lumOnEnabled) {
        return direct;
    }
    
    vec3 indirect = texture(indirectDiffuse, uv).rgb;
    vec3 albedo = texture(gAlbedo, uv).rgb;
    
    // Material-based modulation
    vec4 material = texture(gMaterial, uv);
    float metallic = material.g;
    
    // Metals don't receive diffuse indirect (only specular, future)
    float diffuseWeight = 1.0 - metallic;
    
    // Apply albedo to indirect (diffuse BRDF)
    vec3 indirectDiffuse = indirect * albedo * diffuseWeight;
    
    // Combine
    vec3 finalColor = direct + indirectDiffuse * indirectIntensity;
    
    return finalColor;
}
```

### 4.2 Integration Points in VS

LumOn output integrates into the existing VGE pipeline:

```csharp
// In final composite renderer or dedicated combine pass

private void RenderLightingCombine(IRenderAPI render)
{
    // Bind output framebuffer (screen or intermediate)
    render.FrameBuffer = outputFB;
    
    combineShader.Use();
    
    // Direct lighting (captured before post-processing)
    combineShader.BindTexture2D("sceneDirect", 
        ssgiBufferManager.CapturedSceneTextureId, 0);
    
    // LumOn indirect
    combineShader.BindTexture2D("indirectDiffuse",
        lumOnBufferManager.IndirectDiffuseFullResFB.ColorTextureIds[0], 1);
    
    // G-Buffer for material modulation
    combineShader.BindTexture2D("gAlbedo",
        gBufferManager.AlbedoTextureId, 2);
    combineShader.BindTexture2D("gMaterial",
        gBufferManager.MaterialTextureId, 3);
    
    combineShader.Uniform("indirectIntensity", config.Intensity);
    combineShader.Uniform("lumOnEnabled", config.Enabled);
    
    RenderFullscreenQuad(render);
    
    combineShader.Stop();
}
```

---

## 5. C# Integration

### 5.1 Gather Pass

```csharp
private void RenderGatherPass(IRenderAPI render)
{
    int halfW = config.HalfResolution ? render.FrameWidth / 2 : render.FrameWidth;
    int halfH = config.HalfResolution ? render.FrameHeight / 2 : render.FrameHeight;
    
    render.FrameBuffer = bufferManager.IndirectDiffuseHalfResFB;
    GL.Viewport(0, 0, halfW, halfH);
    GL.Clear(ClearBufferMask.ColorBufferBit);
    
    gatherShader.Use();
    
    // Probe textures (from temporal pass output)
    gatherShader.BindTexture2D("probeRadiance0",
        bufferManager.ProbeRadianceRead.ColorTextureIds[0], 0);
    gatherShader.BindTexture2D("probeRadiance1",
        bufferManager.ProbeRadianceRead.ColorTextureIds[1], 1);
    gatherShader.BindTexture2D("probeAnchorPos",
        bufferManager.ProbeAnchorFB.ColorTextureIds[0], 2);
    gatherShader.BindTexture2D("probeAnchorNormal",
        bufferManager.ProbeAnchorFB.ColorTextureIds[1], 3);
    
    // G-Buffer guides
    gatherShader.BindTexture2D("gDepth",
        gBufferManager.DepthTextureId, 4);
    gatherShader.BindTexture2D("gNormal",
        gBufferManager.NormalTextureId, 5);
    gatherShader.BindTexture2D("gPosition",
        gBufferManager.PositionTextureId, 6);
    
    // Uniforms
    gatherShader.Uniform("probeGridSize",
        new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY));
    gatherShader.Uniform("probeSpacing", config.ProbeSpacingPx);
    gatherShader.Uniform("screenSize",
        new Vec2i(render.FrameWidth, render.FrameHeight));
    gatherShader.Uniform("halfResSize", new Vec2i(halfW, halfH));
    
    gatherShader.Uniform("zNear", render.ShaderUniforms.ZNear);
    gatherShader.Uniform("zFar", render.ShaderUniforms.ZFar);
    
    gatherShader.Uniform("depthSigma", 0.5f);
    gatherShader.Uniform("normalSigma", 8.0f);
    gatherShader.Uniform("intensity", config.Intensity);
    
    RenderFullscreenQuad(render);
    
    gatherShader.Stop();
    
    GL.Viewport(0, 0, render.FrameWidth, render.FrameHeight);
}
```

### 5.2 Upsample Pass

```csharp
private void RenderUpsamplePass(IRenderAPI render)
{
    render.FrameBuffer = bufferManager.IndirectDiffuseFullResFB;
    GL.Viewport(0, 0, render.FrameWidth, render.FrameHeight);
    GL.Clear(ClearBufferMask.ColorBufferBit);
    
    upsampleShader.Use();
    
    // Half-res input
    upsampleShader.BindTexture2D("indirectHalfRes",
        bufferManager.IndirectDiffuseHalfResFB.ColorTextureIds[0], 0);
    
    // Full-res guides
    upsampleShader.BindTexture2D("gDepth",
        gBufferManager.DepthTextureId, 1);
    upsampleShader.BindTexture2D("gNormal",
        gBufferManager.NormalTextureId, 2);
    
    int halfW = config.HalfResolution ? render.FrameWidth / 2 : render.FrameWidth;
    int halfH = config.HalfResolution ? render.FrameHeight / 2 : render.FrameHeight;
    
    upsampleShader.Uniform("fullResSize",
        new Vec2i(render.FrameWidth, render.FrameHeight));
    upsampleShader.Uniform("halfResSize", new Vec2i(halfW, halfH));
    
    upsampleShader.Uniform("zNear", render.ShaderUniforms.ZNear);
    upsampleShader.Uniform("zFar", render.ShaderUniforms.ZFar);
    
    upsampleShader.Uniform("denoiseEnabled", config.DenoiseEnabled);
    upsampleShader.Uniform("upsampleDepthSigma", 0.1f);
    upsampleShader.Uniform("upsampleNormalSigma", 16.0f);
    upsampleShader.Uniform("upsampleSpatialSigma", 1.0f);
    
    RenderFullscreenQuad(render);
    
    upsampleShader.Stop();
}
```

---

## 6. Full Render Pipeline Summary

```csharp
public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
{
    if (stage != EnumRenderStage.AfterPostProcessing) return;
    if (!config.Enabled) return;
    
    var render = api.Render;
    
    // ═══════════════════════════════════════════════════════════════════
    // Pass 1: Build Probe Anchors
    // ═══════════════════════════════════════════════════════════════════
    RenderProbeAnchorPass(render);
    
    // ═══════════════════════════════════════════════════════════════════
    // Pass 2: Trace Rays per Probe
    // ═══════════════════════════════════════════════════════════════════
    RenderProbeTracePass(render);
    
    // ═══════════════════════════════════════════════════════════════════
    // Pass 3: Temporal Accumulation
    // ═══════════════════════════════════════════════════════════════════
    if (!isFirstFrame)
    {
        RenderTemporalPass(render);
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // Pass 4: Gather Probes to Pixels (half-res)
    // ═══════════════════════════════════════════════════════════════════
    RenderGatherPass(render);
    
    // ═══════════════════════════════════════════════════════════════════
    // Pass 5: Bilateral Upsample to Full Resolution
    // ═══════════════════════════════════════════════════════════════════
    if (config.HalfResolution)
    {
        RenderUpsamplePass(render);
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // Bookkeeping
    // ═══════════════════════════════════════════════════════════════════
    StoreViewProjMatrix(render);
    bufferManager.SwapAllBuffers();
    frameIndex++;
    isFirstFrame = false;
}
```

---

## 7. Performance Budget

### 7.1 Per-Pass Cost Summary

| Pass | Resolution | Texture Samples | ALU | Bandwidth |
|------|------------|-----------------|-----|-----------|
| Probe Anchor | 240×135 | 4/probe | Low | ~40 MB read, 0.5 MB write |
| Probe Trace | 240×135 | 8×12×3/probe | High | ~50 MB |
| Temporal | 240×135 | 22/probe | Medium | ~3 MB |
| Gather | 960×540 | 4×4 + 4 guides | Medium | ~20 MB |
| Upsample | 1920×1080 | 4×3 | Low | ~30 MB |
| **Total** | | | | **~145 MB** |

### 7.2 Estimated Frame Time (RTX 3060)

| Pass | Time (ms) |
|------|-----------|
| Probe Anchor | 0.1 |
| Probe Trace | 1.5 |
| Temporal | 0.2 |
| Gather | 0.4 |
| Upsample | 0.3 |
| **Total** | **~2.5 ms** |

Compare to per-pixel SSGI: ~5-8 ms

---

## 8. Debug Visualization

### 8.1 Debug Mode: SH Coefficients (DebugMode = 5)

```glsl
// Visualize SH structure
if (debugMode == 5) {
    vec4 rad0 = texelFetch(probeRadiance0, probeCoord, 0);
    
    // Show DC (ambient) in RGB
    vec3 dc = rad0.rgb;
    
    // Show directional magnitude as brightness variation
    float dirMag = abs(rad0.a);  // First directional coefficient
    
    outColor = vec4(dc + dirMag * 0.5, 1.0);
}
```

### 8.2 Debug Mode: Interpolation Weights

```glsl
// Show which probes contribute to each pixel
if (debugMode == 6) {
    // Color based on dominant probe weight
    vec3 color = vec3(w00, w10, w01 + w11);  // RGB from weights
    outColor = vec4(color, 1.0);
}
```

---

## 9. Future Enhancements (M3+)

### 9.1 Variable Rate Gather

Sample more probes in complex regions (high depth variance):

```glsl
float complexity = ComputeLocalComplexity(fullResUV);
int sampleCount = mix(4, 9, complexity);  // 2x2 to 3x3
```

### 9.2 Specular Path (SPG-FT-004)

Add screen-space reflections using same probe data:

```glsl
vec3 reflectDir = reflect(-viewDir, normal);
vec3 specular = SHEvaluate(shR, shG, shB, reflectDir);
// Modulate by roughness, Fresnel
```

### 9.3 Async Compute (Vulkan Future)

- Probe trace and temporal on async compute queue
- Overlap with shadow map rendering

---

## 10. Implementation Checklist

- [ ] Create `lumon_gather.fsh` and `.vsh`
- [ ] Create `lumon_upsample.fsh` and `.vsh`
- [ ] Add half-res and full-res framebuffers to `LumOnBufferManager`
- [ ] Implement `RenderGatherPass()` in `LumOnRenderer`
- [ ] Implement `RenderUpsamplePass()` in `LumOnRenderer`
- [ ] Integrate output into final lighting combine
- [ ] Test with various probe spacings
- [ ] Profile and optimize bottlenecks
- [ ] Add debug visualizations

---

## 11. Conclusion

This completes the LumOn architecture documentation. The six documents together define:

| Document | Content |
|----------|---------|
| [01-Core-Architecture](LumOn.01-Core-Architecture.md) | Config, component structure, integration |
| [02-Probe-Grid](LumOn.02-Probe-Grid.md) | Probe placement and anchor generation |
| [03-Radiance-Cache](LumOn.03-Radiance-Cache.md) | SH encoding and storage |
| [04-Ray-Tracing](LumOn.04-Ray-Tracing.md) | Per-probe screen-space ray marching |
| [05-Temporal](LumOn.05-Temporal.md) | Reprojection and accumulation |
| [06-Gather-Upsample](LumOn.06-Gather-Upsample.md) | Pixel gathering and final output |

**Next step**: Begin implementation of M1 (SPG v1) following these specifications.
