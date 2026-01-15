# Shader Knobs: Uniforms vs Defines

This repo supports two ways to feed values into GLSL:

- **Uniforms**: runtime values set per-draw/per-frame (no recompiles).
- **Defines**: compile-time constants injected into shader sources via `VgeShaderProgram.SetDefine(...)` (recompiles only when the value changes).

## When to use a `#define`

Use a define when the value is **constant-like** and changing it should reasonably select a different compiled shader variant:

- Feature toggles (`0/1`) that gate code paths (e.g., `#if VGE_FOO`).
- Rarely-changed mode switches.
- Loop bounds and other values that change shader structure (unrolling / early-outs / branch removal).

## When to keep a uniform

Use a uniform when the value is expected to change frequently or is inherently per-frame/per-draw:

- Matrices, camera state, time/frame index.
- Screen sizes / dynamic resolutions.
- Textures/samplers and any resource bindings.
- Fog / lighting arrays / per-scene inputs.
- Parameters that are routinely tuned live (unless you explicitly want variant recompiles).

## Required GLSL defaults

Every define used by a shader must have a safe default so the shader compiles without injection:

```glsl
#ifndef VGE_SOME_DEFINE
  #define VGE_SOME_DEFINE 1
#endif
```

Defaults for cross-pass/shared defines should live in `assets/vanillagraphicsexpanded/shaders/includes/vge_global_defines.glsl`.

## C# usage

- Prefer the canonical define list: `VgeShaderDefine` (string constants).
- Set define-backed properties **before** calling `Use()` so the correct compiled variant is bound.

## Test strategy (GPU tests)

- Tests compile shader variants using define injection (immediately after `#version`).
- “Uniform presence” tests should not assert uniforms that were migrated to defines.
- There should be at least one smoke compilation that relies only on shader-side defaults (no injected defines).
