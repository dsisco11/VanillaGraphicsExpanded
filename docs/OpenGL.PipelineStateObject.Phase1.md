# OpenGL PipelineStateObject (PSO) — Phase 1 (State ID Layout + Bitsets)

This document is the Phase 1 source-of-truth for the stable bit layout used by PSO intent masks.

## Stable ID → Bit Index Mapping

State knobs are assigned stable bit indices via `GlPipelineStateId`:

- Source: `VanillaGraphicsExpanded/Rendering/GlPipelineStateId.cs`
- Rule: append-only; do not reorder/renumber existing entries.

### Initial tracked knob set (Phase 1)

| `GlPipelineStateId` | Bit | Meaning |
| --- | ---: | --- |
| `DepthTestEnable` | 0 | `EnableCap.DepthTest` |
| `DepthFunc` | 1 | `GL.DepthFunc(...)` |
| `DepthWriteMask` | 2 | `GL.DepthMask(...)` / `capi.Render.GLDepthMask(...)` |
| `BlendEnable` | 3 | `EnableCap.Blend` |
| `BlendFunc` | 4 | `GL.BlendFunc(...)` / `GL.BlendFuncSeparate(...)` |
| `BlendEnableIndexed` | 5 | `GL.Enable/Disable(IndexedEnableCap.Blend, i)` |
| `BlendFuncIndexed` | 6 | `GL.BlendFunc(i, ...)` / `GL.BlendFuncSeparate(i, ...)` |
| `CullFaceEnable` | 7 | `EnableCap.CullFace` |
| `ColorMask` | 8 | `GL.ColorMask(...)` |
| `ScissorTestEnable` | 9 | `EnableCap.ScissorTest` |
| `LineWidth` | 10 | `GL.LineWidth(...)` (debug-only) |
| `PointSize` | 11 | `GL.PointSize(...)` (debug-only) |

## Bitset Representation

Phase 1 uses `ulong` for masks:

- `GlPipelineStateMask` (`VanillaGraphicsExpanded/Rendering/GlPipelineStateMask.cs`) wraps a `ulong` bitset.
- `GlPipelineDesc` (`VanillaGraphicsExpanded/Rendering/GlPipelineDesc.cs`) is the Phase 1 PSO descriptor skeleton with:
  - `DefaultMask` (force GL-spec baseline defaults)
  - `NonDefaultMask` (force explicit non-default values stored in the PSO in Phase 2)

If/when the tracked knob count exceeds 64, the mask representation must be upgraded (e.g., a small fixed `ulong[]`).

## Invariants + Debug Validation

PSO intent invariants (enforced via debug checks today, extended in Phase 2):

- `defaultMask & nonDefaultMask == 0`
- `nonDefaultMask ⊆ valuesPresentMask` (all non-default states must have a value payload)

Validation helper:

- `GlPipelineStateValidation.ValidateMasks(...)` (`VanillaGraphicsExpanded/Rendering/GlPipelineStateValidation.cs`)

