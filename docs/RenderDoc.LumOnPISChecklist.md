# RenderDoc checklist: LumOn Product Importance Sampling (PIS)

Use this checklist to validate the Phase 10 PIS mask integration.

## Capture setup

- Enable LumOn and enable PIS:
  - `LumOn.Enabled = true`
  - `LumOn.EnableProbePIS = true`
- Start with a small, stable budget:
  - `ProbeAtlasTexelsPerFrame` small (e.g., 8–16)
  - `ProbePISExploreFraction` (e.g., 0.25) or `ProbePISExploreCount`
- Optional debug forcing (helps isolate behaviors):
  - `ForceUniformMask = true` to verify the mask path is being consumed
  - `ForceBatchSlicing = true` to force the legacy fallback path

## Confirm mask texture contents

- Locate `LumOn.ProbeTraceMask` (probe resolution; format `RG32F`).
- Spot-check a few probes:
  - The mask should have exactly `K = ProbeAtlasTexelsPerFrame` bits set.
  - The mask should be stable for a probe within a frame.
  - When `ForceBatchSlicing = true`, the trace should behave like legacy batch slicing.

## Confirm trace respects the mask

- Locate the probe-atlas trace pass output (`ScreenProbeAtlasTrace`).
- For a given probe tile (8×8 texels):
  - Texels selected by the mask should change (fresh trace results).
  - Texels not selected should be preserved (copied from history).

## Confirm temporal respects the same selection

- Locate the temporal pass output (`ScreenProbeAtlasCurrent`).
- For texels not selected this frame:
  - The temporal pass should not blend new trace data.
  - Velocity reprojection (if enabled) should still work for “not traced” texels.

## Quick sanity debug views (optional)

- Use the debug overlay modes:
  - “Probe-Atlas PIS Trace Mask” (selected vs preserved)
  - “Probe PIS Energy” (per-probe importance energy heatmap)
