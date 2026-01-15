# Shader Define Migration — Phase 0 (Audit / Inventory)

This document is the Phase 0 source-of-truth inventory for migrating “constant-like” GLSL uniforms to compile-time defines via `VgeShaderProgram.SetDefine()`.

## Existing Defines

- `LUMON_EMISSIVE_BOOST` (float literal)
  - Set via `SetDefine()` in the LumOn renderer.
  - Already behaves as a compile-time define (recompile only when value changes).

## Define Candidate Table (Source of Truth)

These are the “constant-like” uniforms that currently gate branches or select shader paths. They are strong candidates for `SetDefine()`.

| GLSL name                    | GLSL type | C# setter(s) (example)                                                                                                                       | Shaders                                                     | Current usage           | Proposed define                                |                                    Default | Notes                              |
| ---------------------------- | --------: | -------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- | ----------------------- | ---------------------------------------------- | -----------------------------------------: | ---------------------------------- |
| `lumOnEnabled`               |     `int` | `LumOnCombineShaderProgram.LumOnEnabled`, `PBRCompositeShaderProgram.LumOnEnabled`                                                           | `lumon_combine.fsh`, `pbr_composite.fsh`                    | Branch gate (`if`)      | `VGE_LUMON_ENABLED`                            | `1` (if current default is “on”), else `0` | Cross-pass toggle; Phase 3 target. |
| `enablePbrComposite`         |     `int` | `LumOnCombineShaderProgram.EnablePbrComposite`, `PBRCompositeShaderProgram.EnablePbrComposite`, `LumOnDebugShaderProgram.EnablePbrComposite` | `lumon_combine.fsh`, `pbr_composite.fsh`, `lumon_debug.fsh` | Branch gate (`if`)      | `VGE_LUMON_PBR_COMPOSITE`                      |             `0/1` (match current behavior) | Cross-pass toggle; Phase 3 target. |
| `enableAO`                   |     `int` | `LumOnCombineShaderProgram.EnableAO`, `PBRCompositeShaderProgram.EnableAO`, `LumOnDebugShaderProgram.EnableAO`                               | `lumon_combine.fsh`, `pbr_composite.fsh`, `lumon_debug.fsh` | Branch gate (`if`)      | `VGE_LUMON_ENABLE_AO`                          |             `0/1` (match current behavior) | Cross-pass toggle; Phase 3 target. |
| `enableBentNormal`           |     `int` | `LumOnCombineShaderProgram.EnableBentNormal`, `PBRCompositeShaderProgram.EnableBentNormal`, `LumOnDebugShaderProgram.EnableBentNormal`       | `lumon_combine.fsh`, `pbr_composite.fsh`, `lumon_debug.fsh` | Branch gate (`if`)      | `VGE_LUMON_ENABLE_BENT_NORMAL`                 |             `0/1` (match current behavior) | Cross-pass toggle; Phase 3 target. |
| `denoiseEnabled`             |     `int` | `LumOnUpsampleShaderProgram.DenoiseEnabled`                                                                                                  | `lumon_upsample.fsh`                                        | Branch gate (`else if`) | `VGE_LUMON_UPSAMPLE_DENOISE`                   |             `0/1` (match current behavior) | Phase 4 target.                    |
| `holeFillEnabled`            |     `int` | `LumOnUpsampleShaderProgram.HoleFillEnabled`                                                                                                 | `lumon_upsample.fsh`                                        | Branch gate (`if`)      | `VGE_LUMON_UPSAMPLE_HOLEFILL`                  |             `0/1` (match current behavior) | Phase 4 target.                    |
| `enableReprojectionVelocity` |     `int` | `LumOnTemporalShaderProgram.EnableReprojectionVelocity`                                                                                      | `lumon_temporal.fsh`                                        | Branch gate (`if`)      | `VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION` |             `0/1` (match current behavior) | Phase 5 target.                    |

### “Candidate, but likely keep uniform” (Phase 0 decision)

These are boolean-ish uniforms used for branching, but are plausibly changed at runtime more often than we’d like to recompile.

| GLSL name             | GLSL type | Shaders                                        | Proposed action           | Rationale                                                                                         |
| --------------------- | --------: | ---------------------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------- |
| `historyValid`        |     `int` | `lumon_velocity.fsh`                           | Keep as uniform           | Can flip during history resets; recompile churn not worth it.                                     |
| `anchorJitterEnabled` |     `int` | `lumon_probe_anchor.fsh`, `lumon_temporal.fsh` | Keep as uniform (for now) | Cross-pass, but may be toggled interactively; treat as runtime config unless proven restart-only. |

## Loop-Bound / Structure Knobs (Defer to Phase 6)

These uniforms influence loop bounds or shader structure. They are _potential_ define candidates, but we should decide a policy first (hot-tunable vs compile-time variants).

| GLSL name        | GLSL type | Shaders                                                         | Usage                                    | Phase 0 decision                                                                           |
| ---------------- | --------: | --------------------------------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------ |
| `holeFillRadius` |     `int` | `lumon_upsample.fsh`                                            | Bounds checks inside neighborhood loops  | Defer (Phase 6) — might stay uniform unless loops are refactored to rely on a fixed bound. |
| `raySteps`       |     `int` | `lumon_probe_trace.fsh`, `lumon_probe_atlas_trace.fsh`          | `for (int i = 1; i <= raySteps; i++)`    | Defer (Phase 6) — strong define candidate if restart-only.                                 |
| `raysPerProbe`   |     `int` | `lumon_probe_trace.fsh`                                         | `for (int i = 0; i < raysPerProbe; i++)` | Defer (Phase 6).                                                                           |
| `texelsPerFrame` |     `int` | `lumon_probe_atlas_trace.fsh`, `lumon_probe_atlas_temporal.fsh` | Batch size / distribution                | Defer (Phase 6).                                                                           |
| `filterRadius`   |     `int` | `lumon_probe_atlas_filter.fsh`                                  | Neighborhood bounds checks               | Defer (Phase 6).                                                                           |
| `sampleStride`   |     `int` | `lumon_probe_atlas_gather.fsh`                                  | Selects integration stride               | Defer (Phase 6).                                                                           |
| `srcMip`         |     `int` | `lumon_hzb_downsample.fsh`                                      | Selects mip to read                      | Keep uniform (per-pass/per-mip dispatch)                                                   |
| `hzbCoarseMip`   |     `int` | `lumon_probe_atlas_trace.fsh`                                   | Coarse mip selection                     | Keep uniform (debug/tuning; Phase 6 note agrees)                                           |

## Must Remain Uniform (Source of Truth)

These are runtime uniforms that should not be converted to defines (per the project guidelines).

### Matrices / transforms (per-frame)

- `invProjectionMatrix`, `projectionMatrix`
- `viewMatrix`, `invViewMatrix`
- `prevViewProjMatrix`, `invCurrViewProjMatrix`
- `invModelViewMatrix`
- `toShadowMapSpaceMatrixNear`, `toShadowMapSpaceMatrixFar`

### Per-frame / per-resolution values

- `frameIndex`
- `screenSize`, `halfResSize`
- `zNear`, `zFar`

### Fog + lighting state

- `rgbaFogIn`, `fogDensityIn`, `fogMinIn`
- `rgbaAmbientIn`, `rgbaLightIn`, `lightDirection`
- `pointLightsCount`, `pointLights3[100]`, `pointLightColors3[100]`
- `shadowRangeNear`, `shadowRangeFar`, `shadowZExtendNear`, `shadowZExtendFar`, `dropShadowIntensity`

### Textures / samplers

(All `sampler2D`/`sampler2DShadow` uniforms should remain runtime-bound.)

## Shader Wrapper Inventory (C# → GLSL)

This is the high-level mapping of shader programs to their “notable” uniforms.

- **LumOn combine** (`LumOnCombineShaderProgram` → `lumon_combine.*`): `lumOnEnabled`, `enablePbrComposite`, `enableAO`, `enableBentNormal`, plus intensity/tint and matrices.
- **PBR composite** (`PBRCompositeShaderProgram` → `pbr_composite.*`): same cross-pass toggles as above + fog uniforms.
- **LumOn upsample** (`LumOnUpsampleShaderProgram` → `lumon_upsample.*`): `denoiseEnabled`, `holeFillEnabled`, `holeFillRadius` + sigmas.
- **LumOn temporal** (`LumOnTemporalShaderProgram` → `lumon_temporal.*`): `enableReprojectionVelocity` + thresholds; jitter controls.
- **LumOn velocity** (`LumOnVelocityShaderProgram` → `lumon_velocity.*`): `historyValid`.
- **LumOn probe trace** (`LumOnProbeTraceShaderProgram` → `lumon_probe_trace.*`): `raysPerProbe`, `raySteps`.
- **LumOn probe-atlas trace/temporal/filter/gather** (various programs): `texelsPerFrame`, `raySteps`, `filterRadius`, `sampleStride`.
- **PBR direct lighting** (`PBRDirectLightingShaderProgram` → `pbr_direct_lighting.*`): point light arrays, shadow uniforms.
