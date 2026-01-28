// ============================================================================
// LumOn UBO Contracts (Phase 23)
//
// This file declares the shared uniform blocks used by LumOn to reduce per-pass
// uniform churn. Blocks are std140 and use fixed binding points.
// Keep fields vec4/ivec4-aligned to simplify CPU-side packing.
// ============================================================================

#ifndef LUMON_UBOS_GLSL
#define LUMON_UBOS_GLSL

// Binding points (choose values unlikely to collide with engine defaults).
// Note: GLSL 330 does not support `layout(binding=...)` for uniform blocks without 420pack.
// We keep these constants as the contract and assign bindings from C# via glUniformBlockBinding.
#define LUMON_UBO_FRAME_BINDING     12
#define LUMON_UBO_WORLDPROBE_BINDING 13

// Expected maximum levels (matches config clamp).
#ifndef LUMON_WORLDPROBE_MAX_LEVELS
  #define LUMON_WORLDPROBE_MAX_LEVELS 8
#endif

// ---------------------------------------------------------------------------
// Per-frame shared state (stable within a frame)
// ---------------------------------------------------------------------------

layout(std140) uniform LumOnFrameUBO
{
    // Matrices
    mat4 invProjectionMatrix;
    mat4 projectionMatrix;
    mat4 viewMatrix;
    mat4 invViewMatrix;

    // Temporal / velocity
    mat4 prevViewProjMatrix;
    mat4 invCurrViewProjMatrix;

    // Sizes and grid:
    // - screenSize.xy, halfResSize.zw
    vec4 screenSize_halfResSize;

    // - probeGridSize.xy, zNear.z, zFar.w
    vec4 probeGridSize_zNear_zFar;

    // Integers:
    // - x=probeSpacing, y=frameIndex, z=historyValid, w=anchorJitterEnabled
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

layout(std140) uniform LumOnWorldProbeUBO
{
    vec4 worldProbeSkyTint;      // xyz tint, w reserved
    vec4 worldProbeCameraPosWS;  // xyz camera pos, w reserved
    vec4 worldProbeOriginMinCorner[LUMON_WORLDPROBE_MAX_LEVELS]; // xyz, w reserved
    vec4 worldProbeRingOffset[LUMON_WORLDPROBE_MAX_LEVELS];      // xyz, w reserved
} lumonWorldProbe;

// ---------------------------------------------------------------------------
// Compatibility aliases
//
// These preserve legacy uniform names after the migration to uniform blocks.
// The old standalone `uniform ...;` declarations should not exist anymore.
// ---------------------------------------------------------------------------

// Matrices
#define invProjectionMatrix   (lumonFrame.invProjectionMatrix)
#define projectionMatrix      (lumonFrame.projectionMatrix)
#define viewMatrix            (lumonFrame.viewMatrix)
#define invViewMatrix         (lumonFrame.invViewMatrix)
#define prevViewProjMatrix    (lumonFrame.prevViewProjMatrix)
#define invCurrViewProjMatrix (lumonFrame.invCurrViewProjMatrix)

// Sizes
#define screenSize    (lumonFrame.screenSize_halfResSize.xy)
#define halfResSize   (lumonFrame.screenSize_halfResSize.zw)
#define probeGridSize (lumonFrame.probeGridSize_zNear_zFar.xy)

// Z-planes
#define zNear (lumonFrame.probeGridSize_zNear_zFar.z)
#define zFar  (lumonFrame.probeGridSize_zNear_zFar.w)

// Frame ints
#define probeSpacing         (lumonFrame.frameInts0.x)
#define frameIndex           (lumonFrame.frameInts0.y)
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
