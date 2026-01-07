# LumOn Probe Grid System

> **Document**: LumOn.02-Probe-Grid.md  
> **Status**: Draft  
> **Dependencies**: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md)  
> **Implements**: SPG-001, SPG-002

---

## 1. Overview

The probe grid is a sparse 2D array of sample points placed over the screen. Each probe:

1. **Anchors** to a surface visible in the G-Buffer (position + normal)
2. **Validates** whether it covers meaningful geometry (not sky, not edge)
3. **Serves** as the center for ray tracing and radiance accumulation

### 1.1 Why Screen-Space Probes?

| Approach                | Pros                             | Cons                          |
| ----------------------- | -------------------------------- | ----------------------------- |
| Per-pixel rays          | Highest quality                  | O(pixels × rays) cost         |
| World-space probes      | View-independent                 | Complex placement, streaming  |
| **Screen-space probes** | Adapts to view, O(probes × rays) | Temporal instability at edges |

LumOn uses screen-space probes because:

- Probes are automatically placed where geometry is visible
- Cost scales with screen coverage, not scene complexity
- Natural integration with G-Buffer data

---

## 2. Grid Dimensions (SPG-001)

### 2.1 Calculation

```csharp
// In LumOnBufferManager.CreateBuffers()
int screenW = platform.FrameWidth;   // e.g., 1920
int screenH = platform.FrameHeight;  // e.g., 1080
int spacing = config.ProbeSpacingPx; // e.g., 8

ProbeCountX = (int)Math.Ceiling((float)screenW / spacing);  // 1920/8 = 240
ProbeCountY = (int)Math.Ceiling((float)screenH / spacing);  // 1080/8 = 135

// Total probes: 240 × 135 = 32,400 (vs 2,073,600 pixels at 1080p)
```

### 2.2 Spacing Trade-offs

| Spacing  | Probe Count (1080p) | Quality      | Cost       |
| -------- | ------------------- | ------------ | ---------- |
| 4 px     | 480 × 270 = 129,600 | High         | High       |
| **8 px** | 240 × 135 = 32,400  | **Balanced** | **Medium** |
| 16 px    | 120 × 68 = 8,160    | Low          | Low        |

**Recommendation**: Start with 8px spacing. Users can adjust via `ProbeSpacingPx` config.

### 2.3 Screen-to-Probe Mapping

```glsl
// Convert screen pixel to probe grid coordinate
ivec2 screenToProbeCoord(ivec2 screenPixel, int probeSpacing) {
    return screenPixel / probeSpacing;
}

// Convert probe grid coordinate to screen pixel (probe center)
ivec2 probeToScreenCoord(ivec2 probeCoord, int probeSpacing) {
    return probeCoord * probeSpacing + probeSpacing / 2;
}

// Get the 4 probes surrounding a pixel for bilinear interpolation
void getEnclosingProbes(vec2 screenUV, int probeSpacing, ivec2 screenSize,
                        out ivec2 probe00, out ivec2 probe10,
                        out ivec2 probe01, out ivec2 probe11,
                        out vec2 weights) {
    vec2 probeUV = screenUV * vec2(screenSize) / float(probeSpacing) - 0.5;
    ivec2 baseProbe = ivec2(floor(probeUV));
    weights = fract(probeUV);

    probe00 = baseProbe;
    probe10 = baseProbe + ivec2(1, 0);
    probe01 = baseProbe + ivec2(0, 1);
    probe11 = baseProbe + ivec2(1, 1);
}
```

---

## 3. ProbeAnchor Texture Layout

### 3.1 Texture Format

Two RGBA16F textures store probe anchor data:

| Attachment         | Channel | Content    | Range                                      |
| ------------------ | ------- | ---------- | ------------------------------------------ |
| **ProbeAnchor[0]** | R       | posVS.x    | view-space meters                          |
|                    | G       | posVS.y    | view-space meters                          |
|                    | B       | posVS.z    | view-space meters (negative = into screen) |
|                    | A       | valid      | 0.0 = invalid, 1.0 = valid                 |
| **ProbeAnchor[1]** | R       | normalVS.x | [-1, 1] (view-space, converted from WS)    |
|                    | G       | normalVS.y | [-1, 1] (view-space, converted from WS)    |
|                    | B       | normalVS.z | [-1, 1] (view-space, converted from WS)    |
|                    | A       | reserved   | future: material flags                     |

### 3.2 Why View-Space?

- **Consistent with G-Buffer**: VS's `gPosition` is view-space
- **Simpler math**: Ray marching stays in view-space
- **Reprojection**: World-space conversion only needed for temporal pass

> **Note**: Normals from `gNormal` are stored in **world-space** in Vintage Story's G-buffer.
> The probe anchor pass converts them to **view-space** using the model-view matrix,
> ensuring consistency with the view-space positions for ray marching.

### 3.3 GLSL Output Declaration

```glsl
// lumon_probe_anchor.fsh
layout(location = 0) out vec4 outProbePos;    // posVS.xyz, valid
layout(location = 1) out vec4 outProbeNormal; // normalVS.xyz, reserved
```

---

## 4. Probe Anchor Build Pass (SPG-002)

### 4.1 Pass Overview

The probe anchor pass runs once per frame, before ray tracing:

1. **Input**: G-Buffer depth texture, G-Buffer normal texture
2. **Output**: ProbeAnchor textures (probeW × probeH)
3. **Method**: Fragment shader with probe-resolution viewport

### 4.2 Vertex Shader

```glsl
// lumon_probe_anchor.vsh
#version 330 core

layout(location = 0) in vec2 inPosition;  // Fullscreen quad [-1, 1]

out vec2 vTexCoord;

void main() {
    vTexCoord = inPosition * 0.5 + 0.5;  // [0, 1]
    gl_Position = vec4(inPosition, 0.0, 1.0);
}
```

### 4.3 Fragment Shader

```glsl
// lumon_probe_anchor.fsh
#version 330 core

// ═══════════════════════════════════════════════════════════════════════════
// Uniforms
// ═══════════════════════════════════════════════════════════════════════════

uniform sampler2D gDepth;          // G-Buffer depth
uniform sampler2D gNormal;         // G-Buffer view-space normals (attachment 2)
uniform sampler2D gPosition;       // G-Buffer view-space position (attachment 3)

uniform ivec2 screenSize;          // Full resolution (e.g., 1920x1080)
uniform ivec2 probeGridSize;       // Probe grid size (e.g., 240x135)
uniform int probeSpacing;          // Pixels per probe (e.g., 8)

uniform float zNear;
uniform float zFar;

uniform float depthDiscontinuityThreshold;  // For edge detection (0.1 recommended)

// ═══════════════════════════════════════════════════════════════════════════
// Inputs / Outputs
// ═══════════════════════════════════════════════════════════════════════════

in vec2 vTexCoord;  // Probe-space UV [0, 1]

layout(location = 0) out vec4 outProbePos;
layout(location = 1) out vec4 outProbeNormal;

// ═══════════════════════════════════════════════════════════════════════════
// Helper Functions
// ═══════════════════════════════════════════════════════════════════════════

// Linearize depth from [0,1] to view-space Z
float linearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

// Check if depth represents sky (very far or exactly 1.0)
bool isSky(float depth) {
    return depth >= 0.9999;
}

// Sample G-Buffer at screen-space UV
vec3 samplePosition(vec2 screenUV) {
    return texture(gPosition, screenUV).xyz;
}

vec3 sampleNormal(vec2 screenUV) {
    return normalize(texture(gNormal, screenUV).xyz * 2.0 - 1.0);
}

float sampleDepth(vec2 screenUV) {
    return texture(gDepth, screenUV).r;
}

// ═══════════════════════════════════════════════════════════════════════════
// Validation Logic
// ═══════════════════════════════════════════════════════════════════════════

// Check for depth discontinuity in neighborhood (indicates edge)
bool hasDepthDiscontinuity(vec2 centerUV, float centerDepth) {
    vec2 texelSize = 1.0 / vec2(screenSize);

    // Sample 4 neighbors
    float depthL = sampleDepth(centerUV + vec2(-texelSize.x, 0.0));
    float depthR = sampleDepth(centerUV + vec2( texelSize.x, 0.0));
    float depthU = sampleDepth(centerUV + vec2(0.0,  texelSize.y));
    float depthD = sampleDepth(centerUV + vec2(0.0, -texelSize.y));

    // Linearize for proper comparison
    float linCenter = linearizeDepth(centerDepth);
    float linL = linearizeDepth(depthL);
    float linR = linearizeDepth(depthR);
    float linU = linearizeDepth(depthU);
    float linD = linearizeDepth(depthD);

    // Check for large depth jumps (relative threshold)
    float threshold = linCenter * depthDiscontinuityThreshold;

    return abs(linCenter - linL) > threshold ||
           abs(linCenter - linR) > threshold ||
           abs(linCenter - linU) > threshold ||
           abs(linCenter - linD) > threshold;
}

// ═══════════════════════════════════════════════════════════════════════════
// Main
// ═══════════════════════════════════════════════════════════════════════════

void main() {
    // Current probe index
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);

    // Convert probe coord to screen-space pixel (center of probe cell)
    ivec2 screenPixel = probeCoord * probeSpacing + probeSpacing / 2;

    // Clamp to screen bounds
    screenPixel = clamp(screenPixel, ivec2(0), screenSize - 1);

    // Screen-space UV for G-Buffer sampling
    vec2 screenUV = (vec2(screenPixel) + 0.5) / vec2(screenSize);

    // Sample G-Buffer
    float depth = sampleDepth(screenUV);
    vec3 posVS = samplePosition(screenUV);
    vec3 normalVS = sampleNormal(screenUV);

    // ═══════════════════════════════════════════════════════════════════════
    // Validation
    // ═══════════════════════════════════════════════════════════════════════

    float valid = 1.0;

    // Reject sky pixels (no surface to anchor to)
    if (isSky(depth)) {
        valid = 0.0;
    }

    // Reject edge pixels (unstable for temporal)
    // Note: Can be disabled for more coverage at cost of stability
    if (valid > 0.5 && hasDepthDiscontinuity(screenUV, depth)) {
        valid = 0.5;  // Mark as edge (partial validity)
    }

    // Reject invalid normals
    if (length(normalVS) < 0.5) {
        valid = 0.0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Output
    // ═══════════════════════════════════════════════════════════════════════

    outProbePos = vec4(posVS, valid);
    outProbeNormal = vec4(normalVS * 0.5 + 0.5, 0.0);  // Encode to [0,1]
}
```

### 4.4 Validation Criteria Summary

| Condition           | Result      | Rationale                     |
| ------------------- | ----------- | ----------------------------- |
| Depth ≥ 0.9999      | valid = 0   | Sky has no surface            |
| Depth discontinuity | valid = 0.5 | Edges are temporally unstable |
| Normal length < 0.5 | valid = 0   | Invalid G-Buffer data         |
| Otherwise           | valid = 1   | Good probe                    |

---

## 5. C# Integration

### 5.1 Render Pass Implementation

```csharp
// In LumOnRenderer.cs

private void RenderProbeAnchorPass(IRenderAPI render)
{
    // Bind probe anchor framebuffer (probe resolution)
    render.FrameBuffer = bufferManager.ProbeAnchorFB;

    // Set viewport to probe grid size
    GL.Viewport(0, 0, bufferManager.ProbeCountX, bufferManager.ProbeCountY);

    // Clear (invalid probes = black)
    GL.Clear(ClearBufferMask.ColorBufferBit);

    // Use probe anchor shader
    probeAnchorShader.Use();

    // Bind G-Buffer textures
    probeAnchorShader.BindTexture2D("gDepth",
        gBufferManager.DepthTextureId, 0);
    probeAnchorShader.BindTexture2D("gNormal",
        gBufferManager.NormalTextureId, 1);
    probeAnchorShader.BindTexture2D("gPosition",
        gBufferManager.PositionTextureId, 2);

    // Set uniforms
    probeAnchorShader.Uniform("screenSize",
        new Vec2i(render.FrameWidth, render.FrameHeight));
    probeAnchorShader.Uniform("probeGridSize",
        new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY));
    probeAnchorShader.Uniform("probeSpacing", config.ProbeSpacingPx);
    probeAnchorShader.Uniform("zNear", render.ShaderUniforms.ZNear);
    probeAnchorShader.Uniform("zFar", render.ShaderUniforms.ZFar);
    probeAnchorShader.Uniform("depthDiscontinuityThreshold", 0.1f);

    // Render fullscreen quad
    RenderFullscreenQuad(render);

    probeAnchorShader.Stop();

    // Restore viewport to full resolution
    GL.Viewport(0, 0, render.FrameWidth, render.FrameHeight);
}
```

### 5.2 Shader Program Class

Create a dedicated `ShaderProgram` subclass for the probe anchor shader, following the same pattern as `SSGIShaderProgram`:

```csharp
// LumOn/Shaders/LumOnProbeAnchorShaderProgram.cs

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn probe anchor pass.
/// Samples G-Buffer at probe centers to determine anchor positions and validity.
/// </summary>
public class LumOnProbeAnchorShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeAnchorShaderProgram
        {
            PassName = "lumon_probe_anchor",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_probe_anchor", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// G-Buffer depth texture (texture unit 0).
    /// </summary>
    public int GDepth { set => BindTexture2D("gDepth", value, 0); }

    /// <summary>
    /// G-Buffer view-space normals (texture unit 1).
    /// </summary>
    public int GNormal { set => BindTexture2D("gNormal", value, 1); }

    /// <summary>
    /// G-Buffer view-space positions (texture unit 2).
    /// </summary>
    public int GPosition { set => BindTexture2D("gPosition", value, 2); }

    #endregion

    #region Screen/Grid Uniforms

    /// <summary>
    /// Full screen resolution in pixels.
    /// </summary>
    public Vec2i ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", value); }

    /// <summary>
    /// Pixels per probe cell.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    #endregion

    #region Depth Uniforms

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    /// <summary>
    /// Threshold for depth discontinuity detection (edge rejection).
    /// Recommended: 0.1
    /// </summary>
    public float DepthDiscontinuityThreshold { set => Uniform("depthDiscontinuityThreshold", value); }

    #endregion
}
```

### 5.3 Shader Registration

Register the shader during mod initialization:

```csharp
// In VanillaGraphicsExpandedModSystem.cs

public override void StartClientSide(ICoreClientAPI api)
{
    // ... other initialization ...

    // Register LumOn shaders
    LumOnProbeAnchorShaderProgram.Register(api);
    // LumOnProbeTraceShaderProgram.Register(api);
    // LumOnTemporalShaderProgram.Register(api);
    // LumOnGatherShaderProgram.Register(api);
    // LumOnUpsampleShaderProgram.Register(api);
}
```

### 5.4 Shader Usage in Renderer

```csharp
// In LumOnRenderer.cs

private LumOnProbeAnchorShaderProgram probeAnchorShader;

private void LoadShaders()
{
    // Get the registered shader program
    probeAnchorShader = (LumOnProbeAnchorShaderProgram)api.Shader
        .GetProgramByName("lumon_probe_anchor");
}

private void RenderProbeAnchorPass(IRenderAPI render)
{
    // ... framebuffer and viewport setup ...

    probeAnchorShader.Use();

    // Use typed property setters instead of string-based uniforms
    probeAnchorShader.GDepth = gBufferManager.DepthTextureId;
    probeAnchorShader.GNormal = gBufferManager.NormalTextureId;
    probeAnchorShader.GPosition = gBufferManager.PositionTextureId;

    probeAnchorShader.ScreenSize = new Vec2i(render.FrameWidth, render.FrameHeight);
    probeAnchorShader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
    probeAnchorShader.ProbeSpacing = config.ProbeSpacingPx;
    probeAnchorShader.ZNear = render.ShaderUniforms.ZNear;
    probeAnchorShader.ZFar = render.ShaderUniforms.ZFar;
    probeAnchorShader.DepthDiscontinuityThreshold = 0.1f;

    RenderFullscreenQuad(render);

    probeAnchorShader.Stop();

    // ... restore viewport ...
}
```

---

## 6. Debug Visualization

### 6.1 Debug Mode: Probe Grid (DebugMode = 1)

When `config.DebugMode == 1`, overlay probe positions on screen:

```glsl
// In final composite shader
if (debugMode == 1) {
    // Highlight probe centers
    ivec2 probeCoord = ivec2(gl_FragCoord.xy) / probeSpacing;
    ivec2 probeCenter = probeCoord * probeSpacing + probeSpacing / 2;

    float dist = length(vec2(gl_FragCoord.xy) - vec2(probeCenter));

    if (dist < 2.0) {
        // Sample probe validity
        vec4 probeData = texelFetch(probeAnchorTex0, probeCoord, 0);
        float valid = probeData.a;

        // Color by validity: green = valid, yellow = edge, red = invalid
        if (valid > 0.9) {
            outColor = vec4(0.0, 1.0, 0.0, 1.0);  // Green
        } else if (valid > 0.4) {
            outColor = vec4(1.0, 1.0, 0.0, 1.0);  // Yellow
        } else {
            outColor = vec4(1.0, 0.0, 0.0, 1.0);  // Red
        }
    }
}
```

### 6.2 Debug Output Screenshot

```
┌────────────────────────────────────────────┐
│ ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ●  │
│ ● ● ● ● ● ○ ○ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  ● = valid (green)
│ ● ● ● ● ○ ✕ ✕ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  ○ = edge (yellow)
│ ● ● ● ○ ✕     ✕ ○ ● ● ● ● ● ● ● ● ● ● ●  │  ✕ = invalid (red)
│ ● ● ○ ✕   SKY   ✕ ○ ● ● ● ● ● ● ● ● ● ●  │
│ ● ● ○ ✕         ✕ ○ ● ● ● ● ● ● ● ● ● ●  │  Sky region has no
│ ● ● ● ○ ✕     ✕ ○ ● ● ● ● ● ● ● ● ● ● ●  │  valid probes; edges
│ ● ● ● ● ○ ✕ ✕ ○ ● ● ● ● ● ● ● ● ● ● ● ●  │  are marked for
│ ● ● ● ● ● ○ ○ ● ● ● ● ● ● ● ● ● ● ● ● ●  │  reduced temporal
│ ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ● ●  │  weight
└────────────────────────────────────────────┘
```

---

## 7. Performance Considerations

### 7.1 Texture Fetch Efficiency

The probe anchor pass samples G-Buffer at probe centers only:

- **1080p, 8px spacing**: 32,400 texture fetches (vs 2M for per-pixel)
- **1080p, 4px spacing**: 129,600 texture fetches

### 7.2 Bandwidth

| Texture        | Read/Write | Size (8px, 1080p)           |
| -------------- | ---------- | --------------------------- |
| gDepth         | Read       | 1920×1080 × R32F = 8 MB     |
| gNormal        | Read       | 1920×1080 × RGBA16F = 16 MB |
| gPosition      | Read       | 1920×1080 × RGBA16F = 16 MB |
| ProbeAnchor[0] | Write      | 240×135 × RGBA16F = 0.26 MB |
| ProbeAnchor[1] | Write      | 240×135 × RGBA16F = 0.26 MB |

**Total bandwidth per pass**: ~40 MB read, ~0.5 MB write

### 7.3 Future: Compute Shader Migration (M3)

For M3, consider migrating to compute shader:

```glsl
// lumon_probe_anchor.comp (future)
#version 430 core

layout(local_size_x = 8, local_size_y = 8) in;

layout(rgba16f) uniform image2D outProbePos;
layout(rgba16f) uniform image2D outProbeNormal;

void main() {
    ivec2 probeCoord = ivec2(gl_GlobalInvocationID.xy);
    // ... same logic as fragment shader
    imageStore(outProbePos, probeCoord, vec4(posVS, valid));
    imageStore(outProbeNormal, probeCoord, vec4(normalVS, 0.0));
}
```

Benefits:

- No rasterization overhead
- Better occupancy for small output sizes
- Can combine with probe trace pass

---

## 8. Edge Cases

### 8.1 Window Resize

When window resizes, probe grid must be recreated:

```csharp
// In LumOnBufferManager
private void OnReloadShader(EnumShaderReloadReason reason)
{
    if (reason == EnumShaderReloadReason.WindowResized)
    {
        DisposeBuffers();
        CreateBuffers();  // Recalculates ProbeCountX/Y

        api.Logger.Notification(
            $"[LumOn] Resized probe grid: {ProbeCountX}x{ProbeCountY}");
    }
}
```

### 8.2 Sub-Pixel Jittering (Future Enhancement)

For anti-aliasing, jitter probe sample position within cell using Squirrel3 (already in codebase):

```glsl
@import "squirrel3.fsh"

// Add to probe center calculation
uint seed = uint(probeCoord.x) * 1973u + uint(probeCoord.y) * 9277u + uint(frameIndex) * 26699u;
vec2 jitter = vec2(
    squirrel3(seed) * 2.0 - 1.0,
    squirrel3(seed + 1u) * 2.0 - 1.0
) * 0.25;  // ±0.25 cells
screenPixel += ivec2(jitter * float(probeSpacing));
```

> **Note**: Blue noise would provide more uniform temporal coverage (samples fill space more evenly over frames), but requires a precomputed texture. Squirrel3 is sufficient for M1-M2; consider blue noise for M3 if temporal patterns are visible.

### 8.3 Invalid Probe Handling

Downstream passes must check probe validity:

```glsl
// In ray tracing / gather passes
vec4 probeData = texelFetch(probeAnchorTex0, probeCoord, 0);
if (probeData.a < 0.5) {
    // Skip this probe or use fallback (sky radiance)
    return;
}
```

---

## 9. Next Steps

| Document                                                 | Dependency    | Topic                          |
| -------------------------------------------------------- | ------------- | ------------------------------ |
| [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md) | This document | SH encoding for probe radiance |
| [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)       | 02, 03        | Per-probe ray marching         |

---

## 10. Implementation Checklist

### 10.1 Shader Files

- [x] Create `lumon_probe_anchor.vsh`
- [x] Create `lumon_probe_anchor.fsh`
- [x] Implement `linearizeDepth()` helper (in `lumon_common.fsh`)
- [x] Implement `isSky()` helper (in `lumon_common.fsh`)
- [x] Add depth discontinuity detection (`hasDepthDiscontinuity()`)
- [x] Add sky/invalid detection
- [x] Encode normal to [0,1] range in output

### 10.2 Shader Program Class

- [x] Create `LumOnProbeAnchorShaderProgram.cs`
- [x] Add all uniform property setters
- [x] Implement static `Register()` method
- [x] Register in mod system startup

### 10.3 Buffer Management

- [x] Create ProbeAnchor framebuffer (2 attachments)
- [x] Implement grid dimension calculation
- [x] Handle window resize (recreate buffers)
- [x] Add probe count validation

### 10.4 Render Pass

- [x] Implement `RenderProbeAnchorPass()` in renderer
- [x] Set viewport to probe grid size
- [x] Bind G-Buffer textures correctly
- [x] Restore viewport after pass
- [x] Pass `DepthDiscontinuityThreshold` uniform

### 10.5 Testing

- [x] Verify probe grid overlay (DebugMode = 1)
- [x] Check valid/invalid/edge probe colors
- [ ] Test at different probe spacings (4, 8, 16)
- [ ] Verify resize handling works
- [x] Implement `screenToProbeCoord()` / `probeToScreenCoord()` helpers
- [x] Implement `getEnclosingProbes()` helper
