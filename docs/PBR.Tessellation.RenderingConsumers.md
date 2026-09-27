# Displaced terrain rendering consumers

The explicitly selected Tessellation mode prepares adaptive stages for opaque terrain, topsoil and
the terrain shadow program. Disabled and Relief retain their existing identity-tessellation setting.
No legacy configuration migration is performed.

## Publication and resources

All three adaptive executables must be installed before any of them displaces geometry. Registration
matches the managed owner and program ID; reload clears family readiness and disposal removes the
owner. A failed candidate retains its ordinary executable. Other installed adaptive candidates render
factor-one, undisplaced geometry until the complete family is available.

The engine's uniform collection sees the displacement interface in its patched vertex source. Runtime
TCS/TES linking uses the existing compiled engine stages and GPU shader abstractions. Capabilities
come from GpuSupport, including patch, subdivision and stage texture limits.

Terrain atlas setters, including the shadow atlas setter, bind a coherent tile-index/record/height
trio through the existing material binding owner. Missing pages bind zero metadata and neutral height.
Opaque/topsoil share the same authored amplitude, tile-boundary pinning, LOD-zero height sampling and
distance fade with their shadow draws. Full-height relief is disabled in Tessellation mode.

## Shadow routing and geometry

The engine reuses main and shadow programs for several pools. Both ChunkRenderer.RenderOpaque and
RenderShadow scope manager identity to opaque and topsoil entries (engine enum values 0 and 5);
other pools retain undisplaced geometry in both passes.
Mini-dimension pools are excluded in both main and shadow draws: their transformed geometry needs a
separate common-coordinate policy. Scopes restore state on exceptional exits.

Subdivision uses a common angular edge estimate from shared camera-relative endpoints and a focal
pixel scale captured before the passes. The estimate is conservative toward screen edges and is
bounded by the configured maximum. The light projection cannot select a different subdivision level.
The perspective metric may lag a projection change by one frame, but both passes use the same value.

The evaluation stage updates raster position, camera position and available G-buffer normals.
Main terrain uses the original engine cascade-coordinate function, including cascade weights, on
the displaced position. Raster position retains the engine's interpolated clip coordinates and adds
only the projected displacement vector. Opaque terrain adds the change in its authored clip-w bias;
neutral height and pinned edges retain the original biased coordinates. Shadow terrain uses its
engine MVP and does not receive the main-pass decor offset. Zero displacement skips those transforms.

Pool admission expands all stored culling extents by the maximum authored displacement (0.05 m),
including models uploaded before enabling detail. This conservative allocation-time allowance avoids
per-frame bounds mutations and survives live mode changes. Engine chunk connectivity/occlusion and
gameplay collision remain coarse; microgeometry does not open visibility through a solid block or
change selection/collision shapes.

## Temporal behavior

Static displaced depth follows the existing depth-reconstruction and previous-origin reprojection
path. Adaptive subdivision, distance fades and streamed height changes have no previous-height
representation. When camera position, subdivision inputs, atlas publication/readiness or shader
generation changes, samples in eligible adaptive terrain draws set bit 16 in PatchId.w. This includes
neutral, fully faded and missing-material destinations: the previous frame may have been displaced.
Low 16-bit slot generations are unchanged. Stable frames resume ordinary history reuse; ineligible
draws and the ordinary/identity paths do not set the marker.

Atlas readiness transitions and each metadata publication advance a monotonic surface-detail
revision. The frame history compares revisions as well as readiness, so a rebuild completed between
snapshots is still detected. First draw binding also checks for publication after the view snapshot.

The velocity pass marks those pixels as unsuitable for temporal reuse. Probe temporal blending
rejects their history even when velocity-coordinate remapping is disabled. Untraced directions retain
their diagnostic radiance but lose confidence until retraced. The Surface Cache is not cleared.
Once geometry inputs stabilize and the atlas is complete, temporal reuse resumes. This conservative
policy also rejects history on unchanged materials within eligible terrain draws during reactive
frames. Distinguishing them from surfaces whose metadata disappeared would require previous-material
or previous-height history. There is no added GPU history allocation or per-texture scan.

## LumOn representation

The G-buffer preserves undisplaced base position and geometric normal specifically for voxel PatchId
and UV addressing. Displacement cannot change ownership to a neighboring block or a different axis
because of a tilted shading normal. Raster depth and normals still describe the visible surface.

Surface Cache voxel capture and mesh-card capture retain their existing coarse geometry; CPU/GPU
world traces likewise retain their existing block/mesh representation. Tessellation does not insert
microtriangles into those structures. Screen-space visibility sees displaced depth, while offscreen
visibility, indirect self-shadowing and cache lighting approximate the underlying surface. Details
up to the 0.05 m amplitude bound can therefore disagree with offscreen GI. Both standalone PBR and
LumOn-compatible main/shadow shader families use the same displacement policy.

## Validation

Focused headless checks cover installed main/shadow linkage, family readiness and reload, pool
provenance, conservative bounds, displaced raster depth, cascade coordinates, base-face addressing
and temporal rejection. Live appearance and production GPU cost remain the following validation task;
no game was launched for this implementation.

Build and 74 distinct focused tests passed across `artifacts/PbrColor/terrain-displacement-production.trx`,
`terrain-displacement-consumers.trx`, `terrain-displacement-temporal-current.trx` and
`terrain-displacement-final-policy.trx`. Earlier failures in intermediate receipts were superseded by
the consumer and temporal reruns. Final main/shadow pool-scope correction passed all 11 policy tests in
`terrain-displacement-pool-scope.trx`. These include exact installed hook targets and field types.
The original frame-to-frame reactive snapshot was reviewed in source; subsequent history-policy
tests cover its transitions as described below.

Validation also exposed a build-tool path error: custom-output builds could launch a stale default
ShaderBuildTool executable. SpirvBuild now resolves and runs the actual built target, and the final
custom-output build verifies the velocity shader's new integer sampler uses its declared binding.

### Correctness follow-up

`artifacts/PbrColor/terrain-detail-fixes.trx` records **147 passed, zero failed, zero skipped**
(22 seconds test duration), with a successful fresh shader build. Coverage includes neutral/faded/
missing/zero-amplitude history transitions, stable and ineligible draw controls, complete-to-complete
atlas revisions, incomplete-to-ready settling, movement, settings and reset. The publication call
sites and readiness transitions were reviewed separately from the history-policy unit tests.

The original raster-depth threshold was below even identity tessellation's raster rounding error
(1.67e-6 versus a 1e-6 threshold), so that failure did not independently establish visible depth
corruption. The replacement uses TinyAst to expose the actual TES clip-coordinate delta before
rasterization: neutral, faded and identity cases preserve all four components exactly, while a
non-neutral control verifies displacement and the corresponding bias change. Existing raster,
cascade, velocity, temporal blending and PBR/LumOn integration checks also pass.

No performance benchmark or game was run for these fixes. Default enablement remains unchanged;
representative production cost and user-run appearance verification are still separate acceptance work.
