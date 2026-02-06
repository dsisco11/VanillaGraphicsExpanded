# VGE GPU Binding Registry

This document defines reserved OpenGL binding points/units used by VanillaGraphicsExpanded (VGE).

Bindings are part of the shader/program contract. `GpuProgramLayout` is expected to be the single source of truth for applying and validating these bindings at runtime.

## Engine vertex attribute locations (custom shaders)

Vintage Story uploads meshes into a VAO with these attribute locations when using custom shaders:

- xyz = 0
- uv = 1
- rgba = 2
- rgba2 = 3
- flags = 4
- customFloats = 5
- customInts = 6
- customBytes = 7

Source: [vsapi/Client/API/IRenderAPI.cs](../vsapi/Client/API/IRenderAPI.cs) (see `UploadMesh` docs).

Shader rule:

- Every VGE vertex shader should include `#extension GL_ARB_explicit_attrib_location: enable` and use `layout(location = N)` for `in` attributes.

## SPIR-V GLSL baseline

For SPIR-V-targeted shaders (especially compute), prefer:

- `#version 460 core`

Rationale:

- Matches modern OpenGL SPIR-V expectations.
- Avoids extension soup for explicit bindings/locations.

## UBO binding points (GL_UNIFORM_BUFFER)

These are global GL state. VGE reserves the following binding points:

- 12: Frame UBO (per-frame constants) (`LumOnFrameUBO` today)
- 13: WorldProbe UBO (`LumOnWorldProbeUBO` today)
- 14: Object UBO (reserved)
- 15: Material UBO (reserved)
- 16: Lights UBO (reserved)
- 27: Terrain bridge UBO (`LumOnTerrainBridgeUBO`)

Code source of truth: [VanillaGraphicsExpanded/Rendering/GpuBindingRegistry.cs](../VanillaGraphicsExpanded/Rendering/GpuBindingRegistry.cs)

## Compute binding ranges (SSBO + images + samplers)

Compute shaders in `assets/vanillagraphicsexpanded/shaders/*.csh` already use explicit bindings.

### LumOnScene (current)

- SSBO bindings:
  - 0..2 used for work/metadata/triangles/slots depending on shader
- Image units:
  - 0..1 used for atlas outputs / page usage stamp
- Sampler texture units:
  - 0..7 used for scene inputs / LUTs depending on shader

Rule:

- Treat these bindings as _per-dispatch contract state_: always bind all required resources before dispatch.

## Texture unit conventions (graphics)

Sampler bindings are global GL state. VGE shaders typically assume stable texture-unit assignments.

Current convention (recommended to keep):

- 0..7: common fullscreen inputs (depth, gbuffer, etc)
- 16..31: high-value debug/world-probe/lumon resources

Note: exact per-shader units should be captured and enforced by shader-specific `GpuProgramLayout` subclasses.

In VGE-owned shader programs (`GpuProgram`), these contracts are applied once after link via `GpuProgramLayout.ApplyContract(...)`.
