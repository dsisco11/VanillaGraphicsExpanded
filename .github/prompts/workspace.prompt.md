---
agent: planning
---

# VanillaGraphicsExpanded - VintageStory Graphics Mod

This workspace is for a game mod called **VanillaGraphicsExpanded** for a voxel-based game named **VintageStory**. The mod enhances the game's rendering with advanced graphics features.

## Linked Game Modules
The workspace includes several of the game's original code modules for reference:
- **vsapi** - VintageStory API (client/server interfaces, rendering, math, data structures)
- **vssurvivalmod** - Survival mode content (blocks, entities, systems)
- **vscreativemod** - Creative mode tools (WorldEdit, building tools)
- **assets** - Game assets (textures, shaders, shapes, configs)

## Project Structure

### Main Mod (`VanillaGraphicsExpanded/`)
- **LumOn/** - Probe-based global illumination system (primary feature)
  - `LumOnRenderer.cs` - Main renderer orchestration
  - `LumOnBufferManager.cs` - GPU buffer management
  - `LumOnConfig.cs` - Configuration settings
  - `LumOnDebugRenderer.cs` - Debug visualization modes
  - `Shaders/` - Shader programs (trace, temporal, gather, combine, debug, upsample)
- **GBuffer/** - Geometry buffer implementation
- **PBR/** - Physically-based rendering enhancements
- **SSGI/** - Screen-space global illumination (legacy/alternative)
- **HarmonyPatches/** - Runtime game code modifications
- **SourceCodeMutator/** - Build-time source code patching
- **Rendering/** - Rendering utilities
- **DebugOverlays/** - Debug visualization tools

### Tests (`VanillaGraphicsExpanded.Tests/`)
- **GPU/** - GPU functional tests using headless OpenGL
  - Shader compilation tests
  - Functional tests for each shader pass (trace, temporal, gather, combine, upsample, debug)
  - Both SH (Spherical Harmonics) and Octahedral encoding variants
- Unit tests for shader patching, uniform extraction, matrix helpers

### Documentation (`docs/`)
- LumOn architecture documentation (7 parts covering core architecture, probe grid, radiance cache, ray tracing, temporal filtering, gather/upsample, future enhancements)

### Build System
- **CakeBuild/** - Cake build automation for packaging
- VS Code tasks: Build, Test, Package

## LumOn System Overview
LumOn is a probe-based indirect lighting system with:
1. **Probe Grid** - Screen-space probe placement
2. **Radiance Cache** - Two encoding modes: Spherical Harmonics (SH) or Octahedral
3. **Ray Tracing** - Screen-space ray marching for radiance sampling
4. **Temporal Filtering** - Motion-compensated history blending with disocclusion detection
5. **Gather & Upsample** - Bilateral-filtered probe interpolation and full-res reconstruction
6. **Combine** - Final composition with direct lighting and PBR materials

## Current Focus
See `project.todo` for test coverage tracking across shader passes (Phase 4-6 priorities).