# WorldPartition coverage and lifecycle proposal

Status: approved. This document defines changes to our WorldPartition and its consumers; it does not record completed implementation.

## Purpose

Turn the existing WorldPartition cell registry into a shared coordinator for world-aligned spatial partitions. Multiple partition instances will register independent layouts, coverage requirements, and content providers. WorldPartition will determine which cells are required, schedule their lifecycle work, and track whether their contents are actually ready.

The first consumer will be near-field voxel geometry. The immediate objective is consistent coverage around tracing origins as the player moves, with incremental capture and GPU publication. Subsequent consumers will reuse the same spatial residency machinery while retaining their domain-specific update algorithms.

## Current code and problem

The current components provide useful foundations but divide ownership across consumers:

- `LumOn/WorldCells/WorldPartitionSystem.cs` registers cell objects and queries their desired or actual states.
- `WorldPartitionModSystem` owns the shared registry.
- `IWorldCell`, `WorldCellStateMachine`, and `WorldCellWindowHysteresis` provide state, transition, and retention helpers.
- `LumonSceneRegionScheduler` manages near-scene cell transitions, capture, and relighting queues.
- `TraceSceneRegionScheduler` manages tracing-region work, retries, and window membership.
- `LumonSceneOccupancyClipmapUpdateRenderer.NearField.cs` attaches near-field geometry publication to the occupancy scene's snapshot stream and derives local volume size from that scene's resolution.
- `LumOnWorldProbeScheduler` separately owns probe-level coverage, ring movement, and lighting-update selection.

`WorldCellKey` currently identifies a cell by a fixed kind and a kind-specific packed value. It cannot independently identify several registered partitions of the same kind. Transition contexts also expose chunk-oriented windows and integer camera positions rather than a general partition layout and source description.

`NearFieldGpuScene.Prepare` currently snaps its window to 32-block boundaries. With a 64-block volume, player coordinates 0 through 31 select the interval [-32, 32) along that axis. At coordinate 31, only one block remains toward the positive boundary. Crossing coordinate 32 shifts the interval to [0, 64). This is an asymmetric coverage policy, not a reason to abandon world-aligned cells.

The new system must keep cell boundaries fixed while selecting enough cells to cover the required area around moving sources. It must distinguish desired coverage from ready coverage: choosing a cell does not make its geometry available.

This coverage defect is established by the current coordinate calculation. It is not confirmation that every unresolved trace in the affected indoor scene has the same cause.

## Scope and ownership

| Responsibility | Owner after migration |
| --- | --- |
| Partition registration and lifetime | WorldPartition |
| Stable logical cell identity and desired residency | WorldPartition |
| Coverage evaluation, source aggregation, prefetch, and retention | WorldPartition using each partition's policy |
| Transition scheduling, cancellation, retry eligibility, and stale completion rejection | WorldPartition |
| Cell layout and content dependency description | Registered partition |
| Capturing source data and producing cell content | Partition content provider |
| GPU slot allocation, upload, and sampling layout | Partition GPU backend |
| Publication authorization and ready-state bookkeeping | WorldPartition, after backend acknowledgement |
| Relighting, probe importance, directional sampling, and temporal history | Existing domain-specific consumer |
| Source chunk availability | Game world; adapters observe availability without forcing chunk loads |

WorldPartition will coordinate spatial residency. It will not become a general replacement for every rendering work queue or own OpenGL calls, material evaluation, voxel traversal, or probe integration.

The initial implementation supports axis-aligned regular grids, with different cell sizes per partition and all grids anchored at world-coordinate zero. Offset grid origins are not supported. It does not require arbitrary spatial trees, disk persistence, or a new general-purpose job execution framework.

## Registration, identity, and layout

Add explicit registration of partition instances. A registration supplies:

- A unique runtime instance identifier and a stable diagnostic name.
- Its world/dimension scope.
- A positive cell extent on each axis. Grid origin is fixed at world-coordinate zero and is not a registration parameter.
- Its coverage and prioritization policies.
- A content provider and resource limits.

A logical cell key consists of the partition instance, world scope, and signed integer grid coordinate. Existing cell kinds become descriptive categories rather than the uniqueness boundary. Physical texture slots and packed game-chunk keys are adapter details and must not become logical identity.

Map each axis with `cellCoordinate = floor(worldPosition / cellExtent)`, including negative positions, and use half-open cell bounds `[cellCoordinate * cellExtent, (cellCoordinate + 1) * cellExtent)` consistently. The moving coverage window and GPU ring offsets do not change these fixed logical boundaries. Keep grid coordinates as integers and source positions at double precision. Convert to small relative floating-point coordinates only at rendering boundaries. Remove the assumption that every partition's cell center is representable by `CenterHalfBlockPos`.

The first near-field geometry layout will use 16-block cells. This is independent of the game's 32-block source chunks. The layout contract will also support other cell sizes; changing a registered layout replaces its registration generation and invalidates its old cells rather than reinterpreting existing coordinates.

Registration disposal cancels pending work, prevents further publication, and retires resources on their owning thread. World unload performs the same operation for all registrations in that world scope.

## Streaming sources and coverage

Introduce source records with a stable identity, world position, required influence bounds, priority, and partition targeting. The initial source follows the player/camera; the contract supports additional sources without baking a single camera into the coordinator.

Each partition converts the relevant sources into three sets:

1. **Required:** cells intersecting the area the consumer currently needs.
2. **Prefetch:** additional cells requested early to absorb motion and publication latency.
3. **Retained:** previously resident cells held within configured spatial/time hysteresis limits to avoid repeated unloading and loading.

Required cells target Active. Prefetched and retained cells target Loaded. Cells outside all three sets target Unloaded. Multiple sources combine by union, with Active taking precedence over Loaded. Lifecycle priority is distinct from these state decisions.

Coverage evaluation selects every cell intersecting the required bounds. It must not select only cells whose centers fall inside those bounds. Enumerate the necessary integer coordinate range and update entering/leaving cells when its bounds change; avoid rescanning every world cell each frame.

For near-field geometry, select the camera cell and its 26 immediate neighbors: a fixed 3-by-3-by-3 window of 16-block cells, totaling 48 blocks per axis. Do not expand this allocation for tracing distance, secondary rays, prefetch, or retention. Exclude cells outside the world vertical domain from loading demand. A ray leaving the known window before its requested segment completes remains unresolved; it does not authorize sky or world-probe cache reuse. Origins outside the window likewise remain unresolved.

Prefetch may incorporate movement direction, but it must preserve required coverage in all directions. Hysteresis may delay retirement; it must never delay requesting a newly required cell. Teleports immediately replace the required set and cancel obsolete requests.

A centered 64-block interval can intersect three 32-block cells along an axis, or five 16-block cells. Capacity must account for this alignment overhead plus prefetch and retained resources. The coordinator must report insufficient capacity instead of silently cropping the required set. Prefetched and retained cells are the first candidates for eviction under pressure.

## Lifecycle and publication contract

Retain the distinction between desired state and actual transition progress. Adapt the existing Loaded/Active state model so providers can acknowledge transitions rather than allowing consumers to independently mutate shared state.

Loaded means the provider's complete resident representation is available. For near-field geometry, this includes a coherent GPU upload, so prefetch does not defer that upload until the cell becomes required. Active additionally enables the consumer's active participation or update policy; it need not require another upload. Publication readiness is tracked independently from this Loaded/Active distinction. Dirty content and in-flight revisions are also separate from residency: an active cell can require rebuilding without being a new logical cell.

Every request and completion carries:

- Partition registration generation.
- Logical cell key and cell incarnation.
- Requested content revision.
- A request identity used to match the in-flight operation.

A completion may publish only when those identities still match, the cell is still wanted, and its source dependencies remain valid. Cancellation is advisory; correctness must also reject a late completion after cancellation, eviction, dirtying, world unload, or slot reuse. Cell incarnation prevents a removed and recreated key from accepting work from its previous lifetime.

Publication follows this order:

1. Capture a consistent source snapshot and record its dependency revisions.
2. Process the snapshot through the existing worker infrastructure where appropriate.
3. Revalidate the request and dependencies before uploading.
4. Upload into storage owned by that logical cell and generation.
5. Acknowledge readiness only after all required resources are coherent for the rendering consumer.
6. Release superseded resources according to the backend's ownership rules.

For near-field tracing, geometry, light, material references, and readiness must become usable coherently. A reused GPU slot must not expose its previous owner's data under the new cell identity. Dirty geometry becomes unavailable until its replacement is safely published; the partition must not retain known-stale occlusion merely to conceal a coverage gap.

Missing source chunks are a retryable dependency condition, not empty geometry. Unsupported block geometry is a content limitation, not a reason to endlessly retry an otherwise current snapshot. Both conditions remain distinguishable in diagnostics.

All coordinator state changes occur on one owning thread. Providers receive immutable requests and return completion records. Source capture obeys the existing game-access threading constraints, worker processing uses existing executors, and GPU publication/retirement remains on the render thread. Queue boundaries must not allow background work to mutate coordinator or GPU state directly.

## Budgets and fairness

Use separate limits for resident resources, in-flight work, source capture, processing dispatch, and GPU upload bytes. Keep per-partition caps together with shared limits so one expensive partition cannot starve all others.

Missing dependencies use exponential retry backoff capped at 64 coordinator updates; explicit dirty notifications reset the delay. Required missing cells receive priority over speculative prefetch. Within required work, allow partition priorities and source distance to contribute, with aging or minimum service guarantees to prevent starvation. Do not conflate a cell's lifecycle urgency with a probe's lighting importance.

Under sustained overload, expose required-versus-ready coverage and pending work explicitly. No generic policy can guarantee instantly ready coverage after a teleport or with insufficient memory. Prefetch and retention reduce ordinary movement gaps; they do not remove these resource constraints.

## Near-field geometry migration

Create a near-field geometry partition with an independent layout, coverage configuration, and publication backend. Remove ownership of its window from the occupancy clipmap renderer once this partition becomes authoritative.

Reuse the bounded chunk-processing workers and `NearFieldCellCapture` geometry evaluation. Near-field source capture runs on these workers, using the same world-access pattern as world-probe tracing. Bulk-copy solid, fluid and packed-light layers under their engine read locks, then decode lighting and evaluate geometry off the render thread. Keep GPU publication on the render thread. A 16-block publication cell occupies a subregion of a 32-block source chunk. Coalesce overlapping source capture requests so multiple publication cells do not independently read the same chunk unnecessarily. A chunk change invalidates each dependent publication cell, while processing and uploading remain cell-granular. Source availability and dependency revisions remain explicit.

Generalize `NearFieldGpuScene` from fixed 32-block regions to the partition's publication-cell size and allocated cell window. Update the near-field UBO and shader addressing together: current `>> 5`, multiplication by 32, and readiness texture dimensions encode the old region size. Preserve world-to-ring mapping and integer chunk plus fractional-origin precision.

Use a contiguous camera-following GPU cell window for the initial consumer, with exactly 27 physical slots and no additional prefetch or retained cells. Preserve overlapping cells as the window shifts and recycle only departing slots. A ring remains appropriate for this regular bounded layout; an arbitrary sparse page table is not required for the initial migration.

The general coordinator can represent multiple sources. This initial contiguous GPU backend supports a bounded source envelope; widely separated source requests must receive an explicit capacity/coverage limitation rather than aliasing or silently replacing another source's data.

Keep near-field geometry capture independent of probe lighting refresh. Publication revisions remain visible to consumers, and existing history invalidation behavior must continue to work during migration. This proposal does not independently redesign temporal lighting history.

## Existing scene consumers

Migrate `TraceSceneRegionScheduler` and `LumonSceneRegionScheduler` through adapters. Each adapter initially preserves its current coverage and work-priority behavior while delegating cell residency to the coordinator.

Once a consumer is migrated, remove its duplicate registry, coverage ownership, and residency-transition scheduling. Preserve its content-update queues, such as capture and relight, where those queues express domain work rather than loading/unloading.

Do not let the old scheduler and the coordinator independently control the same cell lifecycle. During migration, ownership is assigned per registered partition; adapters forward outcomes to the single owner.

Move the reusable coordinator and contracts from `LumOn/WorldCells` into a top-level `WorldPartition` namespace/directory as consumers migrate. Keep separate responsibilities in registration, coordinates, coverage, lifecycle, scheduling, and diagnostics files. Keep `WorldPartitionModSystem` as a thin composition and world-lifetime owner.

## World-probe clipmaps

Evaluate and then migrate spatial residency only. Model each clipmap level as a partition instance with its own spacing and bounds if doing so eliminates duplicate coverage and lifetime code without increasing per-frame work substantially.

A probe partition must use the same world-zero-aligned cell boundaries as every other partition. Probe sampling positions within those cells and atlas ring offsets are consumer details; neither introduces a configurable partition-grid origin. The adapter retains the mapping between logical coordinates and atlas ring slots. Existing probe positions, spacing, and boundary rules must remain consistent with shader interpolation. If a clipmap level cannot map to this fixed grid without changing its sampling lattice, defer its migration rather than adding a partition-origin exception.

Retain domain ownership of:

- Importance and visibility-driven probe selection.
- Directional trace budgets and partial directional updates.
- Lighting age, confidence, invalidation, and temporal history.
- Atlas packing and upload format.

Residency readiness must not be mistaken for valid directional lighting. The adapter must preserve rejection of stale probe work when atlas slots are reassigned. The migration is accepted only if coordinate/ring behavior and lighting-update results remain equivalent and the shared coordinator demonstrably removes duplicated residency machinery. Near-field geometry adoption does not depend on this migration.

## Observability

Provide per-partition counts for required, resident, ready, dirty, queued, and in-flight cells, plus capture/upload backlog, retries, stale completions, and capacity shortfalls. Expose the source bounds and selected cell bounds together so asymmetric coverage is visible.

Extend World Cell Bounds to identify registered partitions and show desired versus ready coverage. Update Near-Field Geometry to visualize the new cell layout using the same GPU resources sampled by tracing. Keep unpublished, unsupported, and out-of-coverage conditions separate. Required coverage that is not ready must be visible rather than reported as an empty successful scene.

## Verification and acceptance

Use reusable in-memory partition providers for lifecycle tests and the existing controlled voxel/GPU fixtures for integration tests.

Required coverage includes:

- World-zero alignment across partitions with different cell sizes, positive and negative coordinates, exact boundaries, fractional source positions, and large world coordinates.
- Every offset within a cell: required bounds remain covered, without the current directional coverage collapse.
- Two instances of the same partition type, different cell sizes, and multiple sources with overlapping or disjoint requests.
- Incremental movement, boundary oscillation, teleports, source removal, and world unload.
- Delayed/out-of-order completions, cancellation races, dirtying during capture/upload, and reuse of a physical slot.
- Missing source chunks becoming available, unsupported geometry remaining explicitly classified, budget pressure, and starvation prevention.
- Stable overlapping GPU cells across ring movement, coherent geometry/light readiness, and preservation of camera-coordinate behavior.
- Existing near-scene, tracing, and probe behavior while each adapter is introduced.

A controlled movement test must sweep the player through an entire publication-cell interval and assert coverage of the requested domain at every position. With delayed providers it must separately assert immediate desired coverage and eventual ready coverage, proving that the test is not treating requested work as completed work.

Measure capture duplication, bytes uploaded during movement, frame-thread work, worker capture duration, resident memory, and readiness latency when validating the fixed near-field window and choosing scheduling budgets. Smaller cells are accepted for their measured granularity and coverage benefits, not assumed to be free.

## Delivery order

1. Add registration, coordinate, coverage, lifecycle, and scheduling contracts around the existing registry, with isolated tests.
2. Introduce the near-field geometry partition, 16-block publication cells, and matching GPU addressing/readiness changes.
3. Validate coverage and publication during movement using the geometry viewer and controlled delayed-work tests.
4. Migrate existing scene residency ownership and remove the replaced code paths.
5. Evaluate world-probe spatial residency against the equivalence and cost criteria above; migrate only that responsibility if those criteria are met.

No change in this proposal assumes that all current indoor lighting failures are caused by spatial coverage. Geometry support and lighting evaluation remain independently testable concerns.
