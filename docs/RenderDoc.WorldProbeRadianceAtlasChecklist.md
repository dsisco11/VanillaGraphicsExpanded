# RenderDoc checklist: World-probe radiance atlas

Use this as a quick sanity checklist when validating the octahedral radiance atlas (Phase 6).

## Capture setup

- Enable world probes and ensure `VGE_LUMON_WORLDPROBE_ENABLED=1` is active.
- Use a small, deterministic config first (e.g. `levels=1`, `resolution=8`, `octTileSize=16`, `atlasTexelsPerUpdate=32`).
- Capture frames across several updates (direction slicing means a single probe tile fills over time).

## Validate atlas dimensions

- Radiance atlas dimensions match packing math:
  - $W = (N \cdot N) \cdot S$
  - $H = (N \cdot levels) \cdot S$
- Scalar atlases (Vis/Dist/Meta/DebugState) match:
  - $W = N \cdot N$
  - $H = (N \cdot levels)$

## Validate tile placement (level stacking)

- For a probe storage index `(x,y,z)` at clipmap level `L`:
  - `tileU0 = (x + z*N) * S`
  - `tileV0 = (y + L*N) * S`
- Verify adjacent `z` slices increase U by `N*S`.
- Verify level `L+1` increases V by `N*S`.

## Validate signed-alpha semantics

- Radiance atlas alpha stores signed log distance:
  - Hit: `+log(dist+1)`
  - Sky-visible/miss: `-log(maxDist+1)`
- Spot-check a few texels:
  - Negative alpha should correlate with “sky visible” directions.
  - Positive alpha should correlate with geometry-hit directions.

## Sanity-check sampling

- In `lumon_probe_atlas_trace.fsh`, force a ray miss (sky depth) and confirm:
  - world-probe fallback path triggers.
  - the sampled radiance comes from `worldProbeRadianceAtlas` when confidence > 0.
