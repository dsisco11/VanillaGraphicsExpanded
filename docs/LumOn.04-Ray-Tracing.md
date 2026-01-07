# LumOn Ray Tracing Pass

> **Document**: LumOn.04-Ray-Tracing.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md), [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md)  
> **Implements**: SPG-004

---

## 1. Overview

The ray tracing pass is the core of LumOn. For each valid probe:

1. **Generate** `N` ray directions distributed over the hemisphere
2. **March** each ray through screen-space using depth buffer
3. **Sample** hit radiance from the captured scene texture
4. **Accumulate** radiance into SH coefficients
5. **Handle** misses with sky/ambient fallback

### 1.1 Key Differences from Per-Pixel SSGI

| Aspect           | Per-Pixel SSGI | LumOn Probe Tracing   |
| ---------------- | -------------- | --------------------- |
| Rays per frame   | pixels × rays  | probes × rays         |
| At 1080p, 8 rays | 16.6M rays     | 259K rays (64× fewer) |
| Output           | Direct RGB     | SH coefficients       |
| Reuse            | Per-pixel      | Shared by ~64 pixels  |

---

## 2. Ray Direction Generation

### 2.1 Hemisphere Distribution

Rays should be distributed over the hemisphere oriented by the probe's surface normal. Requirements:

- **Uniform coverage**: No clustering or gaps
- **Low discrepancy**: Quasi-random for smooth convergence
- **Temporal jitter**: Different samples each frame

### 2.2 Hammersley Sequence

```glsl
// Low-discrepancy 2D sequence
vec2 Hammersley(uint i, uint N) {
    // Van der Corput radical inverse in base 2
    uint bits = i;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    float radicalInverse = float(bits) * 2.3283064365386963e-10;

    return vec2(float(i) / float(N), radicalInverse);
}
```

### 2.3 Hemisphere Mapping (Cosine-Weighted)

```glsl
// Map 2D sample to cosine-weighted hemisphere direction
// Cosine weighting naturally gives more samples toward normal (diffuse importance)
vec3 CosineSampleHemisphere(vec2 xi) {
    float phi = 2.0 * 3.14159265 * xi.x;
    float cosTheta = sqrt(1.0 - xi.y);
    float sinTheta = sqrt(xi.y);

    return vec3(
        sinTheta * cos(phi),
        sinTheta * sin(phi),
        cosTheta
    );
}

// Transform hemisphere sample to be oriented around normal
vec3 OrientHemisphere(vec3 localDir, vec3 normal) {
    // Build tangent frame
    vec3 up = abs(normal.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, normal));
    vec3 bitangent = cross(normal, tangent);

    // Transform to world/view space
    return tangent * localDir.x + bitangent * localDir.y + normal * localDir.z;
}
```

### 2.4 Temporal Jittering

```glsl
// Add frame-based jitter to prevent temporal patterns
vec2 JitteredHammersley(uint rayIndex, uint rayCount, uint frameIndex, uvec2 probeCoord) {
    // Hash probe coord + frame for unique jitter per probe per frame
    uint seed = probeCoord.x * 1973u + probeCoord.y * 9277u + frameIndex * 26699u;
    float jitter = fract(float(seed) * 0.00000001);

    vec2 xi = Hammersley(rayIndex, rayCount);
    xi.x = fract(xi.x + jitter);  // Rotate samples around hemisphere

    return xi;
}
```

---

## 3. Screen-Space Ray Marching

### 3.1 Algorithm Overview

```
1. Start at probe position (view-space)
2. Step along ray direction in view-space
3. At each step, project to screen and sample depth
4. Compare ray depth to scene depth
5. If ray goes behind scene → potential hit
6. Refine hit position, sample radiance
7. If ray exits screen or exceeds max distance → miss
```

### 3.2 Core Marching Loop

```glsl
// Screen-space ray march parameters (from config)
uniform int raySteps;           // e.g., 12
uniform float rayMaxDistance;   // e.g., 10.0 meters
uniform float rayThickness;     // e.g., 0.5 meters

// G-Buffer and scene
uniform sampler2D gDepth;
uniform sampler2D gPosition;
uniform sampler2D sceneTex;     // Captured lit scene

uniform mat4 projection;
uniform vec2 screenSize;
uniform float zNear;
uniform float zFar;

struct RayHit {
    bool hit;
    vec3 radiance;
    float distance;
    vec2 hitUV;
};

// Project view-space position to screen UV
vec2 ProjectToScreen(vec3 posVS) {
    vec4 clipPos = projection * vec4(posVS, 1.0);
    vec3 ndc = clipPos.xyz / clipPos.w;
    return ndc.xy * 0.5 + 0.5;
}

// Linearize depth
float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

// Main ray march function
RayHit RayMarch(vec3 originVS, vec3 dirVS) {
    RayHit result;
    result.hit = false;
    result.radiance = vec3(0.0);
    result.distance = 0.0;
    result.hitUV = vec2(-1.0);

    // Step size (adaptive: larger steps far from camera)
    float stepSize = rayMaxDistance / float(raySteps);

    vec3 rayPos = originVS;
    float travelDist = 0.0;

    // Previous step's depth for thickness check
    float prevRayDepth = -originVS.z;
    float prevSceneDepth = prevRayDepth;

    for (int i = 0; i < raySteps; i++) {
        // Advance ray
        rayPos += dirVS * stepSize;
        travelDist += stepSize;

        // Check if exceeded max distance
        if (travelDist > rayMaxDistance) {
            break;
        }

        // Project to screen
        vec2 screenUV = ProjectToScreen(rayPos);

        // Check if outside screen bounds
        if (screenUV.x < 0.0 || screenUV.x > 1.0 ||
            screenUV.y < 0.0 || screenUV.y > 1.0) {
            break;  // Ray exited screen
        }

        // Sample scene depth at this screen position
        float depthSample = texture(gDepth, screenUV).r;
        float sceneDepth = LinearizeDepth(depthSample);

        // Ray depth (positive, into screen)
        float rayDepth = -rayPos.z;

        // Check for intersection (ray went behind scene)
        if (rayDepth > sceneDepth) {
            // Thickness test: only hit if within reasonable thickness
            float depthDiff = rayDepth - sceneDepth;

            if (depthDiff < rayThickness) {
                // Potential hit! Refine position
                result.hit = true;
                result.hitUV = screenUV;
                result.distance = travelDist;

                // Sample radiance from scene
                result.radiance = texture(sceneTex, screenUV).rgb;

                // Optional: Check for back-face hit
                // (skip if hit surface is facing away from ray origin)

                return result;
            } else {
                // Ray passed through thin geometry or behind occluder
                // Continue marching (might hit something else)
            }
        }

        prevRayDepth = rayDepth;
        prevSceneDepth = sceneDepth;
    }

    return result;  // Miss
}
```

### 3.3 Adaptive Step Size (Enhancement)

```glsl
// Hierarchical ray march: start coarse, refine on potential hit
RayHit RayMarchHierarchical(vec3 originVS, vec3 dirVS) {
    RayHit result;
    result.hit = false;

    // Coarse pass: large steps
    int coarseSteps = raySteps / 2;
    float coarseStepSize = rayMaxDistance / float(coarseSteps);

    vec3 rayPos = originVS;
    vec3 prevPos = originVS;

    for (int i = 0; i < coarseSteps; i++) {
        prevPos = rayPos;
        rayPos += dirVS * coarseStepSize;

        vec2 screenUV = ProjectToScreen(rayPos);
        if (screenUV.x < 0.0 || screenUV.x > 1.0 ||
            screenUV.y < 0.0 || screenUV.y > 1.0) {
            break;
        }

        float sceneDepth = LinearizeDepth(texture(gDepth, screenUV).r);
        float rayDepth = -rayPos.z;

        if (rayDepth > sceneDepth) {
            // Found potential hit region, refine with binary search
            result = BinarySearchRefinement(prevPos, rayPos, 4);
            if (result.hit) return result;
        }
    }

    return result;
}

RayHit BinarySearchRefinement(vec3 start, vec3 end, int iterations) {
    RayHit result;
    result.hit = false;

    for (int i = 0; i < iterations; i++) {
        vec3 mid = (start + end) * 0.5;
        vec2 screenUV = ProjectToScreen(mid);

        float sceneDepth = LinearizeDepth(texture(gDepth, screenUV).r);
        float rayDepth = -mid.z;

        if (rayDepth > sceneDepth) {
            end = mid;  // Hit is in first half
        } else {
            start = mid;  // Hit is in second half
        }
    }

    // Final position
    vec3 hitPos = (start + end) * 0.5;
    vec2 hitUV = ProjectToScreen(hitPos);

    float sceneDepth = LinearizeDepth(texture(gDepth, hitUV).r);
    float rayDepth = -hitPos.z;
    float depthDiff = abs(rayDepth - sceneDepth);

    if (depthDiff < rayThickness) {
        result.hit = true;
        result.hitUV = hitUV;
        result.radiance = texture(sceneTex, hitUV).rgb;
        result.distance = length(hitPos - start);
    }

    return result;
}
```

---

## 4. Hit Shading & Fallback

### 4.1 Hit Radiance Sampling

```glsl
// Sample radiance at hit point with filtering
vec3 SampleHitRadiance(vec2 hitUV, vec3 rayDir) {
    // Basic: sample scene texture
    vec3 radiance = texture(sceneTex, hitUV).rgb;

    // Optional: Check if hit is emissive (boost contribution)
    // vec4 material = texture(gMaterial, hitUV);
    // float emissive = material.b;
    // radiance *= (1.0 + emissive * 10.0);

    return radiance;
}
```

### 4.2 Sky Fallback

```glsl
// Sky radiance for missed rays
uniform vec3 skyColorZenith;    // From VS ambient
uniform vec3 skyColorHorizon;
uniform vec3 sunDirection;
uniform vec3 sunColor;
uniform float sunIntensity;

vec3 SampleSkyRadiance(vec3 dir) {
    // Simple gradient sky
    float upness = dir.y * 0.5 + 0.5;  // 0 = horizon, 1 = zenith
    vec3 skyColor = mix(skyColorHorizon, skyColorZenith, upness);

    // Add sun contribution (soft)
    float sunDot = max(dot(dir, sunDirection), 0.0);
    float sunFactor = pow(sunDot, 32.0);  // Soft sun disk
    vec3 sunContrib = sunColor * sunFactor * sunIntensity;

    return skyColor + sunContrib;
}
```

### 4.3 Distance Falloff

```glsl
// Attenuate contribution by distance (inverse square with offset)
float DistanceFalloff(float distance) {
    // Avoid division by zero, soft falloff
    return 1.0 / (1.0 + distance * distance);
}
```

---

## 5. Full Fragment Shader

### 5.1 lumon_probe_trace.fsh

```glsl
// lumon_probe_trace.fsh
#version 330 core

// ═══════════════════════════════════════════════════════════════════════════
// Includes
// ═══════════════════════════════════════════════════════════════════════════

@import "lumon_sh.ash"

// ═══════════════════════════════════════════════════════════════════════════
// Uniforms
// ═══════════════════════════════════════════════════════════════════════════

// Probe anchor data
uniform sampler2D probeAnchorPos;    // posVS.xyz, valid
uniform sampler2D probeAnchorNormal; // normalVS.xyz (encoded)

// Scene data
uniform sampler2D gDepth;
uniform sampler2D sceneTex;          // Captured lit scene

// Matrices
uniform mat4 projection;

// Screen info
uniform vec2 screenSize;
uniform ivec2 probeGridSize;

// Ray tracing parameters
uniform int raysPerProbe;           // e.g., 8
uniform int raySteps;               // e.g., 12
uniform float rayMaxDistance;       // e.g., 10.0
uniform float rayThickness;         // e.g., 0.5

// Temporal
uniform uint frameIndex;

// Depth linearization
uniform float zNear;
uniform float zFar;

// Sky fallback
uniform vec3 skyColorZenith;
uniform vec3 skyColorHorizon;
uniform vec3 sunDirection;
uniform vec3 sunColor;
uniform float dayLight;

// Lighting tuning
uniform float skyMissWeight;        // Weight for sky/miss samples (e.g., 0.5)
uniform vec3 indirectTint;          // Tint applied to indirect bounce

// ═══════════════════════════════════════════════════════════════════════════
// Inputs / Outputs
// ═══════════════════════════════════════════════════════════════════════════

in vec2 vTexCoord;

layout(location = 0) out vec4 outRadiance0;  // SH packed texture 0
layout(location = 1) out vec4 outRadiance1;  // SH packed texture 1

// ═══════════════════════════════════════════════════════════════════════════
// Helper Functions
// ═══════════════════════════════════════════════════════════════════════════

float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

vec2 ProjectToScreen(vec3 posVS) {
    vec4 clipPos = projection * vec4(posVS, 1.0);
    vec3 ndc = clipPos.xyz / clipPos.w;
    return ndc.xy * 0.5 + 0.5;
}

// Hammersley sequence
vec2 Hammersley(uint i, uint N) {
    uint bits = i;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    float radicalInverse = float(bits) * 2.3283064365386963e-10;
    return vec2(float(i) / float(N), radicalInverse);
}

// Cosine-weighted hemisphere sample
vec3 CosineSampleHemisphere(vec2 xi) {
    float phi = 2.0 * 3.14159265 * xi.x;
    float cosTheta = sqrt(1.0 - xi.y);
    float sinTheta = sqrt(xi.y);
    return vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}

// Orient sample around normal
vec3 OrientHemisphere(vec3 localDir, vec3 normal) {
    vec3 up = abs(normal.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, normal));
    vec3 bitangent = cross(normal, tangent);
    return tangent * localDir.x + bitangent * localDir.y + normal * localDir.z;
}

// Sky fallback
vec3 SampleSky(vec3 dir) {
    float upness = dir.y * 0.5 + 0.5;
    vec3 skyColor = mix(skyColorHorizon, skyColorZenith, upness);

    float sunDot = max(dot(dir, sunDirection), 0.0);
    float sunFactor = pow(sunDot, 32.0);
    vec3 sunContrib = sunColor * sunFactor * dayLight;

    return skyColor + sunContrib;
}

// Distance falloff
float Falloff(float dist) {
    return 1.0 / (1.0 + dist * dist);
}

// ═══════════════════════════════════════════════════════════════════════════
// Ray March
// ═══════════════════════════════════════════════════════════════════════════

struct RayResult {
    bool hit;
    vec3 radiance;
    float distance;
};

RayResult MarchRay(vec3 originVS, vec3 dirVS) {
    RayResult result;
    result.hit = false;
    result.radiance = vec3(0.0);
    result.distance = rayMaxDistance;

    float stepSize = rayMaxDistance / float(raySteps);
    vec3 rayPos = originVS;

    // Small offset to avoid self-intersection
    rayPos += dirVS * 0.1;

    for (int i = 0; i < raySteps; i++) {
        rayPos += dirVS * stepSize;

        vec2 screenUV = ProjectToScreen(rayPos);

        // Out of bounds check
        if (screenUV.x < 0.001 || screenUV.x > 0.999 ||
            screenUV.y < 0.001 || screenUV.y > 0.999) {
            break;
        }

        float depthSample = texture(gDepth, screenUV).r;

        // Sky check
        if (depthSample > 0.9999) {
            continue;  // Ray in sky region, keep going
        }

        float sceneDepth = LinearizeDepth(depthSample);
        float rayDepth = -rayPos.z;

        // Behind surface check
        if (rayDepth > sceneDepth && rayDepth < sceneDepth + rayThickness) {
            result.hit = true;
            result.radiance = texture(sceneTex, screenUV).rgb;
            result.distance = float(i + 1) * stepSize;
            return result;
        }
    }

    return result;
}

// ═══════════════════════════════════════════════════════════════════════════
// Main
// ═══════════════════════════════════════════════════════════════════════════

void main() {
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);

    // Load probe anchor data
    vec4 anchorPos = texelFetch(probeAnchorPos, probeCoord, 0);
    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);

    vec3 posVS = anchorPos.xyz;
    float valid = anchorPos.a;
    vec3 normalVS = normalize(anchorNormal.xyz * 2.0 - 1.0);

    // Invalid probe: output zero
    if (valid < 0.5) {
        outRadiance0 = vec4(0.0);
        outRadiance1 = vec4(0.0);
        return;
    }

    // Accumulate SH
    vec4 shR = vec4(0.0);
    vec4 shG = vec4(0.0);
    vec4 shB = vec4(0.0);

    float totalWeight = 0.0;

    uint rayCount = uint(raysPerProbe);
    uvec2 probeCoordU = uvec2(probeCoord);

    for (uint i = 0u; i < rayCount; i++) {
        // Generate ray direction
        vec2 xi = Hammersley(i, rayCount);

        // Add temporal jitter
        uint seed = probeCoordU.x * 1973u + probeCoordU.y * 9277u + frameIndex * 26699u + i * 12345u;
        float jitter = fract(float(seed) * 0.00000001);
        xi.x = fract(xi.x + jitter);

        vec3 localDir = CosineSampleHemisphere(xi);
        vec3 rayDir = OrientHemisphere(localDir, normalVS);

        // March ray
        RayResult hit = MarchRay(posVS, rayDir);

        vec3 radiance;
        float weight;

        if (hit.hit) {
            // Hit: use scene radiance with distance falloff and tint
            radiance = hit.radiance * indirectTint * Falloff(hit.distance);
            weight = 1.0;
        } else {
            // Miss: use sky with configurable weight
            radiance = SampleSky(rayDir);
            weight = skyMissWeight;  // From config (default 0.5)
        }

        // Cosine weight (already in hemisphere sampling, but reinforce)
        float cosTheta = max(dot(normalVS, rayDir), 0.0);
        weight *= cosTheta;

        // Project to SH and accumulate
        vec4 sampleR, sampleG, sampleB;
        SHProject(rayDir, radiance * weight, sampleR, sampleG, sampleB);

        shR += sampleR;
        shG += sampleG;
        shB += sampleB;

        totalWeight += weight;
    }

    // Normalize by weight
    if (totalWeight > 0.0) {
        float invWeight = 1.0 / totalWeight;
        shR *= invWeight;
        shG *= invWeight;
        shB *= invWeight;
    }

    // Clamp to prevent negative reconstruction
    SHClampNegative(shR, shG, shB);

    // Pack to output textures
    // Layout: tex0 = DC terms + R.y, tex1 = remaining directional
    outRadiance0 = vec4(shR.x, shG.x, shB.x, shR.y);
    outRadiance1 = vec4(shG.y, shB.y,
                        (shR.z + shG.z + shB.z) / 3.0,  // Avg Z
                        (shR.w + shG.w + shB.w) / 3.0); // Avg X (luminance approx)
}
```

### 5.2 Vertex Shader (Same as Probe Anchor)

```glsl
// lumon_probe_trace.vsh
#version 330 core

layout(location = 0) in vec2 inPosition;

out vec2 vTexCoord;

void main() {
    vTexCoord = inPosition * 0.5 + 0.5;
    gl_Position = vec4(inPosition, 0.0, 1.0);
}
```

---

## 6. C# Integration

### 6.1 Render Pass

```csharp
private void RenderProbeTracePass(IRenderAPI render)
{
    // Bind radiance output framebuffer (probe resolution)
    render.FrameBuffer = bufferManager.ProbeRadianceWrite;

    GL.Viewport(0, 0, bufferManager.ProbeCountX, bufferManager.ProbeCountY);
    GL.Clear(ClearBufferMask.ColorBufferBit);

    probeTraceShader.Use();

    // Bind probe anchor textures
    probeTraceShader.BindTexture2D("probeAnchorPos",
        bufferManager.ProbeAnchorFB.ColorTextureIds[0], 0);
    probeTraceShader.BindTexture2D("probeAnchorNormal",
        bufferManager.ProbeAnchorFB.ColorTextureIds[1], 1);

    // Bind scene textures
    probeTraceShader.BindTexture2D("gDepth",
        gBufferManager.DepthTextureId, 2);
    probeTraceShader.BindTexture2D("sceneTex",
        ssgiBufferManager.CapturedSceneTextureId, 3);  // Reuse SSGI capture

    // Set uniforms
    probeTraceShader.UniformMatrix("projection", render.CurrentProjectionMatrix);
    probeTraceShader.Uniform("screenSize",
        new Vec2f(render.FrameWidth, render.FrameHeight));
    probeTraceShader.Uniform("probeGridSize",
        new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY));

    probeTraceShader.Uniform("raysPerProbe", config.RaysPerProbePerFrame);
    probeTraceShader.Uniform("raySteps", config.RaySteps);
    probeTraceShader.Uniform("rayMaxDistance", config.RayMaxDistance);
    probeTraceShader.Uniform("rayThickness", config.RayThickness);

    probeTraceShader.Uniform("frameIndex", (uint)frameIndex);

    probeTraceShader.Uniform("zNear", render.ShaderUniforms.ZNear);
    probeTraceShader.Uniform("zFar", render.ShaderUniforms.ZFar);

    // Sky/lighting uniforms
    probeTraceShader.Uniform("skyColorZenith", GetSkyZenithColor());
    probeTraceShader.Uniform("skyColorHorizon", GetSkyHorizonColor());
    probeTraceShader.Uniform("sunDirection",
        render.ShaderUniforms.LightPosition3D.Normalized());
    probeTraceShader.Uniform("sunColor", GetSunColor());
    probeTraceShader.Uniform("dayLight", render.ShaderUniforms.SkyDaylight);

    // Lighting tuning (from config)
    probeTraceShader.Uniform("skyMissWeight", config.SkyMissWeight);
    probeTraceShader.Uniform("indirectTint",
        new Vec3f(config.IndirectTint[0], config.IndirectTint[1], config.IndirectTint[2]));

    RenderFullscreenQuad(render);

    probeTraceShader.Stop();

    GL.Viewport(0, 0, render.FrameWidth, render.FrameHeight);
}
```

---

## 7. Performance Budget

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

## 8. Debug Visualization

### 8.1 Debug Mode: Raw Radiance (DebugMode = 2)

```glsl
// Show probe radiance as colored dots
if (debugMode == 2) {
    ivec2 probeCoord = ivec2(gl_FragCoord.xy) / probeSpacing;
    ivec2 probeCenter = probeCoord * probeSpacing + probeSpacing / 2;

    float dist = length(vec2(gl_FragCoord.xy) - vec2(probeCenter));

    if (dist < 3.0) {
        vec4 sh0 = texelFetch(probeRadiance0, probeCoord, 0);
        vec3 ambient = sh0.rgb;  // DC term = average radiance
        outColor = vec4(ambient, 1.0);
    }
}
```

---

## 9. Next Steps

| Document                                     | Dependency    | Topic                                |
| -------------------------------------------- | ------------- | ------------------------------------ |
| [LumOn.05-Temporal.md](LumOn.05-Temporal.md) | This document | Temporal accumulation & reprojection |
