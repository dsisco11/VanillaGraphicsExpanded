# OpenGL PipelineStateObject (PSO) — Phase 0 (Inventory + Baseline Defaults)

This document is the Phase 0 source-of-truth for the initial PSO state inventory and the meaning of “baseline defaults” used by `defaultMask`.

## Baseline Default Semantics (Phase 0 decision)

Phase 0 decision: **baseline defaults are OpenGL specification defaults** for the tracked knobs listed below.

Notes:

- `defaultMask` means “force GL-spec default”, not “restore Vintagestory’s current baseline”.
- Any integration that must be safe with engine code should still use scope APIs / snapshot+restore (via the future `GlStateCache`) to return to the previous engine state.

Baseline defaults (GL spec):

| Knob | Baseline default |
| --- | --- |
| Depth test | Disabled |
| Depth func | `LESS` |
| Depth write mask | `true` |
| Blend (global) | Disabled |
| Blend func (global) | `src=ONE`, `dst=ZERO` (and same for alpha) |
| Blend equation (global) | `FUNC_ADD` |
| Blend (per-RT / indexed) | Disabled (per attachment) |
| Blend func (per-RT / indexed) | `src=ONE`, `dst=ZERO` (and same for alpha) |
| Blend equation (per-RT / indexed) | `FUNC_ADD` |
| Cull face | Disabled |
| Color mask | `RGBA = true,true,true,true` |
| Scissor test | Disabled |
| Line width (optional debug knob) | `1.0` |
| Point size (optional debug knob) | `1.0` |

Rationale: VGE already has multiple “force known-good state + restore” callsites for offscreen/debug paths. Using spec defaults makes the `defaultMask` semantics unambiguous and aligns with those callsites (e.g. `MaterialAtlasNormalDepthGpuBuilder` disables `Blend/DepthTest/Scissor/Cull` and forces `ColorMask` to all-true).

## PSO State Knobs (initial tracked set)

This list is intentionally limited to knobs already exercised by current VGE render paths.

- Depth test enable, depth func, depth write mask
  - Used in: `VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs`, `VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs`, `VanillaGraphicsExpanded/DebugView/VgeBuiltInDebugViews.cs`
- Blend enable + blend factors (global; include separate RGB/alpha)
  - Used in: `VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs`, plus engine-level toggles via `capi.Render.GlToggleBlend(...)`
- Per-render-target (indexed) blend enable + blend factors (MRT)
  - Used in: `VanillaGraphicsExpanded/GBuffer/GBufferManager.cs` (attachments 4–5), including the “reapply after global stomp” behavior
- Cull enable
  - Used in: `VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs`, `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs`
- Color mask (RGBA)
  - Used in: `VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs`
- Scissor enable (scissor box remains dynamic unless explicitly included)
  - Used in: `VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs`, `VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs`
- Optional (debug-only): line width, point size
  - Used in: `VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs`

## Dynamic State (set outside PSO)

Dynamic state is not part of the immutable PSO descriptor, but should still be routed through future `GlStateCache` bind/scope APIs so redundant calls can be skipped and restore/debug tracking can be centralized.

- Viewport
- Scissor box (if scissor is enabled dynamically)
- Framebuffer binds (draw/read), plus draw buffers
- Program use
- VAO bind
- Buffer binds (VBO/IBO/UBO/SSBO) and buffer contents
- Active texture unit + texture binds + sampler binds
- Uniforms / UBO updates

## Deferred / Out of Scope (Phase 0)

Not currently tracked because VGE isn’t explicitly controlling them today (can be added later if a renderer needs them):

- Stencil test and stencil ops
- Polygon offset and depth range
- Alpha-to-coverage / multisample toggles
- sRGB framebuffer enable, dithering, logic op
- Clip control, provoking vertex, primitive restart

