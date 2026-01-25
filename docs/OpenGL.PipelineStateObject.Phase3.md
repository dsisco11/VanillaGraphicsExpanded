# OpenGL PipelineStateObject (PSO) — Phase 3 (GlStateCache + Apply/Diff)

This document is the Phase 3 source-of-truth for the OpenGL state cache used by the PSO emulation.

## GlStateCache (overview)

Source:

- `VanillaGraphicsExpanded/Rendering/GlStateCache.cs`
- `VanillaGraphicsExpanded/Rendering/GlStateCache.Bindings.cs`
- `VanillaGraphicsExpanded/Rendering/GlStateCache.Legacy.cs`

Responsibilities:

- Track PSO “fixed-function knobs” and skip redundant GL calls when re-applying similar pipelines.
- Provide cached bind helpers and RAII scopes for dynamic bindings (program/VAO/FBO/buffers/textures/samplers/etc).
- Centralize “dirtying rules” when external code is known to stomp state (especially indexed MRT blending).

## Apply algorithm and ordering

Entry point:

- `GlStateCache.Apply(in GlPipelineDesc desc)` (`VanillaGraphicsExpanded/Rendering/GlStateCache.cs`)

Ordering is intentionally consistent and predictable:

1. Enables/disables (`DepthTest`, `CullFace`, `ScissorTest`, `Blend`)
2. Depth func + depth write mask
3. Global blend func
4. Indexed blend enable (MRT)
5. Indexed blend func (MRT)
6. Color mask
7. Optional debug-only: line width, point size

## Cached setters (fixed-function knobs)

`GlStateCache` implements cached setters for the Phase 0/2 knob list:

- Depth: enable, `DepthFunction`, write mask
- Blend (global): enable + `GlBlendFunc` (separate RGB/alpha)
- Blend (indexed / MRT): per-attachment enable + `GlBlendFunc` (`glBlendFuncSeparatei`)
- Cull: enable
- Scissor: enable
- Color mask: packed RGBA mask (`GlColorMask`)
- Optional: line width, point size

## Dynamic bindings and scopes

`GlStateCache.Bindings.cs` provides bind helpers and scope structs for:

- Program use
- VAO bind
- Program pipeline bind
- Framebuffer bind (combined + read/draw)
- Renderbuffer bind
- Transform feedback bind
- Active texture unit + texture bind (by target)
- Sampler bind (per texture unit)
- Buffer bind (by `BufferTarget`)

Important integration note:

- Binding operations are implemented as **always-bind** (best-effort) to avoid correctness risks when legacy code uses raw `GL.*` calls.
- Scope helpers query GL for “previous” state on entry so they restore correctly even if the cache was stale.

## Dirtying rules (external stomps)

`GlStateCache` supports targeted invalidation for MRT blending:

- `SetBlendFunc(...)` calls `DirtyIndexedBlendFunc()` because `glBlendFunc*` updates the blend func for all draw buffers.
- `Apply(...)` dirties indexed blend enable when toggling global blend enable.
- `GBufferManager.ReapplyGBufferBlendState()` explicitly dirties indexed blend enable/func before re-applying:
  - `VanillaGraphicsExpanded/GBuffer/GBufferManager.cs`

If a broader unknown stomp occurs, use:

- `GlStateCache.InvalidateAll()`

## Optional legacy scoped restore

For legacy paths that need a “save/restore” pattern:

- `GlStateCache.CaptureLegacyFixedFunctionState()` (`VanillaGraphicsExpanded/Rendering/GlStateCache.Legacy.cs`)

