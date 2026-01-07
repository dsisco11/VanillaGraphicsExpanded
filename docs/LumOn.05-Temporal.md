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

```csharp
// In LumOnRenderer.cs

using System.Numerics;

private Matrix4x4 prevViewMatrix;
private Matrix4x4 prevProjMatrix;
private Matrix4x4 prevViewProjMatrix;

private void StoreViewProjMatrix(IRenderAPI render)
{
    // Store current matrices for next frame
    prevViewMatrix = ToMatrix4x4(render.CurrentModelviewMatrix);
    prevProjMatrix = ToMatrix4x4(render.CurrentProjectionMatrix);

    // Compute combined ViewProj (System.Numerics handles multiplication)
    prevViewProjMatrix = prevViewMatrix * prevProjMatrix;
}

/// <summary>
/// Convert VS float[16] column-major array to System.Numerics.Matrix4x4.
/// </summary>
private static Matrix4x4 ToMatrix4x4(float[] m)
{
    return new Matrix4x4(
        m[0],  m[1],  m[2],  m[3],
        m[4],  m[5],  m[6],  m[7],
        m[8],  m[9],  m[10], m[11],
        m[12], m[13], m[14], m[15]
    );
}

/// <summary>
/// Convert System.Numerics.Matrix4x4 to float[16] for shader uniform.
/// </summary>
private static float[] ToFloatArray(Matrix4x4 m)
{
    return new float[]
    {
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44
    };
}
```

### 2.2 First Frame Handling

```csharp
private bool isFirstFrame = true;

public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
{
    // ...

    if (isFirstFrame)
    {
        // No history available, skip temporal pass
        // Or: copy current to history directly
        bufferManager.ClearHistory();
        StoreViewProjMatrix(render);
        isFirstFrame = false;
        return;
    }

    // Normal render with temporal
    // ...
}
```

---

## 3. Reprojection

### 3.1 World-Space Approach

Probes store view-space positions. To reproject:

1. Convert probe posVS to world-space using inverse current view matrix
2. Transform world-space to previous clip-space using prevViewProj
3. Sample history at previous screen UV

```glsl
// In lumon_temporal.fsh

uniform mat4 invViewMatrix;      // Current frame inverse view
uniform mat4 prevViewProjMatrix; // Previous frame view-projection

// Convert current view-space position to previous screen UV
vec2 ReprojectToHistory(vec3 posVS) {
    // View-space to world-space
    vec4 posWS = invViewMatrix * vec4(posVS, 1.0);

    // World-space to previous clip-space
    vec4 prevClip = prevViewProjMatrix * posWS;

    // Clip to NDC
    vec3 prevNDC = prevClip.xyz / prevClip.w;

    // NDC to UV
    return prevNDC.xy * 0.5 + 0.5;
}
```

### 3.2 Handling Camera Translation

Camera translation causes parallax—objects at different depths move differently on screen. This is automatically handled by world-space reprojection.

### 3.3 Handling Camera Rotation

Pure rotation doesn't change world positions, but screen positions change drastically. Reprojection handles this correctly, but may cause many probes to fall outside previous frame bounds.

---

## 4. History Validation

### 4.1 Depth Rejection

Reject history if the reprojected position's depth differs significantly from the stored history depth.

```glsl
uniform sampler2D historyDepth;  // Depth at history probe positions
uniform float depthRejectThreshold;

bool ValidateDepth(vec2 historyUV, float currentDepth) {
    float historyDepth = texture(historyDepth, historyUV).r;

    // Compare linearized depths
    float currentLin = LinearizeDepth(currentDepth);
    float historyLin = LinearizeDepth(historyDepth);

    float relDiff = abs(currentLin - historyLin) / max(currentLin, 0.001);

    return relDiff < depthRejectThreshold;
}
```

### 4.2 Normal Rejection

Reject if surface normal changed significantly (e.g., camera panned to different surface).

```glsl
uniform sampler2D historyNormal;
uniform float normalRejectThreshold;

bool ValidateNormal(vec2 historyUV, vec3 currentNormal) {
    vec3 historyNormal = texture(historyNormal, historyUV).xyz * 2.0 - 1.0;

    float dotProduct = dot(currentNormal, historyNormal);

    return dotProduct > normalRejectThreshold;  // e.g., > 0.8
}
```

### 4.3 Bounds Rejection

Reject if reprojected UV is outside screen bounds.

```glsl
bool ValidateBounds(vec2 historyUV) {
    return historyUV.x >= 0.0 && historyUV.x <= 1.0 &&
           historyUV.y >= 0.0 && historyUV.y <= 1.0;
}
```

### 4.4 Combined Validation

```glsl
struct ValidationResult {
    bool valid;
    float confidence;  // 0-1, how much to trust history
};

ValidationResult ValidateHistory(vec2 historyUV, float currentDepth, vec3 currentNormal) {
    ValidationResult result;
    result.valid = false;
    result.confidence = 0.0;

    // Bounds check
    if (!ValidateBounds(historyUV)) {
        return result;
    }

    // Depth check
    if (!ValidateDepth(historyUV, currentDepth)) {
        return result;
    }

    // Normal check
    if (!ValidateNormal(historyUV, currentNormal)) {
        return result;
    }

    result.valid = true;

    // Confidence based on how close to thresholds
    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    result.confidence = min(depthConf, normalConf);

    return result;
}
```

---

## 5. Neighborhood Clamping

Even with validation, history can drift (accumulate error). Clamping keeps history within the range of current frame's local neighborhood.

### 5.1 Min/Max Clamping

```glsl
// Sample 3x3 neighborhood of current frame
void GetNeighborhoodMinMax(ivec2 probeCoord, sampler2D currentRadiance,
                           out vec4 minVal, out vec4 maxVal) {
    minVal = vec4(1e10);
    maxVal = vec4(-1e10);

    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            ivec2 neighbor = probeCoord + ivec2(dx, dy);

            // Clamp to grid bounds
            neighbor = clamp(neighbor, ivec2(0), probeGridSize - 1);

            vec4 sample = texelFetch(currentRadiance, neighbor, 0);

            minVal = min(minVal, sample);
            maxVal = max(maxVal, sample);
        }
    }
}

vec4 ClampToNeighborhood(vec4 history, vec4 minVal, vec4 maxVal) {
    return clamp(history, minVal, maxVal);
}
```

### 5.2 Variance Clamping (More Robust)

```glsl
// Compute mean and standard deviation of neighborhood
void GetNeighborhoodStats(ivec2 probeCoord, sampler2D currentRadiance,
                          out vec4 mean, out vec4 stdDev) {
    vec4 sum = vec4(0.0);
    vec4 sumSq = vec4(0.0);
    float count = 0.0;

    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            ivec2 neighbor = probeCoord + ivec2(dx, dy);
            neighbor = clamp(neighbor, ivec2(0), probeGridSize - 1);

            vec4 sample = texelFetch(currentRadiance, neighbor, 0);

            sum += sample;
            sumSq += sample * sample;
            count += 1.0;
        }
    }

    mean = sum / count;
    vec4 variance = sumSq / count - mean * mean;
    stdDev = sqrt(max(variance, vec4(0.0)));
}

vec4 VarianceClamp(vec4 history, vec4 mean, vec4 stdDev, float gamma) {
    // gamma = 1.0 for tight clamping, 2.0+ for looser
    vec4 minVal = mean - stdDev * gamma;
    vec4 maxVal = mean + stdDev * gamma;
    return clamp(history, minVal, maxVal);
}
```

---

## 6. Temporal Blend

### 6.1 Basic Exponential Moving Average

```glsl
uniform float temporalAlpha;  // e.g., 0.95

vec4 BlendTemporal(vec4 current, vec4 history, float alpha) {
    // alpha = 0.95 means 95% history, 5% current
    return mix(current, history, alpha);
}
```

### 6.2 Adaptive Blend Based on Validation

```glsl
vec4 AdaptiveBlend(vec4 current, vec4 history,
                   ValidationResult validation, float baseAlpha) {
    if (!validation.valid) {
        // No valid history, use current
        return current;
    }

    // Reduce alpha (use more current) when confidence is low
    float adaptedAlpha = baseAlpha * validation.confidence;

    // Also reduce alpha for edge probes (more unstable)
    // float valid = probeAnchor.a;
    // if (valid < 0.9) adaptedAlpha *= 0.5;

    return mix(current, history, adaptedAlpha);
}
```

### 6.3 Handling Disocclusion

When validation fails, we could:

1. **Hard reset**: Use only current frame (noisy but correct)
2. **Soft reset**: Use lower alpha for a few frames
3. **Spatial fill**: Sample nearby valid probes

```glsl
// Track accumulation count per probe (in alpha channel of history metadata)
float GetAccumulationCount(vec2 historyUV) {
    return texture(historyMeta, historyUV).a;
}

vec4 DisocclusionAwareBlend(vec4 current, vec4 history,
                            ValidationResult validation,
                            float baseAlpha, float accumCount) {
    if (!validation.valid) {
        // Disoccluded: reset
        return current;
    }

    // Ramp up alpha as we accumulate more frames
    // First few frames use more current data
    float rampedAlpha = baseAlpha * min(accumCount / 10.0, 1.0);

    return mix(current, history, rampedAlpha);
}
```

---

## 7. Full Temporal Shader

### 7.1 lumon_temporal.fsh

```glsl
// lumon_temporal.fsh
#version 330 core

// ═══════════════════════════════════════════════════════════════════════════
// Uniforms
// ═══════════════════════════════════════════════════════════════════════════

// Current frame data
uniform sampler2D currentRadiance0;   // SH texture 0
uniform sampler2D currentRadiance1;   // SH texture 1
uniform sampler2D probeAnchorPos;     // posVS.xyz, valid
uniform sampler2D probeAnchorNormal;  // normalVS.xyz

// History data
uniform sampler2D historyRadiance0;
uniform sampler2D historyRadiance1;
uniform sampler2D historyDepth;       // Depth at history positions
uniform sampler2D historyNormal;      // Normal at history positions

// Matrices
uniform mat4 invViewMatrix;
uniform mat4 prevViewProjMatrix;

// Config
uniform vec2 probeGridSize;
uniform float temporalAlpha;
uniform float depthRejectThreshold;
uniform float normalRejectThreshold;
uniform float zNear;
uniform float zFar;

// ═══════════════════════════════════════════════════════════════════════════
// Inputs / Outputs
// ═══════════════════════════════════════════════════════════════════════════

in vec2 vTexCoord;

layout(location = 0) out vec4 outRadiance0;
layout(location = 1) out vec4 outRadiance1;
layout(location = 2) out vec4 outMeta;  // Depth, normal.xy, accumCount

// ═══════════════════════════════════════════════════════════════════════════
// Helper Functions
// ═══════════════════════════════════════════════════════════════════════════

float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

vec2 ReprojectToHistory(vec3 posVS) {
    vec4 posWS = invViewMatrix * vec4(posVS, 1.0);
    vec4 prevClip = prevViewProjMatrix * posWS;
    vec3 prevNDC = prevClip.xyz / prevClip.w;
    return prevNDC.xy * 0.5 + 0.5;
}

// ═══════════════════════════════════════════════════════════════════════════
// Validation
// ═══════════════════════════════════════════════════════════════════════════

struct ValidationResult {
    bool valid;
    float confidence;
};

ValidationResult ValidateHistory(vec2 historyUV, float currentDepthLin, vec3 currentNormal) {
    ValidationResult result;
    result.valid = false;
    result.confidence = 0.0;

    // Bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return result;
    }

    // Depth
    vec4 histMeta = texture(historyDepth, historyUV);
    float historyDepthLin = histMeta.r;

    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    if (depthDiff > depthRejectThreshold) {
        return result;
    }

    // Normal
    vec3 historyNormal = texture(historyNormal, historyUV).xyz * 2.0 - 1.0;
    float normalDot = dot(normalize(currentNormal), normalize(historyNormal));
    if (normalDot < normalRejectThreshold) {
        return result;
    }

    result.valid = true;

    // Confidence
    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    result.confidence = clamp(min(depthConf, normalConf), 0.0, 1.0);

    return result;
}

// ═══════════════════════════════════════════════════════════════════════════
// Neighborhood Clamping
// ═══════════════════════════════════════════════════════════════════════════

void GetNeighborhoodMinMax(ivec2 probeCoord,
                           out vec4 minVal0, out vec4 maxVal0,
                           out vec4 minVal1, out vec4 maxVal1) {
    minVal0 = vec4(1e10);
    maxVal0 = vec4(-1e10);
    minVal1 = vec4(1e10);
    maxVal1 = vec4(-1e10);

    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            ivec2 neighbor = clamp(probeCoord + ivec2(dx, dy),
                                   ivec2(0), probeGridSize - 1);

            vec4 s0 = texelFetch(currentRadiance0, neighbor, 0);
            vec4 s1 = texelFetch(currentRadiance1, neighbor, 0);

            minVal0 = min(minVal0, s0);
            maxVal0 = max(maxVal0, s0);
            minVal1 = min(minVal1, s1);
            maxVal1 = max(maxVal1, s1);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Main
// ═══════════════════════════════════════════════════════════════════════════

void main() {
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);

    // Load current frame data
    vec4 currentRad0 = texelFetch(currentRadiance0, probeCoord, 0);
    vec4 currentRad1 = texelFetch(currentRadiance1, probeCoord, 0);

    vec4 anchorPos = texelFetch(probeAnchorPos, probeCoord, 0);
    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);

    vec3 posVS = anchorPos.xyz;
    float valid = anchorPos.a;
    vec3 normalVS = normalize(anchorNormal.xyz * 2.0 - 1.0);

    // Invalid probe: pass through
    if (valid < 0.5) {
        outRadiance0 = currentRad0;
        outRadiance1 = currentRad1;
        outMeta = vec4(0.0);
        return;
    }

    // Compute linearized depth for validation
    float currentDepthLin = -posVS.z;  // View-space Z (positive into screen)

    // Reproject to history
    vec2 historyUV = ReprojectToHistory(posVS);

    // Validate history
    ValidationResult validation = ValidateHistory(historyUV, currentDepthLin, normalVS);

    vec4 outputRad0;
    vec4 outputRad1;
    float accumCount = 1.0;

    if (validation.valid) {
        // Sample history
        vec4 historyRad0 = texture(historyRadiance0, historyUV);
        vec4 historyRad1 = texture(historyRadiance1, historyUV);

        // Get neighborhood bounds for clamping
        vec4 minVal0, maxVal0, minVal1, maxVal1;
        GetNeighborhoodMinMax(probeCoord, minVal0, maxVal0, minVal1, maxVal1);

        // Clamp history to neighborhood
        historyRad0 = clamp(historyRad0, minVal0, maxVal0);
        historyRad1 = clamp(historyRad1, minVal1, maxVal1);

        // Adaptive blend
        float alpha = temporalAlpha * validation.confidence;

        // Edge probes get less temporal accumulation
        if (valid < 0.9) {
            alpha *= 0.5;
        }

        outputRad0 = mix(currentRad0, historyRad0, alpha);
        outputRad1 = mix(currentRad1, historyRad1, alpha);

        // Track accumulation (from history meta)
        float prevAccum = texture(historyDepth, historyUV).a;
        accumCount = min(prevAccum + 1.0, 100.0);  // Cap at 100 frames
    } else {
        // Disoccluded: use current only
        outputRad0 = currentRad0;
        outputRad1 = currentRad1;
        accumCount = 1.0;
    }

    outRadiance0 = outputRad0;
    outRadiance1 = outputRad1;

    // Store metadata for next frame
    // r = linearized depth, gba = normal.xyz encoded, a = accumCount
    outMeta = vec4(currentDepthLin, normalVS * 0.5 + 0.5);
    outMeta.a = accumCount;
}
```

---

## 8. C# Integration

### 8.1 Additional Buffer for Metadata

```csharp
// In LumOnBufferManager.cs

public FrameBufferRef ProbeMetaCurrentFB { get; private set; }
public FrameBufferRef ProbeMetaHistoryFB { get; private set; }

private void CreateMetaBuffers()
{
    // Stores: linearized depth, encoded normal, accumulation count
    ProbeMetaCurrentFB = CreateFramebuffer(
        "LumOn_MetaCurrent",
        ProbeCountX, ProbeCountY,
        new[] { EnumTextureFormat.Rgba16f }
    );

    ProbeMetaHistoryFB = CreateFramebuffer(
        "LumOn_MetaHistory",
        ProbeCountX, ProbeCountY,
        new[] { EnumTextureFormat.Rgba16f }
    );
}

public void SwapAllBuffers()
{
    SwapRadianceBuffers();

    // Also swap meta buffers
    var temp = ProbeMetaCurrentFB;
    ProbeMetaCurrentFB = ProbeMetaHistoryFB;
    ProbeMetaHistoryFB = temp;
}
```

### 8.2 Render Pass

```csharp
private void RenderTemporalPass(IRenderAPI render)
{
    // Output goes to history buffer (will become current next frame after swap)
    render.FrameBuffer = bufferManager.ProbeRadianceWrite;
    // Also need to output to meta buffer - use MRT

    GL.Viewport(0, 0, bufferManager.ProbeCountX, bufferManager.ProbeCountY);

    temporalShader.Use();

    // Current frame textures
    temporalShader.BindTexture2D("currentRadiance0",
        bufferManager.ProbeRadianceRead.ColorTextureIds[0], 0);
    temporalShader.BindTexture2D("currentRadiance1",
        bufferManager.ProbeRadianceRead.ColorTextureIds[1], 1);
    temporalShader.BindTexture2D("probeAnchorPos",
        bufferManager.ProbeAnchorFB.ColorTextureIds[0], 2);
    temporalShader.BindTexture2D("probeAnchorNormal",
        bufferManager.ProbeAnchorFB.ColorTextureIds[1], 3);

    // History textures (from previous frame)
    temporalShader.BindTexture2D("historyRadiance0",
        bufferManager.ProbeRadianceWrite.ColorTextureIds[0], 4);  // Previous write = current read
    temporalShader.BindTexture2D("historyRadiance1",
        bufferManager.ProbeRadianceWrite.ColorTextureIds[1], 5);
    temporalShader.BindTexture2D("historyDepth",
        bufferManager.ProbeMetaHistoryFB.ColorTextureIds[0], 6);

    // Matrices
    temporalShader.UniformMatrix("invViewMatrix", GetInverseViewMatrix(render));
    temporalShader.UniformMatrix("prevViewProjMatrix", prevViewProjMatrix);

    // Config
    temporalShader.Uniform("probeGridSize",
        new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY));
    temporalShader.Uniform("temporalAlpha", config.TemporalAlpha);
    temporalShader.Uniform("depthRejectThreshold", config.DepthRejectThreshold);
    temporalShader.Uniform("normalRejectThreshold", config.NormalRejectThreshold);
    temporalShader.Uniform("zNear", render.ShaderUniforms.ZNear);
    temporalShader.Uniform("zFar", render.ShaderUniforms.ZFar);
    temporalShader.Uniform("debugMode", config.DebugMode);

    RenderFullscreenQuad(render);

    temporalShader.Stop();

    GL.Viewport(0, 0, render.FrameWidth, render.FrameHeight);
}

private float[] GetInverseViewMatrix(IRenderAPI render)
{
    // Invert current modelview matrix using System.Numerics
    var view = ToMatrix4x4(render.CurrentModelviewMatrix);
    Matrix4x4.Invert(view, out var invView);
    return ToFloatArray(invView);
}
```

---

## 9. Teleport/Scene Change Detection

### 9.1 Detecting Large Camera Jumps

```csharp
private Vec3d lastCameraPos;
private float teleportThreshold = 50.0f;  // meters

private bool DetectTeleport(IRenderAPI render)
{
    Vec3d currentPos = new Vec3d(
        render.CameraMatrixOrigin[0],
        render.CameraMatrixOrigin[1],
        render.CameraMatrixOrigin[2]
    );

    double distance = currentPos.DistanceTo(lastCameraPos);
    lastCameraPos = currentPos;

    return distance > teleportThreshold;
}

public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
{
    // ...

    if (DetectTeleport(render))
    {
        bufferManager.ClearHistory();
        api.Logger.Debug("[LumOn] Teleport detected, cleared history");
    }

    // ...
}
```

### 9.2 Scene Change Events

```csharp
// Register for world unload/load events
api.Event.LeaveWorld += () => {
    isFirstFrame = true;
    bufferManager?.ClearHistory();
};
```

---

## 10. Debug Visualization

Debug visualizations for the temporal system are implemented in `lumon_debug.fsh`, not in the temporal shader itself.

### 10.1 Debug Mode: Temporal Weight (DebugMode = 6)

Shows how much history is being used per probe. Brighter = more history weight.

```glsl
// In lumon_debug.fsh
float weight = validation.valid ? validation.confidence * temporalAlpha : 0.0;
outColor = vec4(weight, weight, weight, 1.0);  // Grayscale
```

### 10.2 Debug Mode: Rejection Mask (DebugMode = 7)

Shows why history was rejected at each probe:

| Color  | Meaning                                |
| ------ | -------------------------------------- |
| Green  | Valid history                          |
| Red    | Out of bounds (reprojected off-screen) |
| Yellow | Depth rejection                        |
| Orange | Normal rejection                       |
| Purple | No history data available              |

```glsl
// In lumon_debug.fsh
vec3 color;
if (!validation.valid) {
    if (outOfBounds) {
        color = vec3(1.0, 0.0, 0.0);  // Red
    } else if (depthRejected) {
        color = vec3(1.0, 1.0, 0.0);  // Yellow
    } else {
        color = vec3(1.0, 0.5, 0.0);  // Orange (normal reject)
    }
} else {
    color = vec3(0.0, 1.0, 0.0);  // Green
}
```

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

---

## 13. Implementation Checklist

### 13.1 Shader Files

- [x] Create `lumon_temporal.vsh`
- [x] Create `lumon_temporal.fsh`
- [x] Implement reprojection function
- [x] Implement history validation (depth/normal/bounds)
- [x] Implement neighborhood clamping (min/max)
- [ ] (Optional) Implement variance clamping alternative
- [x] Implement adaptive blend based on confidence

### 13.2 Shader Program Class

- [x] Create `LumOnTemporalShaderProgram.cs`
- [x] Add current frame texture properties
- [x] Add history texture properties
- [x] Add matrix uniform properties
- [x] Add config uniform properties

### 13.3 Matrix Management

- [x] Store `prevViewMatrix` and `prevProjMatrix`
- [x] Compute `prevViewProjMatrix` using System.Numerics
- [x] Implement `ToMatrix4x4()` / `ToFloatArray()` helpers
- [x] Implement `GetInverseViewMatrix()`

### 13.4 Buffer Management

- [x] Create ProbeMetaCurrent/History framebuffers
- [x] Store accumulation count per probe in meta alpha
- [x] Update `SwapAllBuffers()` to include meta
- [x] Handle first frame (no history)
- [x] Implement `isFirstFrame` flag handling

### 13.5 Special Cases

- [x] Implement teleport detection
- [x] Register for world unload/load events
- [x] Clear history on scene change

### 13.6 Testing

- [ ] Verify temporal weight visualization (DebugMode = 6)
- [ ] Check rejection mask colors (DebugMode = 7)
- [ ] Test camera rotation stability
- [ ] Test camera translation (parallax)
- [ ] Verify disocclusion handling
- [ ] Test accumulation count ramping
- [ ] Verify neighborhood clamping reduces ghosting
