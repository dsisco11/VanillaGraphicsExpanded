# Phase 14.1 — Reprojection Inputs Contract

This document defines the reprojection inputs (textures + uniforms) and a consistent velocity convention for temporal shaders.

## Temporal consumers (current)

### 1) Probe-grid temporal accumulation

- Shader: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_temporal.fsh`
- Program binding: `VanillaGraphicsExpanded/LumOn/Shaders/LumOnTemporalShaderProgram.cs`
- Reprojection: YES (world-space anchor → previous screen UV)

**Textures**

- `radianceCurrent0`, `radianceCurrent1`
- `radianceHistory0`, `radianceHistory1`
- `probeAnchorPosition` (posWS.xyz, validity in w)
- `probeAnchorNormal` (normalWS.xyz in [0,1] encoding)
- `historyMeta` (R=linear depth in view-space units, G/B=normal.xy packed to [0,1], A=accumCount)

**Uniforms**

- `viewMatrix` (WS→VS; used to compute view-space depth and to rotate normals into view-space for validation)
- `prevViewProjMatrix` (WS→prev clip; used to compute history UV)
- `probeGridSize`
- `zNear`, `zFar` (only used for helper functions in this shader; current path uses anchor depth)
- `temporalAlpha`, `depthRejectThreshold`, `normalRejectThreshold`

**Notes**

- Current reprojection is position-based (world-space anchors). It does not require a per-pixel velocity buffer.
- History rejection uses depth + normal from `historyMeta`.

### 2) Screen-probe atlas temporal accumulation

- Shader: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_temporal.fsh`
- Program binding: `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTemporalShaderProgram.cs`
- Reprojection: NO (atlas texels are in a stable atlas coordinate system)

**Textures**

- `octahedralCurrent` (current frame trace output, includes copied history for non-traced texels)
- `octahedralHistory`
- `probeAtlasMetaCurrent`, `probeAtlasMetaHistory`
- `probeAnchorPosition` (probe validity)

**Uniforms**

- `probeGridSize`
- `frameIndex`, `texelsPerFrame`
- `temporalAlpha`, `hitDistanceRejectThreshold`

**Notes**

- Disocclusion detection is done via hit-distance + meta/flags. No screen-space reprojection is involved.

## Velocity convention (shared)

When a temporal pass needs screen-space reprojection without engine motion vectors:

- **velocityUv**: `velocityUv = currUv - prevUv`
- **history UV from velocity**: `prevUv = currUv - velocityUv`
- **space**: UV in `[0,1]` matching GLSL `texture()` coordinates
- **axes**: UV origin is **bottom-left**, consistent with existing `NDC → UV` conversion `uv = ndc * 0.5 + 0.5`

Validity rules (recommended):

- depth is invalid/sentinel (e.g., `depthRaw >= 1.0` for sky)
- previous clip-space `w <= 0` (behind camera)
- `prevUv` out of bounds
- NaNs propagate into UVs

## Shared include contract

A shared reprojection helper include lives at:

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_reprojection.glsl`

Exported functions:

- `lumonComputePrevUvFromDepth(currUv, depthRaw, invCurrViewProj, prevViewProj, out prevUv)`
- `lumonComputeVelocityUvFromDepth(currUv, depthRaw, invCurrViewProj, prevViewProj, out velocityUv, out prevUv)`

Expected uniforms for passes that use depth-based reprojection:

- `uniform sampler2D primaryDepth;` (current frame depth)
- `uniform mat4 invCurrViewProjMatrix;` (inverse current ViewProj)
- `uniform mat4 prevViewProjMatrix;` (previous ViewProj)

(If a pass already has stable world-space anchors, it can keep using position-based reprojection and skip velocity.)

## Velocity texture (planned implementation)

To support reuse across multiple temporal consumers and future per-object motion, the preferred path is to generate a dedicated **velocity texture** each frame.

### Encoding

- `velocityUv.xy`: stored as floats (UV delta per frame)
- `flagsPacked`: stored as `uintBitsToFloat(flags)` in one channel, matching the existing probe-atlas meta pattern

Important: `uintBitsToFloat` packing requires a **32-bit float channel** to preserve bit patterns. This means the velocity render target must include at least one `*32F` channel for the packed flags (e.g., `RGBA32F` with `A = uintBitsToFloat(flags)`).

### Suggested flags

- `VALID` (reprojection valid)
- `SKY_OR_INVALID_DEPTH`
- `PREV_BEHIND_CAMERA` (prev clip `w <= 0`)
- `PREV_OOB` (prev UV out of bounds)

Consumers use:

- `prevUv = currUv - velocityUv`
- If `VALID` is not set, reject history.
