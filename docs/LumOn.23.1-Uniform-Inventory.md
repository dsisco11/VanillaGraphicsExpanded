# LumOn Phase 23.1 — Uniform Inventory (Pre-UBO)

This document inventories **current LumOn GLSL uniforms** (especially non-sampler/value uniforms), and classifies them by **update frequency** and **recommended destination** for the UBO refactor in Phase 23.

Notes:
- Inventory was derived by scanning `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/**` for `uniform ...;` declarations in `lumon_*` entrypoints and `includes/lumon_*` includes.
- This is an *authoritative snapshot* of **declared** uniforms, not a whole-program “active uniform” list (drivers may strip unused uniforms).
- Sampler uniforms are listed only briefly; Phase 23 primarily targets **value uniforms** (matrices, sizes, config knobs).

## 1) Recommended Buckets

### 1.1 FrameUBO (shared + stable within the frame)

These values are written repeatedly across multiple passes each frame today and are the primary UBO win.

- Matrices:
  - `mat4 invProjectionMatrix`
  - `mat4 projectionMatrix`
  - `mat4 viewMatrix`
  - `mat4 invViewMatrix`
  - `mat4 prevViewProjMatrix` (velocity/debug)
  - `mat4 invCurrViewProjMatrix` (velocity)
- Sizes / grid:
  - `vec2 screenSize`
  - `vec2 halfResSize`
  - `vec2 probeGridSize`
  - `int probeSpacing`
- Frame + camera:
  - `int frameIndex`
  - `float zNear`
  - `float zFar`
- Probe jitter (must match anchor ↔ temporal):
  - `int anchorJitterEnabled`
  - `float anchorJitterScale`
  - `int pmjCycleLength`
- Velocity reprojection toggles (temporal/debug):
  - `int enableVelocityReprojection`
  - `float velocityRejectThreshold`
  - `int historyValid` (velocity)
- Sky fallback (trace pass):
  - `vec3 sunPosition`
  - `vec3 sunColor`
  - `vec3 ambientColor`

### 1.2 WorldProbeUBO (shared + stable within the frame, world-probe clipmap params)

These are currently set per-program and per-level via repeated uniform writes:

- `vec3 worldProbeCameraPosWS`
- `vec3 worldProbeSkyTint`
- `vec3 worldProbeOriginMinCorner[LUMON_WORLDPROBE_MAX_LEVELS]` (max 8)
- `vec3 worldProbeRingOffset[LUMON_WORLDPROBE_MAX_LEVELS]` (max 8)

Recommendation: encode the arrays as `vec4[8]` payload in std140 to avoid alignment pitfalls.

### 1.3 Pass uniforms (legit per-pass controls)

Keep these as uniforms initially (later they could move into a small PassUBO if desired).

- HZB downsample:
  - `int srcMip`
- Probe-atlas filter:
  - `int filterRadius`
  - `float hitDistanceSigma`
- Probe-atlas temporal:
  - `float temporalAlpha`
  - `float hitDistanceRejectThreshold`
- Gather (atlas + SH9):
  - `float intensity`
  - `vec3 indirectTint`
  - `float leakThreshold`
  - `int sampleStride`
- Upsample:
  - `float upsampleDepthSigma`
  - `float upsampleNormalSigma`
  - `float upsampleSpatialSigma`
  - `int holeFillRadius`
  - `float holeFillMinConfidence`
- Combine / composite parity (still used by debug, even if the runtime combine pass is removed):
  - `float indirectIntensity`
  - `vec3 indirectTint`
  - `float diffuseAOStrength`
  - `float specularAOStrength`
- Debug-only controls:
  - `int debugMode`
  - `int gatherAtlasSource`
  - (debug also consumes many “shared” values; those should come from FrameUBO)

## 2) Value Uniforms — Full List (by name)

This is the union of *declared* value uniforms found in `lumon_*` shader entrypoints and `includes/lumon_*` includes:

- `vec3 ambientColor`
- `int anchorJitterEnabled`
- `float anchorJitterScale`
- `vec2 atlasSize` (world-probe clipmap resolve)
- `int debugMode`
- `float depthDiscontinuityThreshold`
- `float depthRejectThreshold` (debug)
- `float diffuseAOStrength`
- `int enableVelocityReprojection`
- `int filterRadius`
- `int frameIndex`
- `int gatherAtlasSource`
- `vec2 halfResSize`
- `int historyValid`
- `float hitDistanceRejectThreshold`
- `float hitDistanceSigma`
- `float holeFillMinConfidence`
- `int holeFillRadius`
- `float indirectIntensity`
- `vec3 indirectTint`
- `float intensity`
- `mat4 invCurrViewProjMatrix`
- `mat4 invProjectionMatrix`
- `mat4 invViewMatrix`
- `float leakThreshold`
- `float normalRejectThreshold` (debug)
- `int pmjCycleLength`
- `mat4 prevViewProjMatrix`
- `vec2 probeGridSize`
- `int probeSpacing`
- `mat4 projectionMatrix`
- `int sampleStride`
- `vec2 screenSize`
- `float specularAOStrength`
- `int srcMip`
- `vec3 sunColor`
- `vec3 sunPosition`
- `float temporalAlpha`
- `float upsampleDepthSigma`
- `float upsampleNormalSigma`
- `float upsampleSpatialSigma`
- `float velocityRejectThreshold`
- `mat4 viewMatrix`
- `float zFar`
- `float zNear`

World-probe parameter uniforms (declared in `includes/lumon_worldprobe.glsl`):
- `vec3 worldProbeCameraPosWS`
- `vec3 worldProbeSkyTint`
- `vec3 worldProbeOriginMinCorner[LUMON_WORLDPROBE_MAX_LEVELS]`
- `vec3 worldProbeRingOffset[LUMON_WORLDPROBE_MAX_LEVELS]`

## 3) Biggest Duplication Hotspots (today)

These are currently set repeatedly in `LumOnRenderer` for many programs each frame and are the highest-priority FrameUBO candidates:

- Matrices: `invProjectionMatrix`, `projectionMatrix`, `viewMatrix`, `invViewMatrix`
- Per-frame sizing: `screenSize`, `halfResSize`
- Z planes: `zNear`, `zFar`
- Probe grid: `probeGridSize`, `probeSpacing`
- Frame counter: `frameIndex`
- World-probe per-level arrays: `worldProbeOriginMinCorner[]`, `worldProbeRingOffset[]`

## 4) Decisions Recorded for Phase 23.2+

- Start with **FrameUBO + WorldProbeUBO** only.
- Keep “true pass knobs” as plain uniforms for now (filter radius/sigma, temporal alpha, leak thresholds, upsample sigmas).
- Keep specialization knobs as **defines** (ray steps, HZB coarse mip, etc.).

