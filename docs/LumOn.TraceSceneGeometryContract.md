# Shared TraceScene geometry contract

The later [bounded coverage implementation](LumOn.SurfaceCache.GeometryCoverage.md) extends the
surface domain to 192 blocks by default, supports up to 256 and exposes live bounded work settings.
Its settings and cost table supersede the original 128-block coverage/default limits below; the
historical consolidation measurements remain unchanged.

This is the implementation contract for [the consolidation task list](LumOn.TraceSceneGeometryConsolidation.todo). It defines the shared backend contract; the implementation and measured costs are recorded below. The approved conversation requires one geometry pipeline, the fixed 48-block NearField domain, preserved surface-cache consumers, worker capture, explicit validity and unchanged world-probe scheduling.

## Consumer inventory

Paths below are relative to `VanillaGraphicsExpanded/`. Existing behavior was inspected directly; older design documents are background, not additional controlling requirements.

| Retained consumer | Current source | Required data and coverage | Target validity rule |
| --- | --- | --- | --- |
| Screen-probe offscreen tracing | `assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh` | Block geometry inside the fixed NearField window; material identity at a hit | Unknown data or an incomplete segment cannot authorize cache handoff |
| Screen-probe hit lighting and its sky rays | `shaders/includes/lumon_near_field_hit_lighting.glsl` under the same assets directory | Hit-face diffuse/emission, normalized RGB block light and sunlight in the adjacent outside cell; bounded secondary geometry queries | Missing lighting/material data leaves a geometric hit intact but lighting unavailable |
| World-probe sample visibility, including gather and debug views | `shaders/includes/lumon_worldprobe_visibility.glsl`, imported by `lumon_worldprobe.glsl` | Geometry only; same fixed NearField domain | Accept only a fully traversed clear segment; reject unknown/unsupported/budget exits |
| Surface-cache material capture | `shaders/lumonscene_capture_voxel.csh`; `LumOn/Scene/LumonSceneFeedbackUpdateRenderer.cs` | Existing L0 domain, per-face surface IDs for each occupied source block | Missing geometry publication or face material must not mark a captured page ready |
| Surface-cache relighting | `shaders/lumonscene_relight_voxel_dda.csh`; `LumOn/Scene/LumonSceneRelightUpdateRenderer.cs` | Existing L0 domain, geometry and existing packed-light/LUT shading inputs | Incomplete/unsupported rays supply no sample and no temporal weight; preserve lighting arithmetic for valid samples |
| Geometry, payload and scene-overview diagnostics | `lumon_debug_near_field_geometry.glsl`, `lumon_debug_tracescene.glsl`, `lumon_debug_scenes_overview.glsl`; `LumOn/LumOnDebugRenderer.cs` | Shared resources, logical consumer bounds and physical bounds | Display unknown separately from air; NearField viewer keeps its 48-block domain |
| World Cell Bounds and resource measurements | `DebugView/Views/VgeWorldCellBoundsDebugView.Partitions.cs` | Shared partition demand/readiness and source-specific coverage | Show one geometry owner, union counts and NearField versus surface demand |

The CPU world-probe integrator continues to read the game world. It does not become a consumer of this GPU storage. Surface-cache sampling into probe hit lighting remains separate work; preserve the current relight producer and preview consumers.

The pre-consolidation inventory confirmed that capture, relight and debug sampled `OccupancyLevels[0]` only. Higher levels were written by `lumonscene_trace_scene_region_to_clipmap.csh`, but no current sampling consumer reads them. Their representative-point downsampling is not a conservative occupancy hierarchy. Retain L0 only in the shared backend; do not use the old coarsest-level window to select source captures. Deprecate `ClipmapLevels` for this backend with an explicit diagnostic; do not silently retain unconsumed allocations. This does not alter world-probe clipmap levels.

## Shared payload

Keep one world-zero grid of one-block voxels, published in 16-cubed cells. A 32-cubed game chunk is a source snapshot, not the GPU publication unit. The first implementation deliberately retains both lighting encodings to preserve existing shading arithmetic.

| Resource | Format | Contract |
| --- | --- | --- |
| Geometry | R32UI | Low two bits: 0 unavailable, 1 known air, 2 supported full opaque cube, 3 unsupported occupied shape. Remaining bits hold material identity; restrict to 1..16383 initially. Geometry classification never depends on material readiness. |
| Legacy lighting/material word | R32UI | Preserve block level bits 0..5, sun level 6..11, light ID 12..17, material index 18..31. Use the same collision-free material identity as Geometry. Zero payload does not establish air. |
| Normalized light | RGBA8 | Captured block-light RGB and sunlight A, preserving NearField clamping/quantization. Retain data in air voxels for outside-face lighting. |
| Publication readiness | R8UI per 16-cubed slot | Zero until all voxel resources and referenced material data are coherent. CPU retains logical owner, incarnation and revision. |
| Face surface IDs | RGBA32UI, 16384 entries | Six 16-bit surface IDs in three lanes. Fourth lane holds separate surface-data and hit-lighting readiness bits. Entry 0 is unavailable. |
| Hit materials | RGBA8, 12 texels per material | Six diffuse/emission pairs, preserving the existing NearField RGB representation and quantization. Diffuse alpha mirrors hit readiness so screen tracing stays within its 16 fragment samplers; face-table readiness remains authoritative for surface consumers. |
| Surface/light lookup tables | Existing formats | Keep surface LUT RGBA32UI, light-color LUT RGBA16F and block/sun scalar LUTs R16F for surface-cache shading. |

Replace block-ID masking with one stable collision-free scene material registry. A supported cube remains an opaque hit when its material cannot resolve. Material exhaustion returns unavailable material, never a different block's entry. Unsupported shapes may retain surface IDs for material capture, but remain unresolved for voxel ray intersection. This preserves the ability to capture their material without pretending their collision shape is a full cube.

Record material readiness independently: geometry may publish with an unresolved material, with flags preventing lighting/capture use. Resolved entries are immutable within a registry generation. Publish referenced entries before marking a cell ready. Material changes invalidate dependent cells and affected surface/screen histories; registry reset replaces the scene generation so old indices cannot alias. NearField hit colors and legacy surface IDs may become ready separately.

## Sampling and tracing API

Introduce a shared GLSL include and a matching CPU binding contract. Function names here define responsibility and outputs, not a requirement for trivial wrappers:

- `TraceSceneReadCell(worldCell, domain, out cell)`: returns `Ready`, `OutsideDomain`, `Unpublished` or `Unsupported`. The returned cell carries geometry classification, material ID and both lighting encodings. A material-only capture query may read a published unsupported cell's material while traversal still rejects that shape.
- `TraceSceneReadMaterial(materialId, face, out material)`: separate flags for surface ID and diffuse/emission availability. Never infer readiness from a nonzero index alone.
- `TraceSceneTrace(startCell, fraction, direction, maxDistance, maxSteps, domain)`: returns `Hit`, `Clear`, `Unavailable` or `BudgetExceeded`, plus the unavailable reason, hit cell/face/distance/material. `Clear` means the requested finite segment completed through published supported air. An occupied starting cell is an immediate hit. Preserve NearField's tied-boundary handling.

Domains are NearField or SurfaceCache. Check domain bounds before physical-ring lookup, then cell ownership/readiness, then geometry. Use signed integer world coordinates and a small fractional origin; perform wrapping before converting to float. Surface material capture has an explicit published-material read, rather than interpreting a traversal rejection as air.

Surface relight uses its existing finite step budget and valid-hit lighting arithmetic. At a window exit, unavailable cell or exhausted budget, skip that ray's sample. If no valid samples remain, do not increment the irradiance weight. Invalidate affected history when geometry changes, so skipping cannot preserve known-stale lighting indefinitely. There is no implicit sky outside the known domain. A future explicit sky-visibility policy is separate work; this consolidation must not claim to have established infinite visibility from a finite clear segment.

## Coverage and ring addressing

Let `a = floor(cameraWorld)` and `c = floor(cameraWorld / 16)`, componentwise. Keep both logical domains:

- NearField: `[16*(c-1), 16*(c+2))`, exactly 48 blocks per axis, without prefetch/retention. Clip loading demand to valid world height. Traversal still checks this domain even when shared storage contains geometry beyond it.
- SurfaceCache: when its producer is enabled, retain the existing L0 box `[a-N/2, a+N/2)` for sanitized `ClipmapResolution N` (16, 32, 64 or 128). This is existing trace coverage, not the larger surface-page residency radius. Out-of-domain pages remain explicitly unable to complete capture/relight; do not expand to the full near/far page radii in this work.

One WorldPartition registration owns the union of intersecting 16-block cells. The two sources target that same registration. Overlap produces one capture dependency and one physical publication. Both sources are required; NearField receives higher service priority, with minimum progress for eligible surface-only demand. No extra retained/prefetch fringe is added.

Use one contiguous ring. With surface coverage enabled, allocate `R = max(48, N+16)` blocks per axis; otherwise allocate 48. Align its origin down to 16 at the minimum of the two logical boxes. The extra allocation accounts for cell alignment of the moving L0 box; it is not additional ray coverage. Select only cells intersecting the source union, not every allocated slot. For N=16/32, the existing L0 box fits in the NearField box. For N=64/128 the aligned union fits within N+16. This formula must be swept across all cell offsets on every axis in implementation tests.

Physical voxel texel is positive-modulo(worldCell, R); publication slot is positive-modulo(floor(worldCell/16), R/16). R need not be a power of two. Check source bounds before lookup. Window movement preserves overlapping logical cells; retire and clear departing slot readiness before exposing new owners. Enabling/disabling surface coverage or changing N reallocates storage through a new registration generation; pending old completions cannot publish. If neither consumer is enabled, retire the registration.

## Resource limits and cost model

Initial operating ceilings, to be checked by measurements before final acceptance:

- Two existing chunk workers; at most two new 32-cubed captures per update, eight outstanding source captures including cancelled work awaiting completion.
- At most eight completed source snapshots retained by a bounded cache. Pin snapshots until all currently requested dependent subcells have their immutable publication payload; then evict by recency. Coalesce both consumer requests by world/chunk identity/version/material generation. Do not re-read merely because the second consumer requests the same current overlapping data.
- Shared geometry partition: at most `(R/16)^3` resident slots, 64 in-flight cell requests, 64 capture admissions and 32 processing dispatches per update, at most 16 cell publications and 8 MiB actual upload input per update. Existing global coordinator limits still apply.
- When both queues are continuously eligible, reserve at least one source-capture opportunity and one cell-publication opportunity for surface-only work per update; give NearField the remaining opportunities first. Unused reservations are borrowable. WorldPartition remains the shared-budget arbiter. Material/table uploads count against the same byte ceiling and cannot permanently block a feasible cell upload.

Native GPU-ready upload arrays are two uint words and four normalized bytes per voxel: 12 bytes. A 16-cubed publication costs 49,152 bytes plus readiness writes. Sixteen cost 786,432 bytes before tables. A 32-cubed packed snapshot is 393,216 bytes; eight retained snapshots are 3,145,728 bytes of payload. Bulk-read input arrays, temporary decoding, worker allocations and cell publication staging are additional and must be counted separately in measurements. Sixty-four staged cell payloads are at most another 3,145,728 bytes. These are payload ceilings, not total process memory.

| Nominal GPU storage | Bytes |
| --- | ---: |
| NearField-only shared voxels, 48 cubed at 12 bytes | 1,327,104 |
| Default shared voxels, 144 cubed at 12 bytes | 35,831,808 |
| Default publication readiness, 9 cubed | 729 |
| Shared material face IDs plus hit colors | 1,048,576 |
| Existing 65536-entry surface LUT | 1,048,576 |
| Light-color and two scalar LUTs | 644 |
| Current separate voxel volumes: three 128-cubed R32UI levels plus 48-cubed NearField at 8 bytes | 26,050,560 |

The shared voxel allocation is therefore 9,781,248 bytes larger at defaults. This choice prioritizes exact existing L0 coverage, cell-granular publication and unchanged lighting inputs; consolidation is not claimed to reduce VRAM. It removes duplicated capture/publication paths and unconsumed coarse-level work. Compressing the dual lighting representation is a later optimization requiring lighting equivalence evidence. Record table/readiness costs separately from voxel-only comparisons; surface-cache page atlases are unchanged and excluded on both sides.

If allocation or shared resident capacity is insufficient, expose capacity shortfall and unavailable data; do not silently crop required bounds or alias slots. Preserve configuration ceilings, and report the retired TraceScene level setting rather than secretly increasing source demand.

## Ownership and implementation acceptance

WorldPartition owns union residency, revisions, cancellation and authorization. TraceScene owns the single snapshot provider, material registry, GPU backend and sampling bindings. Capture/relight queues and screen-probe integration stay with their consumers. CPU workers use bulk solid/fluid/light reads and immutable snapshots; GPU writes and readiness acknowledgements stay on the render thread.

Introduce the shared backend behind testable interfaces before switching consumers. Temporary compatibility bindings may exist during switching, but must delegate to one shared owner when active. Remove the old NearField volume/provider and optional TraceScene companion payload after parity is verified. No changes to world-probe scheduling or CPU tracing are part of this contract.

Required subsequent evidence: payload round trips and material readiness; full-offset coverage union including N=16/32/64/128, negative/large coordinates and clipped world height; overlap retention and teleport/dirty/reuse rejection; coherent GPU publication; screen/visibility and capture/relight shader tests; contention fairness and bounded source coalescing; startup/movement/edit cost comparisons. Capture the pre-change baseline before modifying runtime code. The original contract asserted no build, gameplay or performance result; subsequent implementation evidence is recorded below.

## Traceability and review

Item 1 inherits the task-list introduction, both item 1 bullets, its gate and the scope boundary. Those reference the approved conversation and no external normative documents. The inventory and payload/API sections satisfy consumer/data/validity requirements; coverage, limits and cost sections satisfy shared demand, levels, formats, costs and ownership requirements. Evidence is current source inspection plus explicit target decisions; implementation and runtime verification belong to items 2–4.

Second review checked the consumer bindings, material tables, current source windows and configuration range. It retained dual lighting to avoid a shading change, added a separate material-only read for unsupported shapes, accounted for L0 cell-alignment overhead, and made the VRAM increase explicit.

Delegated formula verification: `artifacts/geometry-contract/Verify-GeometryContract.ps1`, with receipt `artifacts/geometry-contract/verification.txt`, passed 333,216 assertions over 143,360 XYZ offset combinations. It covers NearField-only and N=16/32/64/128, all 16-cubed integer offsets at seven negative/zero/large anchors, ring injectivity/congruence, vertical clipping and 13 byte-count calculations. This verifies the proposed formulas, not production implementation, shader execution or runtime performance.

The independent completion audit read the complete task and contract and checked actual consumer bindings, topology, formats and material/lighting requirements. It found both item 1 requirements and the gate fully satisfied with no remaining blockers or material evidence gaps. Implementation, GPU verification and measured performance remain explicit obligations of items 2–4; no build is required for this documentation-only change.

## Implemented ownership and diagnostics

`LumOn/Scene/Geometry/TraceGeometryRenderer` is the composition root for the shared pipeline. `TraceGeometryWorldSource` submits worker bulk capture to `TraceGeometrySnapshotSource`; `TraceGeometrySourceCache` coalesces chunk revisions. `TraceGeometryPartition` owns one WorldPartition registration for both logical demands and publishes immutable cell payloads through `TraceGeometryGpuScene`. Geometry, both lighting encodings, lookup tables and readiness now have one GPU owner. The separate NearField provider/cache/volume and the old TraceScene region scheduler, companion upload buffers and scatter compute producer are removed.

Screen tracing and direct world-probe visibility use the NearField logical domain. Surface capture and relighting use the surface domain through shared sampling and bindings. Consumer lighting algorithms and CPU world-probe scheduling are unchanged. Shared bulk-reader, light-decoder and source-lifetime helpers live beside their owning geometry subsystem. A zero-byte secondary artifact cache now disables retention; the eight-snapshot source cache remains the intended completed-result owner.

NearField Geometry still traces only its 48-block logical box, even when the physical ring is larger. TraceScene bounds show logical surface coverage; occupancy and payload views distinguish unpublished data (purple), unsupported geometry (red) and out-of-domain samples (blue) from known air (black). The camera-ray TraceScene viewer uses the same bounded traversal. Surface reconstruction uses the integer/fractional frame bridge. Scene Overview uses these same geometry and material reads.

World Cell Bounds reports the shared registration, NearField and surface demand, overlap, actual ready cells, workers, retained source payload, staged cell payload, texture allocation, cumulative uploads and update timing. Requested coverage is never reported as ready data. Timing includes the coordinator pump but excludes metric serialization; it is not total frame cost. Texture bytes are nominal storage, excluding driver allocation overhead. Old TraceScene level configuration is explicitly reported as retired; only the logical L0 resolution controls surface geometry demand. Existing configuration serialization remains compatible.

Traceability: removal and diagnostics -> this document's consumer inventory, payload, coverage and ownership sections -> one backend and explicit logical-domain/readiness semantics -> shared backend/publication regressions, migrated screen/visibility fixtures, shared diagnostic GPU tests and output-asset inspection. Cost comparison -> resource limits and cost model -> startup/movement/edit source reads, actual uploads, nominal texture storage, separate worker/source/staging allocations and frame-thread work -> controlled before/after measurement fixtures and receipts recorded in the task list.

### Controlled implementation-cost comparison

The before/after fixtures exercise startup, a sixteen-block movement and a one-source-chunk edit. The baseline is commit `4a7ce2a` in the isolated comparison worktree, before consumer consolidation; it retains both old owners. Default surface resolution is 128. These are deterministic source/publication measurements with synthetic geometry, not observed gameplay frames. Removing unused coarse levels intentionally reduces demanded coverage; this is not an equal-volume throughput benchmark.

| Scenario | Old source captures | Shared captures | Old uploaded bytes | Shared uploaded bytes |
| --- | ---: | ---: | ---: | ---: |
| Startup | 2,056 | 64 | 274,423,862 | 27,265,024 |
| Move 16 blocks | 2,180 | 16 | 286,620,187 | 3,145,920 |
| Edit one source chunk | 2 | 1 | 786,764 | 393,240 |

Nominal allocated texture storage, queried from actual GL textures, increases from 28,148,383 to 37,930,333 bytes. The old 131,588-byte staging-buffer allocation is retired. Shared retained source payload is 3,145,728 bytes; old unique cached payload grows from 72,351,744 at startup to 75,628,544 after the edit. Shared staging sampled after updates was zero, which does not imply zero transient allocations; the separate 64-cell payload ceiling remains 3,145,728 bytes. Eight real production worker captures and all sixteen chunk-identity lookups occurred off the submitting thread. Worker allocation, including pool warmup, was 1,598,248 bytes; returned source payload was 3,145,728 bytes.

Equal-coverage NearField-only fixtures retain the same 48-cubed window and 8/4/1 source captures. Upload bytes fall from 5,357,622 / 737,307 / 655,384 to 3,425,334 / 442,395 / 393,240, while textures increase from 1,671,195 to 3,424,927 bytes.

Single-run cumulative frame-thread observations (startup/move/edit) were 40.738/4.259/1.866 ms for old NearField, 997.369/834.966/3.701 ms for old TraceScene, and 119.956/17.515/1.950 ms for shared default geometry. Old components were exercised separately; these numbers cannot establish a combined game-frame speedup. Resource construction and shader compilation are excluded. Old TraceScene main-thread capture accounted for 539.568/538.631/0.572 ms; shared capture is on workers. Shared cumulative frame-thread allocations remain substantial: 87,697,552 / 11,222,904 / 1,532,248 bytes. This is not a claim that frame-thread allocations or gameplay stalls have been eliminated.

Detailed methods, raw measurements and caveats: `artifacts/geometry-costs/comparison.md`, `measurement-method.md`, `legacy-cost-verified.trx` (2/2), and `current-cost-verified.trx` (5/5, including cache regressions). The current reusable fixtures are `SharedGeometryCostMeasurementsTests` and `SharedGeometryWorkerCostMeasurementsTests`. Startup readiness reached all 512 requested shared publication cells; requested coverage and actual ready counts are asserted separately.
