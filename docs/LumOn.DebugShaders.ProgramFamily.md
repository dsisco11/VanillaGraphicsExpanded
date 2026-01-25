# LumOn Debug Shaders: Program Family (Multi-Entrypoint)

LumOn’s fullscreen debug overlay is now a **shader program family** instead of a single monolithic `lumon_debug` program.

- Each high-level debug category has its own shader program:
  - `lumon_debug_probe_anchors`
  - `lumon_debug_gbuffer`
  - `lumon_debug_temporal`
  - `lumon_debug_sh`
  - `lumon_debug_indirect`
  - `lumon_debug_probe_atlas`
  - `lumon_debug_composite`
  - `lumon_debug_direct`
  - `lumon_debug_velocity`
  - `lumon_debug_worldprobe`
- The legacy `lumon_debug` entrypoint still exists as a dispatcher/compat program.

The C# side registers all of these as memory shader programs and keeps compile-time defines synchronized across the family.

## How to add a new debug category

A “category” here means: a new debug program name, plus one or more `LumOnDebugMode` values that select it.

### 1) Add shader stages

Create the new stage files under:

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/`

Name them to match the program/pass name:

- `lumon_debug_<mycategory>.vsh`
- `lumon_debug_<mycategory>.fsh`

Implementation notes:

- Start each stage with `#version ...` (VGE injects defines immediately after it).
- Prefer sharing common code via `@import` includes.
- Any new compile-time switches must have safe defaults (see `shaders/includes/vge_global_defines.glsl`).

### 2) Wire C# program selection

Update the debug program-kind plumbing:

- Add a new value to `LumOnDebugShaderProgramKind`.
- Update `LumOnDebugRenderer.GetShaderProgramKind(...)` to map your `LumOnDebugMode` values to the new kind.
- Update `LumOnDebugRenderer.GetShaderProgramName(...)` to return the new `lumon_debug_<mycategory>` program name.

### 3) Register the new program in the family

Add the new program name to the family registrar:

- `LumOnDebugShaderProgramFamily.ProgramNames`

This ensures it’s registered (memory shader program) and that shared define updates apply consistently.

### 4) Add compilation coverage

Update GPU compilation coverage so breakage is caught quickly:

- Add the new `lumon_debug_<mycategory>.vsh/.fsh` pair to the shader compilation list.
- If the new shaders introduce new important define variants, add a small define-injection compilation test.

## Runtime define rules

- Compile-time toggles are set via `GpuProgram.SetDefine(...)` and can trigger recompiles.
- The debug renderer applies shared toggles (e.g., composite/world-probe topology) across **all** debug programs so switching categories doesn’t produce missing-define/mismatched-variant issues.
