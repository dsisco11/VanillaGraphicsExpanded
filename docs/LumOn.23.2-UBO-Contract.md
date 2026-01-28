# LumOn Phase 23.2 — UBO Contract

This defines the **uniform block contract** (names, bindings, and field layout) used by the LumOn UBO refactor (Phase 23).

## 1) Source of Truth

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_ubos.glsl`

## 2) Binding Points + Block Names

Bindings are part of the contract, but **assigned from C#** (GLSL 330 does not support `layout(binding=...)` for uniform blocks without extensions):

- `binding = 12`: `LumOnFrameUBO` (instance: `lumonFrame`)
- `binding = 13`: `LumOnWorldProbeUBO` (instance: `lumonWorldProbe`)

## 3) Field Layout (std140)

The contract intentionally uses `vec4`/`ivec4` “pack groups” to keep CPU-side packing simple.

### 3.1 `LumOnFrameUBO` (`lumonFrame`)

- Matrices:
  - `mat4 invProjectionMatrix`
  - `mat4 projectionMatrix`
  - `mat4 viewMatrix`
  - `mat4 invViewMatrix`
  - `mat4 prevViewProjMatrix`
  - `mat4 invCurrViewProjMatrix`
- Packed vec4s:
  - `vec4 screenSize_halfResSize` (`xy=screenSize`, `zw=halfResSize`)
  - `vec4 probeGridSize_zNear_zFar` (`xy=probeGridSize`, `z=zNear`, `w=zFar`)
- Packed ivec4s:
  - `ivec4 frameInts0` (`x=probeSpacing`, `y=frameIndex`, `z=historyValid`, `w=anchorJitterEnabled`)
  - `ivec4 frameInts1` (`x=pmjCycleLength`, `y=enableVelocityReprojection`, `z/w=reserved`)
- Packed floats:
  - `vec4 frameFloats0` (`x=anchorJitterScale`, `y=velocityRejectThreshold`, `z/w=reserved`)
- Sky fallback:
  - `vec4 sunPosition` (`xyz`)
  - `vec4 sunColor` (`xyz`)
  - `vec4 ambientColor` (`xyz`)

### 3.2 `LumOnWorldProbeUBO` (`lumonWorldProbe`)

- Scalars:
  - `vec4 worldProbeSkyTint` (`xyz`)
  - `vec4 worldProbeCameraPosWS` (`xyz`)
- Arrays (std140-safe, vec4-aligned payload):
  - `vec4 worldProbeOriginMinCorner[8]` (`xyz`)
  - `vec4 worldProbeRingOffset[8]` (`xyz`)

## 4) Notes for CPU Packing (Phase 23.3)

- Treat these blocks as binary layouts; prefer explicit offsets or fixed “float/int arrays” over relying on implicit struct packing.
- Arrays are `vec4[8]` in the UBO even though the shader historically used `vec3[8]`.
