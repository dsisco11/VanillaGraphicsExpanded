# WorldPartition coverage diagnostics and evidence

Status: item 3 complete. The user confirmed live viewer alignment and resolution of the startup lag; deterministic coverage, resource limits and recovery are verified by 178 passing tests, including 80 GPU cases. The final completion review below supersedes earlier provisional findings and pending-evidence statements.

## Contract traceability

Controlling documents: [task list, item 3](WorldPartition.CoverageAndLifecycle.todo#3-coverage-evidence-and-diagnostics) and the complete [approved proposal](WorldPartition.CoverageAndLifecycle.Proposal.md). The proposal's Observability, Streaming sources and coverage, Budgets and fairness, Near-field geometry migration, and Verification and acceptance sections govern this work. No additional normative document is referenced by those sections.

| Task | Proposal requirements | Implementation and evidence |
| --- | --- | --- |
| Diagnostics | Registration identity, source/selected bounds, required versus ready, backlog/retry/stale/capacity counters, distinct unpublished/unsupported/outside states | `PartitionCoordinator.Diagnostics.cs`; World Cell Bounds partition renderer/panel; production near-field geometry shader and its GPU classification tests |
| Movement | Full publication interval, fractional/negative/large coordinates, immediate demand versus delayed readiness, teleports, dirty updates, camera precision and stable GPU ring overlap | `PartitionCoverageSweepTests`, `PartitionDiagnosticsTests`, `NearFieldMovementMeasurementsTests`; existing near-field ring/publication tests; geometry-view camera sweep and origin-split GPU tests |
| Measurements | Compare source reads, movement uploads, frame-thread work, worker capture, resident memory and readiness latency; select scheduling budgets | Fixed-window runtime-budget measurements and recovery tests; production cache/GPU counters; user-confirmed live alignment and lag resolution |

## Diagnostic behavior

World Cell Bounds selects registered partitions using **Next partition** and identifies each by name, instance and world scope. White boxes show source-required bounds; cyan boxes show the intersecting required-cell envelope. Cell wireframes show acknowledged readiness by default: green ready, orange required/unpublished, violet speculative/unpublished, magenta published unsupported content. **Color By Desired** instead shows required versus speculative demand. Existing legacy scene wireframes retain their earlier lifecycle colors.

Statistics refresh every 250 ms while the view renders. The panel shows required-ready separately from total ready (which includes prefetch), resident/dirty/queued/in-flight counts, capture/upload backlog, retries, stale completions, capacity shortfall and upload-budget shortfall. Wait age starts when a cell is first requested or dirtied, and does not reset on retries. Publication wait is measured in coordinator update ticks, not milliseconds. Unsupported published content can be resident/ready without being valid occlusion everywhere.

Near-Field Geometry reads the production geometry and readiness textures. Violet means unpublished; magenta means unsupported voxel geometry; blue means a ray never intersects coverage; black means it traversed published air and left the volume. Scene-unavailable blue has a different red component and remains separately labelled. White lines on occupied surfaces show publication-cell boundaries; dark lines remain voxel boundaries. Integer wrapping precedes floating-point conversion for the publication grid, preserving large-coordinate behavior.

## Measurement semantics

**Log measurements** in World Cell Bounds emits `[VGE PartitionMetrics]` JSON records to the game's normal client log once per second. The switch remains enabled when changing debug views so the geometry viewer can be inspected during capture. Turn it off to stop recording.

- Source reads count loader requests; cache hits count successful shared snapshot returns. These are not counts of unique disk reads or world chunk loads.
- Uploaded bytes count actual API input payloads, including readiness writes and material palette uploads. Float light inputs are counted as float transfers even though texture storage is RGBA8.
- Texture bytes are nominal allocated texel storage, including readiness and material textures. Snapshot bytes are successfully completed payloads held by the near-field source cache. Neither metric is total process/driver memory; task/executor caches, managed object overhead and transient upload copies are excluded.
- Update timing covers the near-field renderer's coverage, dependency maintenance and coordinator pump. It excludes logging, diagnostic snapshots and consumer-side scene preparation. It is not total frame time or GPU execution time.
- Cumulative counters reset when the near-field registration is recreated. Compare samples only within the same instance; record resolution and margins alongside them. Mean/max readiness latency includes speculative publications as well as required publications. Oldest required wait and required-ready counts expose current overload independently.

## Automated receipts

Build passed with zero errors and eight existing warnings; all seven SPIR-V variants compiled on the initial build. Final focused NearField/WorldPartition tests passed **157/157**, including **77 actual GPU tests**, with no skips. This includes the full-interval GPU camera sweep at origins 0 and +/-16,777,216. The independent audit subsequently identified a latency-counter correction; its focused follow-up receipt is recorded below.

Final receipts: `artifacts/partition-diagnostics-final.build.log`, `artifacts/partition-diagnostics-final.tests.log`, and `artifacts/TestResults/partition-diagnostics-final.trx`. Initial receipts remain under the earlier `partition-diagnostics` names. Validation used isolated output at `artifacts/partition-diagnostics-bin` because an existing test process held the normal test executable. That process was left running.

Controlled source-cache/provider comparison: origin radius 16, trace reach 1, source capture limit 2 per update, explicitly deferred source completions, walk from X=0 through X=16 in one-block steps at Y=Z=0.5. Each position settles before moving onward. This measures finite-demand convergence and reuse, not sustained motion under load. The separate lifecycle test sweeps quarter-block offsets with delayed completions at zero and positive/negative large coordinates.

| Prefetch blocks | Ring resolution | Initial / movement publications | Initial / movement source reads | Movement voxel transfer bytes | Peak cached snapshot payload | Voxel texture bytes | Maximum settle updates |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 64 | 64 / 16 | 8 / 4 | 1,310,720 | 7,864,320 | 2,097,152 | 4 |
| 8 | 80 | 64 / 16 | 8 / 4 | 1,310,720 | 7,864,320 | 4,096,000 | 4 |
| 16 | 96 | 216 / 36 | 64 / 0 | 2,949,120 | 41,943,040 | 7,077,888 | 32 |

All three scenarios had zero duplicate `(chunk, revision)` loader requests and retained overlapping publications. Synthetic voxel transfer bytes exclude readiness/palette traffic; the production GPU counter includes both. Final-run total measured fixture update time was 21.191 / 53.681 / 298.860 ms over 23 / 23 / 49 calls respectively (initial run: 40.050 / 51.844 / 288.560 ms). These are managed fixture timings with a copying CPU backend, not statistically qualified performance comparisons or GPU upload timings.

The 16-block margin avoided new source reads on this short path, at substantially greater initial demand and memory. The 8-block margin incurred a larger fixed texture allocation without a benefit on this particular path. These observations support testing smaller margins but do not establish safe live defaults. Existing origin radius 16, prefetch 16, maximum resolution 192 and runtime budgets remain provisional.

## Historical live-runtime procedure and remaining evidence

1. Reload the validated build in a representative world and enable Log measurements. Record a stationary warm-up, current configuration and frame rate.
2. Walk continuously through at least a full 16-block interval in positive and negative directions, including an exact boundary. Inspect World Cell Bounds and Near-Field Geometry; retain the corresponding client log.
3. Teleport, wait for convergence, then edit nearby terrain. Confirm desired bounds update immediately, dirty/unpublished regions remain explicit, and eventual readiness follows source availability. Move/bob/rotate the camera while observing the geometry grid.
4. Compare matched routes with candidate margins, recording counter deltas within each registration and separate warm-up/movement/recovery intervals. Record missing source chunks and any capacity shortfalls instead of attributing them to empty space.
5. Select final margins and budgets using live readiness, frame-thread cost, transfers and memory, then rerun affected coverage tests.

Live observations, live margin comparison and the final margin/budget decision are not yet available. No instantaneous-readiness claim is made under overload. The running game predates this build and is not evidence for these changes.

## Review

Second review checks: detached observations preserve coordinator ownership; required-ready excludes prefetch; retry wait age is monotonic; upload accounting distinguishes transfer bytes from resident texture bytes; diagnostics retain integer-before-float coordinate conversion; UI statistics fit the existing panel's source dimensions using two-column controls and selected-partition text. Actual rendering still requires live verification.

The independent completion audit read the full proposal and identified one implementation defect: evicting ready speculative content retained the old wait-start timestamp. The correction resets that timestamp only when ready content is evicted; it preserves an already-running wait when unready content loses its reservation. `EvictedReadyCellRestartsPublicationWait` forces eviction at tick 100 and republication at tick 104 and checks that the old ready lifetime is excluded. The auditor re-reviewed and accepted this source correction. Follow-up WorldPartition tests passed **48/48**, including that regression, with no skips/failures: `artifacts/partition-diagnostics-audit-fix.tests.log` and `artifacts/TestResults/partition-diagnostics-audit-fix.trx`. The isolated rebuild had zero errors/eight existing warnings; the production-only normal launch-output rebuild had zero errors/warnings (`artifacts/partition-diagnostics-audit-fix.production-build.log`). The earlier 77 GPU passes remain applicable because the correction only changes a CPU diagnostic timestamp.

Historical audit verdict: not complete. Live visualization, matched live margin/resource measurements and final margin/budget selection remain evidence gaps. The existing runtime process was not restarted or modified. Task markers and the goal remain open until those requirements are satisfied.

## Live feedback: bounds camera transform

The user enabled measurements and walked through the world, then reported bobbing in World Cell Bounds. Source inspection confirmed the same pattern corrected by commit `6fb1369` in the probe wireframe renderer: camera-relative vertices were projected with `CameraMatrixOriginf` after its translation entries were cleared. World Cell Bounds now preserves the complete matrix, retaining its existing double-precision camera subtraction. This covers partition cells, source envelopes and legacy scene bounds together. Live confirmation of the corrected overlay remains pending. The existing near-field GPU camera tests do not exercise this CPU wireframe matrix assembly.

A scan of the debug shaders and their CPU matrix producers found one additional translation-stripping producer in `LumOnDebugRenderer.UpdateCurrentViewProjMatrixNoTranslate`. Its stored previous-frame matrix is consumed by `lumon_debug_temporal.glsl` for Temporal Weight and Temporal Rejection (modes 6 and 7). Following user approval, the shared debug renderer now uses the full view-projection matrix and the obsolete translation-stripping helper has been removed. Other debug position reconstruction uses the full inverse view; the other wireframe paths use the full view-projection matrix. Rotation-only transforms of normals and ray directions are intentional and do not constitute this defect.

Bounds-fix validation: production build passed with zero warnings/errors. Existing focused camera/coordinate regressions passed 12/12 (seven GPU and five unit cases), with no skips; receipts are `artifacts/bounds-transform-build.log` and `artifacts/bounds-transform-focused.log` / `.trx`. These regressions do not prove the World Cell Bounds CPU matrix assembly or live overlay alignment; the source correction and pending live confirmation remain separate evidence.

The user subsequently reported that the colored cell grid still followed camera bob after restarting with the corrected build. The first matrix correction therefore did not establish a resolved live defect. Comparison with the existing probe wireframe path found another difference: it renders during OIT, while World Cell Bounds still rendered AfterBlit. World Cell Bounds now registers, draws and unregisters during OIT, preserving the full matrix and camera-relative vertices. This matches the probe renderer's world-geometry stage instead of drawing the grid as a post-blit overlay. No additional camera-position or inverse-view offset was introduced.

`WorldCellBoundsCameraTests` constructs the actual bounds renderer with scripted API interfaces and checks stage registration/retirement. It also invokes the actual matrix producer with nonzero eye/bob translation, reads the packed debug-line UBO, and compares point projection against an independent scalar view-then-projection oracle. This covers the CPU matrix path missed by the earlier tests. It does not simulate the complete engine render loop or prove live terrain alignment; confirmation of the stage correction remains pending.

Follow-up verification: production build passed with zero warnings/errors, and all four bounds-camera tests passed with zero skips/failures. Receipts: `artifacts/bounds-stage-production-build.log`, `artifacts/bounds-stage-camera-tests.log`, and `artifacts/TestResults/bounds-stage-camera.trx`. The test project references the installed managed SkiaSharp assembly because it appears in `IRenderAPI` signatures used by the proxy; the tests do not call native Skia drawing.

## Live capture review: 2026-09-21

The user confirmed World Cell Bounds renders correctly after the OIT-stage correction. This supersedes the pending live-alignment status above.

Available measurement evidence consists of 45 samples from 10:53:13 through 10:53:58 in `C:\Users\Sisco\AppData\Roaming\VintagestoryData\Logs\Archive\2026-09-21_11_11_26\client-main.log`. The current client log contained no PartitionMetrics entries when inspected. These samples therefore cannot be attributed to the latest walk or establish the timing of its reported startup lag.

- Allocated near-field resolution: 160 cubed, or 1,000 physical 16-cubed cells. This is 37.04 times the voxel capacity of the user's intended 48-cubed, 27-cell window. Recorded texture storage is 33,555,432 bytes; cached source snapshot payload grows from 25 to 30 MiB.
- Required readiness remains 256 of 512 cells at the endpoints, with capture backlog declining from 744 to 680. Retries increase by 119,928 across 1,875 updates: 63.96 retries per update, almost the entire 64-attempt budget. Missing dependencies retry on the next tick; scheduling repeatedly enumerates and sorts candidates and reconstructs outstanding-work sets. These are confirmed redundant-work mechanisms, not individually profiled costs.
- Sampled update duration averages 5.025 ms (3.889 to 7.078 ms). Cumulative mean ends at 6.087 ms; cumulative peak is 104.214 ms and predates the first sample. The timer covers the renderer update, not separately enqueued main-thread source capture, total frame time, or isolated GPU execution.
- During the 45-second interval, nine additional source reads and 72 cell publications transfer 5,898,392 additional bytes (5.625 MiB). The endpoints report zero upload backlog. This does not establish excessive sustained upload bandwidth as the principal cause of lag, and does not measure the initial upload burst.
- Source snapshots bulk-lock solid block IDs, but subsequently call GetLightRGBs once per voxel, GetMostSolidBlock for air entries, and collision-box queries for non-air entries. Capture runs on the main thread over 32-cubed source chunks, shared among eight partition cells. A chunk therefore entails 32,768 lighting accessor calls; the source permits two new requests per update. This is a credible startup-stall source requiring separate timing.
- Camera Y is 3 in this capture. Symmetric coverage extending below the world is consistent with half the required cells remaining unavailable. The source availability check has no explicit vertical-domain classification, but the log omits failed cell coordinates/reasons; this remains an inference.

Recommended follow-up: adopt an explicit 3-by-3-by-3 near-field window with defined trace-exit behavior, prevent unavailable dependencies from consuming the full capture budget every update, and bulk-capture lighting where supported with separately measured main-thread capture cost. Merely lowering MaximumResolution to 48 would reject the current conservative coverage plan rather than implement the requested window. Matched margin measurements and final scheduling-budget decisions remain outstanding.

## Approved bounded-window and worker-capture correction

The near-field runtime now allocates exactly 48 cubed voxels in 27 world-zero-aligned 16-cubed ring slots. The camera's cell is the middle cell on each axis, providing between 16 and 32 blocks to each window face as the camera moves. There is no additional prefetch or retained fringe. Vertical demand is intersected with the world height, preventing below-world cells from entering the retry queue. Nominal texture payload is 1,671,195 bytes including the existing material palette and readiness texture (driver allocations are not measured).

Ray length no longer determines allocation size. Supported origins are bounded by the ring, and traversal still requires every visited cell to be published and supported. A premature window exit remains unresolved; only a completed clear segment authorizes distant cache lighting. This preserves conservative occlusion, but the smaller volume can leave longer cache-handoff segments unresolved. It does not promise world-probe lighting on every screen surface.

Near-field capture now uses a dedicated snapshot source invoked by the existing two-worker ChunkProcessingService, with no main-thread enqueue. The source retains actual engine palette layer identities and takes one read lock per solid, fluid and light layer. It copies packed lighting directly, avoiding per-voxel world lookup, chunk unpack/lock and Vec4 allocation. Decoding matches the installed engine's hue/saturation tables, integer HSV-to-RGB conversion, RGB ordering, intensity and sunlight. Collision evaluation remains position-dependent and runs on workers; material indices and decoded light values are reused within each snapshot. GPU publication remains on the render thread.

Captures remain coalesced at the engine's 32-cubed source-chunk granularity; this can include source voxels outside the 48-cubed GPU window. Only requested 16-cubed cells are uploaded. The processor makes one owned copy of the completed near-field snapshot instead of copying through the occupancy payload. Post-capture version, chunk identity and disposal checks prevent stale publication. The engine-specific palette path is verified with real ChunkData; alternate chunk implementations retain synchronized chunk-local API reads.

Missing dependencies back off by 1, 2, 4, 8, 16, 32 and then 64 coordinator updates, with delayed candidates excluded before sorting. Successful publication and explicit dirty notifications reset backoff. Availability without a notification is rediscovered within the capped polling delay. This avoids repeated attempts each frame without interpreting missing data as empty space.

Logs now include CaptureCount, CaptureMilliseconds (cumulative worker execution time), and PeakCaptureMilliseconds, separately from render-thread update timing. Concurrent worker durations are summed; they are not total frame time. OriginRadius now describes the fixed window half-extent of 24, not an exact camera-centered origin sphere. PrefetchMargin is zero.

Verification: 167 focused NearField/WorldPartition tests passed, including 78 actual GPU tests, with zero skips. The fixed-window GPU case proves short clear segments still obtain cache radiance while segments exceeding the window remain unresolved. The controlled movement fixture asserts 27 initial publications and nine new publications after a one-cell move, preserving the other 18. Worker tests prove off-caller-thread capture, bounded world accessor calls, real palette copying, light decoding, cancellation recovery and rejection after source edits/unloading. Receipts: artifacts/nearfield-bounded-worker-final-tests.log and artifacts/TestResults/nearfield-bounded-worker-final.trx.

The production C# build passed with zero warnings/errors and was written to the normal launch output. The SPIR-V build integration was disabled because its tool restore could not reach NuGet; no tracing shader changes were needed, and actual GPU shader execution was covered above. Receipt: artifacts/nearfield-bounded-worker-final-csharp-build.log. Six existing test-build warnings remain. Post-change live startup/movement timing is still required before claiming the reported lag is resolved.

A final worker-only follow-up passed 7/7 tests, including the additional solid-fluid precedence regression. This brings unique verified focused cases to 168. The final normal-output C# build again passed with zero warnings/errors. Receipts: artifacts/nearfield-worker-followup-tests.log, artifacts/TestResults/nearfield-worker-followup.trx, and artifacts/nearfield-worker-followup-csharp-build.log.

## Final completion review

Verdict: item 3 is satisfied, with high confidence in the tested coverage/lifecycle/resource contracts. No required discrepancy remains open. This review read the complete current proposal, item 3 and its inherited links, the diagnostic implementation, source/partition/GPU paths, and the actual test receipts. The user's subsequent confirmations establish live viewer alignment and lag resolution; they are qualitative observations, not measured post-change frame-time statistics. The user approved closing deterministic behavior with automated tests instead of requiring another general walkthrough.

| Contract area | Evidence | Disposition |
| --- | --- | --- |
| Registration, source/selected bounds, demand/readiness and backlog/retry/stale/capacity diagnostics | PartitionCoordinator.Diagnostics/Statistics, bounds renderer/panel; PartitionDiagnosticsTests, lifecycle capacity/fairness tests | Satisfied |
| Distinct unpublished, unsupported, outside and empty geometry | GeometryView_DistinguishesPublishedStates, including dedicated and monolithic shader entrypoints | Satisfied by actual GPU rendering |
| Fractional/full-interval, negative and large coordinates; immediate desired versus delayed ready state | NearFieldCoveragePolicyTests, PartitionCoverageSweepTests, geometry-view camera interval sweeps | Satisfied |
| GPU overlap, slot reassignment, teleport and geometry/light coherence | RegionRing_PreservesOverlapAndClearsReassignedSlots, including added positive/negative 48-block-ring teleports; production publication/invalidation tests | Satisfied |
| Camera bob and terrain-stage alignment | WorldCellBoundsCameraTests invokes the real CPU matrix producer and stage registration; user confirms correct live rendering | Satisfied; automated and live evidence remain distinguished |
| Runtime-limit recovery, edits and obsolete completion rejection | New NearFieldBudgetRecoveryTests combines the production planner, source cache, provider and coordinator with delayed captures at zero and +/-16,777,216 coordinates | Satisfied; budgets checked on every update |
| Source reuse, transfer/storage counts, update/capture time and readiness latency | FixedWindowReusesOverlap compares unrestricted coordinator service with actual runtime limits; actual worker capture prints separate timing; GPU tests check transfer/storage counters | Satisfied within the documented metric boundaries |

The review found two discrepancies and resolved both: movement measurements previously used generous fixture limits rather than runtime limits, and documentation retained superseded expanding-volume/pending-live-evidence descriptions. The fixture now accepts injected limits, runtime-budget scenarios are verified, and the task list/current evidence reflect the approved fixed window and user confirmations.

### Resource decision and measurements

Retain the existing near-field limits: 27 resident slots; 64 in-flight partition requests; 64 capture attempts and 32 dispatches per update; 8 MiB reserved upload budget per update. The source cache separately permits two new source requests per update and eight in-flight requests including cancelled workers awaiting acknowledgement; actual capture runs on two chunk-service workers. With only 27 desired cells, larger request/dispatch ceilings do not enlarge the resident volume. These are accepted bounded operating defaults, not a claim of optimal frame-time performance.

The provider conservatively reserves a worst-case material palette with each cell publication. The 8 MiB budget admits at most two such reservations per update. Tests check that bound, the two-source-request limit, eight in-flight source credits, 27 resident cells, at most eight cached 32-cubed snapshots, and that published cells belong to the current window. Initial and recovered demand converges without capacity shortfall or remaining capture/upload backlog.

| Controlled movement measurement | Generous coordinator limits | Runtime limits |
| --- | ---: | ---: |
| Ring resolution / initial publications | 48 / 27 | 48 / 27 |
| One-cell movement publications / retained overlap | 9 / 18 | 9 / 18 |
| Initial / movement source requests | 8 / 4 | 8 / 4 |
| Duplicate source-generation requests | 0 | 0 |
| Movement voxel input bytes | 737,280 | 737,280 |
| Peak source-cache payload bytes | 5,242,880 | 5,242,880 |
| Voxel texture payload bytes, excluding palette/readiness | 884,736 | 884,736 |
| Maximum settle updates | 15 | 16 |
| Measured fixture update time / calls | 28.783 ms / 35 | 49.534 ms / 38 |

These timings are one controlled execution with a copying CPU backend and intentionally delayed source completions. They are not live rendering timings or a statistically qualified speed comparison. Movement voxel bytes exclude palette/readiness traffic; real GPU publication tests separately verify the production upload counters. The worker fixture measured 4.595 ms for one source capture and exactly two world chunk-identity lookups, with no per-voxel world accessor calls. This is synthetic worker wall duration, not main-thread stall time.

The combined runtime-budget recovery scenarios each settled in 16 updates initially, 5 after a one-cell move, 16 after teleporting away from pending edit work, and 16 after a subsequent edit. All three coordinate anchors produced 30 source requests and 90 total cell publications. Cancelled old snapshots deliberately completed with stale geometry and did not become destination data. These deterministic update counts depend on fixture completion timing; no instantaneous-readiness or universal live latency promise is made.

### Validation and scope boundaries

Final receipts: `artifacts/item3-completion-tests.log`, `artifacts/TestResults/item3-completion.trx`, and `artifacts/item3-completion-build.log`. All 178 selected tests passed with zero skips/failures, including 80 GPU tests and the bounds-camera regressions. Production C# build passed with zero warnings/errors; test compilation retained six existing warnings. Package restore used the existing local package cache. SPIR-V build integration remained disabled; no production shader changes were made in this completion pass, and actual GPU shader compilation/execution was verified by the selected suite.

Intentional conditions are not completion defects: rays exiting the bounded geometry window remain unresolved; unsupported published geometry is distinguishable from valid occlusion; readiness can lag demand under delayed work; and source chunks can contain voxels outside the GPU window. Quantified post-change live frame-time improvement and exhaustive engine/mod combinations are not claimed. The user's live confirmations plus automated proofs satisfy this item's agreed scope; another manual walk is not a prerequisite to item 4.
