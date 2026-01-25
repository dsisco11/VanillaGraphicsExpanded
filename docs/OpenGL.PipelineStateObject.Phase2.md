# OpenGL PipelineStateObject (PSO) — Phase 2 (Value Payloads)

This document is the Phase 2 source-of-truth for non-default state value storage in the OpenGL PSO emulation.

## Value Payload Types (compact storage)

Phase 2 introduces compact, immutable value types used only when the corresponding intent bit is present.

- Global blend factors: `GlBlendFunc` (`VanillaGraphicsExpanded/Rendering/GlBlendFunc.cs`)
  - Stores separate RGB/alpha factors.
  - Baseline default is `ONE/ZERO` for both RGB and alpha (`GlBlendFunc.Default`).
- Indexed (MRT) blend factors: `GlBlendFuncIndexed` (`VanillaGraphicsExpanded/Rendering/GlBlendFuncIndexed.cs`)
  - Sparse list of `(attachmentIndex, blendFunc)` pairs.
- Color write mask: `GlColorMask` (`VanillaGraphicsExpanded/Rendering/GlColorMask.cs`)
  - Packs RGBA enables into the low 4 bits of a byte (`0bRGBA`).

## PSO Descriptor Payload Fields

`GlPipelineDesc` is the Phase 2 descriptor that combines intent masks and optional value payloads:

- Source: `VanillaGraphicsExpanded/Rendering/GlPipelineDesc.cs`
- Intent: `DefaultMask` and `NonDefaultMask` (see Phase 0/1 docs for baseline semantics and bit layout)

Value payload fields (only required when referenced by the masks):

- Depth
  - `DepthTestEnable`: intent-only (bit)
  - `DepthFunc`: value payload `DepthFunction`
  - `DepthWriteMask`: value payload `bool`
- Blend (global)
  - `BlendEnable`: intent-only (bit)
  - `BlendFunc`: value payload `GlBlendFunc` (separate RGB/alpha)
- Blend (indexed / MRT)
  - `BlendEnableIndexed`: sparse attachment list `BlendEnableIndexedAttachments` (`byte[]`)
  - `BlendFuncIndexed`: sparse list `BlendFuncIndexed` (`GlBlendFuncIndexed[]`)
  - Note: Phase 2 keeps the attachment lists separate per knob to avoid forcing a single “MRT table” shape too early.
- Output write masks
  - `ColorMask`: value payload `GlColorMask`
- Scissor
  - `ScissorTestEnable`: intent-only (bit); scissor box remains dynamic
- Optional debug-only
  - `LineWidth`: value payload `float`
  - `PointSize`: value payload `float`

## Invariants + Validation (debug-focused)

Validation helper:

- `GlPipelineStateValidation.ValidateDesc(...)` (`VanillaGraphicsExpanded/Rendering/GlPipelineStateValidation.cs`)

Checks:

- No overlap between `DefaultMask` / `NonDefaultMask`
- No unknown bits in masks
- Required payload fields are present when their bits are set
  - Indexed blend knobs require attachment indices even when forcing baseline defaults
- Indexed attachment lists must be sorted/unique (the descriptor normalizes order and validation rejects duplicates)

## Binding Ownership (Phase 2 decision)

Phase 2 decision: **PSO does not own GL object bindings** (program/VAO/FBO/textures/buffers).

- The PSO descriptor is a *non-owning* immutable description of fixed-function state.
- GL object lifetimes remain owned by existing VGE wrappers/resource managers.
- Binding state is treated as dynamic and will be routed through `GlStateCache` scope APIs in Phase 3.

