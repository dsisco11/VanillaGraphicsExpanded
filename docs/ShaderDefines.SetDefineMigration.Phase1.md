# Shader Define Migration — Phase 1 (Naming + Global Include)

Phase 1 establishes a stable define naming scheme, a shared default include, and the recommended usage patterns.

## Naming conventions

- New compile-time defines should use a `VGE_` prefix.
- Feature-scoped defines should add a subsystem prefix, e.g. `VGE_LUMON_*`.
- Define names are SCREAMING_SNAKE_CASE.
- Boolean toggles should be **numeric** `0/1` defines (not `#define FOO`), so the shader can use `#if FOO`.
- Keep the existing `LUMON_EMISSIVE_BOOST` define name unchanged.
  - Rationale: it is already shipped and used by `SetDefine()`; renaming would cause churn without functional value.

## Global vs feature-specific defines

- **Global**: used by multiple shader families/passes (e.g., LumOn + PBR composite). These should have shared defaults in a single include.
- **Feature-specific**: used by exactly one stage or shader family. These can be defaulted locally in that shader (or later grouped into a feature include if it grows).

## Global defines include

File:

- `VanillaGraphicsExpanded/VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_global_defines.glsl`

Currently included defaults:

- `VGE_LUMON_ENABLED`
- `VGE_LUMON_PBR_COMPOSITE`
- `VGE_LUMON_ENABLE_AO`
- `VGE_LUMON_ENABLE_SHORT_RANGE_AO`

## GLSL usage pattern

1. Import the global defaults (near the top of the shader):

```glsl
@import "./includes/vge_global_defines.glsl"
```

1. Use `#if` for numeric 0/1 toggles:

```glsl
#if VGE_LUMON_ENABLED
  // GI enabled path
#else
  // GI disabled path
#endif
```

1. If a define is feature-specific and not in the global include, default it locally:

```glsl
#ifndef VGE_SOME_FEATURE
  #define VGE_SOME_FEATURE 0
#endif
```

## C# usage pattern

- Use `VgeShaderProgram.SetDefine("NAME", "VALUE")`.
- Values should be:
  - Int/bool toggles: `"0"` / `"1"`
  - Floats: invariant-culture literals (e.g. `"1.0"`, `"0.35"`)
- Only call `SetDefine()` when the value may have changed (optional optimization); `SetDefine()` itself recompiles only if the define value actually changes.
