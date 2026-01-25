# OpenGL PipelineStateObject (PSO) — Overview

This note summarizes the Phase 0 baseline decision and the knobs tracked by the PSO + `GlStateCache`.

## Baseline defaults

Baseline defaults are **OpenGL specification defaults** for the tracked knobs.

See the full baseline table in `docs/OpenGL.PipelineStateObject.Phase0.md`.

## Tracked knobs (PSO)

Tracked by `GlPipelineDesc` + applied via `GlStateCache.Apply(...)`:

- Depth: test enable, `DepthFunction`, write mask
- Blend (global): enable + `GlBlendFunc` (separate RGB/alpha)
- Blend (indexed/MRT): per-attachment enable + `GlBlendFunc` (`glBlendFuncSeparatei`)
- Cull face: enable
- Scissor test: enable (scissor box remains dynamic)
- Color mask: RGBA packed (`GlColorMask`)
- Debug-only: line width, point size

## Dynamic state (not in PSO)

Not stored in the immutable PSO descriptor, but generally routed through `GlStateCache` bind/scope APIs:

- Viewport, scissor box
- Framebuffer binds (read/draw), draw/read buffers
- Program use, VAO bind, program pipeline bind
- Buffers, textures, samplers
- Uniforms / UBO updates

