// ============================================================================
// LumOn UBO Contracts
//
// This file declares the shared uniform blocks used by LumOn to reduce per-pass
// uniform churn. Blocks are std140 and use fixed binding points.
// Keep fields vec4/ivec4-aligned to simplify CPU-side packing.
// ============================================================================

#ifndef LUMON_UBOS_GLSL
#define LUMON_UBOS_GLSL

// Binding points (choose values unlikely to collide with engine defaults).
// Note: GLSL 330 does not support `layout(binding=...)` for uniform blocks without 420pack.
// All consumers require 420pack and declare their binding points explicitly.
@import "./vge_ubo_layout.glsl"
@import "./vge_frame_ubo.glsl"
#define LUMON_UBO_FRAME_BINDING     17
#define LUMON_UBO_WORLDPROBE_BINDING 13

// Expected maximum levels (matches config clamp).
#ifndef LUMON_WORLDPROBE_MAX_LEVELS
  #define LUMON_WORLDPROBE_MAX_LEVELS 8
#endif

// ---------------------------------------------------------------------------
// Per-frame shared state (stable within a frame)
// ---------------------------------------------------------------------------

layout(std140, binding = LUMON_UBO_FRAME_BINDING) uniform LumOnFrameUBO
{
    // Reduced target and probe-grid dimensions are specific to this effect.
    vec4 halfResSize_probeGridSize;

    // Integers:
    // - x=probeSpacing, y=reserved, z=historyValid, w=anchorJitterEnabled
    ivec4 frameInts0;

    // - x=pmjCycleLength, y=enableVelocityReprojection, z/w reserved
    ivec4 frameInts1;

    // Floats:
    // - x=anchorJitterScale, y=velocityRejectThreshold, z/w reserved
    vec4 frameFloats0;

    // Sky fallback parameters (trace pass)
    vec4 sunPosition;   // xyz, w reserved
    vec4 sunColor;      // xyz, w reserved
    vec4 ambientColor;  // xyz, w reserved

} lumonFrame;

// ---------------------------------------------------------------------------
// World-probe clipmap params (stable within a frame)
// ---------------------------------------------------------------------------

layout(std140, binding = LUMON_UBO_WORLDPROBE_BINDING) uniform LumOnWorldProbeUBO
{
    vec4 worldProbeSkyTint;      // xyz tint, w reserved
    vec4 worldProbePlayerOriginWorld; // xyz origin used for relative clipmap coordinates, w reserved
    vec4 worldProbeOriginMinCorner[LUMON_WORLDPROBE_MAX_LEVELS]; // xyz, w reserved
    vec4 worldProbeRingOffset[LUMON_WORLDPROBE_MAX_LEVELS];      // xyz, w reserved
} lumonWorldProbe;

// ---------------------------------------------------------------------------
// Compatibility aliases
//
// These preserve legacy uniform names after the migration to uniform blocks.
// The old standalone `uniform ...;` declarations should not exist anymore.
// ---------------------------------------------------------------------------

// Sizes remain effect-specific; full-view dimensions come from VgeFrameUBO.
#define halfResSize   (lumonFrame.halfResSize_probeGridSize.xy)
#define probeGridSize (lumonFrame.halfResSize_probeGridSize.zw)

// Frame ints
#define probeSpacing         (lumonFrame.frameInts0.x)
#define historyValid         (lumonFrame.frameInts0.z)
#define anchorJitterEnabled  (lumonFrame.frameInts0.w)
#define pmjCycleLength       (lumonFrame.frameInts1.x)
#define enableVelocityReprojection (lumonFrame.frameInts1.y)

// Frame floats
#define anchorJitterScale     (lumonFrame.frameFloats0.x)
#define velocityRejectThreshold (lumonFrame.frameFloats0.y)

// Sky fallback
#define sunPosition (lumonFrame.sunPosition.xyz)
#define sunColor    (lumonFrame.sunColor.xyz)
#define ambientColor (lumonFrame.ambientColor.xyz)


#endif // LUMON_UBOS_GLSL
