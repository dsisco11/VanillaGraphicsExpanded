---
name: workspace
description: This prompt provides an overview of the VanillaGraphicsExpanded repository, including its structure, key components, and current focus areas.
---

# VanillaGraphicsExpanded - VintageStory Graphics Mod

This repo contains the mod code and tests for **VanillaGraphicsExpanded**, a rendering overhaul for the voxel game **VintageStory** (PBR, GI, material systems, debugging/profiling). Game binaries are referenced via `$(VINTAGE_STORY)` in the project files, so the engine assemblies are not vendored here.

## Repository Layout

### Main Mod (`VanillaGraphicsExpanded/`)
- **LumOn/** - Global illumination system (screen-probe atlas + world-space clipmap probes)
  - `LumOnRenderer.cs` - Render orchestration
  - `LumOnBufferManager.cs` - GI render targets (HZB, history, probe atlas)
  - `WorldProbes/` - Clipmap topology, scheduling, CPU trace integration, trace scenes
  - `Shaders/` - LumOn shader programs (trace/temporal/filter/gather/upsample/combine/velocity/HZB)
- **PBR/** - Direct lighting pass + composite pass
- **PBR/Materials/** - Material atlas system (params + normal/depth sidecar), async build scheduler, disk cache, overrides
- **GBuffer/** - Primary framebuffer extensions (adds normal/material MRT attachments)
- **Rendering/** - GPU abstractions, shader system, GPU profiling
- **HarmonyPatches/** - Runtime hooks into the engine (framebuffer and shader bindings)
- **SourceCodeMutator/** - Shader import/define preprocessing and AST tooling
- **DebugView/**, **DebugOverlays/** - In-game debug UI and visualization tools
- **ModSystems/** - Config loading + live reload
- **Noise/**, **Numerics/** - PMJ/blue noise generators and SIMD helpers
- **Profiling/** - CPU event source + scope helpers
- **assets/vanillagraphicsexpanded/** - Shaders, configs, language entries

### Tests (`VanillaGraphicsExpanded.Tests/`)
- **GPU/** - Headless OpenGL shader and render tests
- **Unit/** - CPU-side logic tests (materials, world probes, numerics)

### Documentation (`docs/`)
- LumOn architecture series (`LumOn.01-21`)
- Material atlas + cache docs
- Normal/height bake notes
- Shader import/define migration docs
- Profiling event source docs

### Build/Tools
- use the `dotnet: build` task to compile the mod.
- use the `dotnet: test` task to run all unit and GPU tests.
- `VanillaGraphicsExpanded.sln`

## Live Config + Debug Tooling
- Config file: `VanillaGraphicsExpanded.json` (ConfigLib aware) with live reload via `ILiveConfigurable` + `LiveConfigReload`
- Config system entry: `ModSystems/ConfigModSystem.cs` (load/sanitize, ConfigLib sync, notify + persist)
- Debug UI: `DebugView/VgeDebugViewManager.cs` (F8 hotkey) and overlays in `DebugOverlays/`
- Render-stage GPU labels (DEBUG builds): `GpuDebugLabelManager` wraps VS render stages
- Debug hotkey registration: `VanillaGraphicsExpandedModSystem.cs` (F8 toggle wiring)
- ConfigLib event hook: `ModSystems/ConfigModSystem.cs` (config-saved bus listener)

## GPU Abstractions (Rendering/)
- `GpuBufferObject` - RAII wrapper for GL buffer objects; allocate, resize, map, debug labels
- `GpuVbo` - RAII wrapper for VBOs; allocate, resize, attribute setup, debug labels
- `GpuVao` - RAII wrapper for VAOs; attribute binding, debug labels
- `GpuEbo` - RAII wrapper for EBOs; allocate, resize, debug labels
- `VgeShaderProgram` - Shader program base; compile/link, uniform setters, debug labels
- `Texture2D` / `Texture3D` - RAII wrappers for 'static' 2D + 2D-array/3D textures; allocate, resize, upload, readback, debug labels
- `DynamicTexture2D` / `DynamicTexture3D` - RAII wrappers for 'dynamic' 2D + 2D-array/3D textures; allocate, resize, upload, readback, debug labels
- `GBuffer` - FBO wrapper (single, MRT, depth-only) with attach/resize/blit and wrapper support for engine-owned FBOs
- `GBufferManager` - Injects extra MRT targets (normal/material) into the engine Primary framebuffer
- `TextureStreamingManager` - PBO-backed upload queue with per-frame budgets, persistent-mapped ring or triple-buffer fallback
- `TextureFormatHelper`, `TextureFilterMode`, `GlDebug` - format mapping, filter enums, GL object labels/groups
- `Rendering/Shaders` - `VgeShaderProgram` base, stage pipeline, define injection, diagnostics

## Shader Patching (Base Game Shaders)
- Hook point: `ShaderIncludesHook` patches `ShaderRegistry.LoadShaderProgram` (postfix) and runs only on non-VGE shader programs (domain != `vanillagraphicsexpanded`)
- Pipeline per stage: parse GLSL to `SyntaxTree` -> pre-process (`VanillaShaderPatches.TryApplyPreProcessing`) -> inline `@import` (`ShaderImportsSystem` + `GlslPreprocessor`) -> post-process (`VanillaShaderPatches.TryApplyPatches`) -> emit GLSL and `StripNonAscii` -> write back to `shader.Code`
- Pre-process inserts `@import` blocks (e.g., `vsfunctions.glsl`, `vge_material.glsl`, `vge_normaldepth.glsl`, `vge_parallax.glsl`) and optional parallax defines
- Post-process injects G-buffer outputs/samplers, material param reads, and fog/light overrides for target shaders (chunk + generic + sky)
- Runtime binding: `TerrainMaterialParamsTextureBindingHook` patches chunk shader property setters to bind injected samplers (`vge_materialParamsTex`, `vge_normalDepthTex`) to texture units

## VGE Shader Program Conventions (Memory Shaders)
- Rule: every VGE shader must have a dedicated shader class that derives from `VgeShaderProgram` or `VgeStageNamedShaderProgram`
- Each shader class exposes uniforms as property setters/accessors and caches locations as needed
- Registration happens in `VanillaGraphicsExpandedModSystem.LoadShaders` via `*.Register(api)`; `Register` must `Initialize(api)` + `CompileAndLink()` and register as a memory shader program
- VGE shader compilation uses `ShaderSourceCode` (define injection + import inlining + diagnostics) and is skipped by the base-game patch hook

## Profiling and Tracking
- CPU scopes: `Profiler` + `ProfileScope` emit ETW EventSource events via `VgeProfilingEventSource` (name, category, thread id)
- GPU timing: `GlGpuProfiler` uses timestamp queries with rolling stats; `GlGpuProfilerRenderer` calls `BeginFrame` at render stage `Before`; `GlGpuProfilerScope` wraps begin/end
- GPU debug labels: `GlDebug` provides object labels + debug group scopes; `GpuDebugLabelManager` registers stage-wide push/pop groups (DEBUG builds) for RenderDoc/NSight

## LumOn System Summary
- Screen-probe atlas pipeline: trace -> temporal -> probe-space filter -> gather -> upsample -> combine
- HZB + velocity passes for traversal and reprojection
- Debug modes in `LumOnDebugRenderer.cs`
- World-space clipmap probes (CPU trace + GPU upload) in `LumOn/WorldProbes/`

## PBR + Material Systems
- Direct lighting pass writes split radiance buffers (diffuse/specular/emissive)
- Composite pass merges direct lighting + LumOn indirect and applies AO/fog
- Material atlas builds packed params (roughness/metallic/emissive) and normal/depth sidecar, with async builds, disk cache, and overrides

## Shader Asset Layout
- Domain: `vanillagraphicsexpanded`
- Main shader assets: `assets/vanillagraphicsexpanded/shaders/*.vsh|*.fsh`
- Shared includes: `assets/vanillagraphicsexpanded/shaders/includes/*.glsl`

## Current Focus
- `project.todo` - Active feature work (LumOn alignment, PBR pipeline, material atlas)
- `docs/*.todo` - Phase-specific design and implementation tasks
