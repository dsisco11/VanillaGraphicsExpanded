# World-probe sealed-room reproduction tests

## Purpose

Separate incorrect world-probe light generation from lighting introduced by spatial interpolation. These deterministic tests use controlled voxel data with the production block accessor trace scene, integrator and GPU shaders. They do not require a running game.

## Reusable fixtures

The fixtures live in [Tests/Fixtures/WorldProbes](../VanillaGraphicsExpanded.Tests/Fixtures/WorldProbes).

| Fixture | Responsibility |
| --- | --- |
| ControlledVoxelWorld | Mutable solid cells, material IDs/collision boxes, independent light values, loaded-cell policy and recorded light queries. Supports room shells, block edits and light regions. |
| ControlledBlockAccessor | Adapts those cells to the engine accessor interface. Unexpected API calls fail loudly. |
| LoadedChunkSentinel | Represents loaded chunk presence without simulating chunk internals. |
| WorldProbeRoomScenario | Builds the reference room and runs complete 16×16 directional traces. |
| WorldProbeAtlasData | Packs successful, complete trace results into single-level radiance/visibility/confidence arrays. Rejects missing or duplicate directions. |

New scenarios can construct their own voxel worlds, choose material/collision geometry and light regions, and reuse the same tracer and atlas packer. Ray results are never scripted. The production implementation selects rays, traverses cells and chooses the surface-adjacent light cell.

## Reference geometry

A one-voxel-thick inner room occupies inclusive block bounds (-4,-4,-4) to (1,4,4). Its air interior is x=[-3,1), y=[-3,4), z=[-3,4). Interior light cells have zero block light and zero sunlight. An outer enclosure at (-8,-8,-8) to (8,8,8) makes exterior directions hit known surfaces instead of relying on sky misses.

The positive X wall occupies [1,2). Probe centers at x=0.5 are inside; centers at x=2.5 are outside. Centers at y/z=0.5 and 2.5 form the eight corners of a spacing-2 interpolation cell.

## CPU coverage

[WorldProbeSealedRoomTests](../VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeSealedRoomTests.cs) checks:

- All 256 unique interior directions successfully hit geometry and return zero radiance.
- Bright exterior block light and sunlight do not affect those interior traces.
- Actual light-cell queries stay inside the dark interior.
- Exterior probes return the explicitly supplied block light.
- Opening a doorway exposes exterior lighting; closing it restores darkness on a fresh trace.
- Unavailable world data fails tracing rather than silently satisfying a darkness assertion.

The doorway test checks fresh tracing only. It does not validate scheduler invalidation or stale atlas removal.

## GPU coverage and visibility regression

[Sealed-room GPU cases](../VanillaGraphicsExpanded.Tests/GPU/LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.SealedRoom.cs) populate all eight probe tiles from actual CPU trace results, then run forced screen misses through production atlas tracing, two frames of separate temporal histories, filtering, both atlas and SH9 gather, and the signed lighting-effect shader.

| Exterior block light | Interior sample X | Expected directional cache radiance |
| --- | --- | --- |
| 0 | 0.75 | 0 |
| 1 | 0.5, exactly at the interior probe center | 0 |
| 1 | 0.75, still inside the sealed room | 0 (previously 0.125) |

The final case originally reproduced across-wall interpolation: exterior weight (0.75-0.5)/2 = 0.125 contributed despite the enclosing wall. It now requires zero radiance and neutral gray through both gather modes and the paired diagnostic.

[Visibility controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.Visibility.cs) additionally check:

- All neighbors blocked, visibility texels uninitialized, or recorded miss range too short: zero lighting, zero confidence, and no approximate sky fallback.
- Blocked bright neighbors removed while visible neighbors retain full radiance through renormalization.
- Ring-shifted atlas storage uses the correct physical probe centers.
- Unobstructed neighbors retain lighting between probe centers.
- A doorway in actual voxel geometry permits exterior lighting.
- An unavailable coarse level preserves visible fine lighting and does not turn rejected fine data into sky.
- Both final-gather modes reject wholly blocked world-probe neighborhoods.

## Implemented repair

The shared sampler tests each contributing probe-to-sample segment using that probe's stored directional hit distance, independently of the lighting direction. Rejected corners contribute neither radiance nor associated scalar metadata; surviving published lighting samples are normalized by their usable weights.

The visibility tolerance scales with probe spacing and is clamped to 0.001-0.05 world units. Negative encodings establish visibility only within their traced range. Zero/nonfinite encodings are unavailable, not open space. Probe-center coincidence avoids normalizing a zero-length direction; the lighting lookup still checks publication.

Cache coverage is separate from usable lighting confidence. A covered but rejected or unfinished neighborhood stays dark with zero usable confidence instead of selecting approximate sky. Completely absent cache coverage retains the existing fallback. An unavailable coarse level cannot erase a usable fine result.

## Evidence limits

This isolates a mechanism capable of producing above-gray lighting in a sealed interior. It does not establish that this mechanism caused a particular live scene's result.

The fixture bypasses asynchronous scheduling, upload budgets, incremental publication and cache invalidation. The voxel scene uses one clipmap level; additional controls exercise an X ring offset and an unavailable second level. Temporal reprojection is disabled; each branch has two deterministic frames. Gather output is supplied directly to the debug shader at half resolution, so production upsampling, final composition, camera motion and live renderer orchestration are outside this test.

## Validation

**71 tests passed, 0 failures, 0 skipped.** Production and test builds succeeded, including seven SPIR-V shader compilations. The GPU controls executed with a valid OpenGL context.

Receipts: [repair build/test log](../artifacts/visibility-repair-final.log) and [repair TRX results](../artifacts/TestResults/visibility-repair-final.trx).

The repaired sealed-room case remains dark through CPU tracing, screen-probe tracing, independent temporal histories, both gathers and the diagnostic shader. Visibility uses nearest directional depths from a 16x16 tile; thin occluders, grazing angles and corners remain approximate. Live-game validation and performance measurement have not been performed.

## Flat-wall visibility artifact reproduction

[Planar-wall GPU tests](../VanillaGraphicsExpanded.Tests/GPU/LumOnWorldProbeWallVisibilityFunctionalTests.cs) render the production world-probe irradiance (31) and confidence (33) debug modes over a 128x128 grid. A fully lit voxel enclosure supplies one complete 16x16 world-probe tile from the real CPU integrator. Every direction hits geometry and stores unit RGB radiance; there are no missing directions, sky misses, mixed probe colors or clipmap transitions.

The grid spans x/y=(-5,5) in front of the z=8 wall, with surface normal -Z. The probe is at (0.5,0.5,0.5). Before rendering, the production voxel tracer checks all 16,384 exact probe-to-sample rays and confirms that enclosing geometry is farther away than each sample point.

Two cases separate surface proximity from missing lighting:

- A grid 0.01 world units inside the wall characterizes the current defect: both accepted and incorrectly rejected pixels must exist.
- A grid 2 world units inside the wall must retain all samples.

Rejected pixels must be black in the irradiance view and zero in the confidence view. Accepted pixels must retain the source confidence and the expected tone-mapped unit-radiance diffuse integral, pi/(1+pi). Thus the test checks the displayed failure directly, rather than inferring it solely from a CPU copy of the visibility formula.

These are explicit characterization assertions: a passing near-wall case means the defect was reproduced, not fixed. The eventual repair should change it to require zero rejected pixels while preserving the exact-ray ground truth and control case. Production code is unchanged by this reproduction.

The tests report rejection counts through test output; no image or CSV files are generated.

### Recorded planar-wall results

The focused suite passed **73 tests, 0 failures, 0 skipped**, including both new GPU cases:

| Wall inset | Exact unobstructed segments | False GPU rejections | Accepted |
| --- | --- | --- | --- |
| 0.01 | 16,384 | 6,228 (38.01%) | 10,156 |
| 2 | 16,384 | 0 | 16,384 |

Matching irradiance/confidence assertions attribute black pixels to rejection rather than absent source lighting. This is a controlled single-probe reproduction, not an exact reconstruction of a particular live scene.

Validation receipts: [test log](../artifacts/wall-visibility-reproduction.log) and [TRX](../artifacts/TestResults/wall-visibility-reproduction.trx).


## Local world tracing and cache handoff

The screen-probe trace shader now resolves screen misses through local voxel geometry before sampling distant world radiance. The earlier direct irradiance visibility implementation and its flat-wall characterization remain unchanged.

[LocalTraceVoxelFixture](../VanillaGraphicsExpanded.Tests/GPU/Fixtures/LocalTraceVoxelFixture.cs) adapts the shared controlled voxel world into production region artifacts and publishes them through the real GPU scene owner. Scene contents, normalized cell light, hit materials, readiness and versions are independently controllable. The fixture uses real texture uploads and production traversal; it does not mock individual ray results.

The GPU scenarios are separated into [basic tracing](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.cs), [geometry](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Geometry.cs), [hit lighting](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Lighting.cs), [publication](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Publication.cs), and [cache sampling](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Cache.cs), with a shared binding harness.

Coverage includes:

- Bright exterior/cache around a dark sealed room: zero lighting with valid local-hit confidence in every traced direction.
- Nonzero outside-cell block light: preserved local radiance, unaffected by cache-only diagnostic suppression.
- Open doorway followed by closure: distant lighting appears through the opening and disappears behind the new wall.
- Initial solid cells, tied voxel boundaries, negative coordinates and world offsets of plus/minus 16,777,216.
- Unpublished/unsupported geometry, step exhaustion and bounds exit: zero radiance and zero confidence without inferred sky.
- Missing material: opaque hit retained, lighting unavailable, no cache substitution.
- Dirty generations, stale async completions and world-region identity after physical slot reuse.
- Fully visible diffuse sky term of inverse pi; independent emitted radiance with the GI emission boost applied once.
- Clear local segments retain distant lighting; near cache hits are excluded.
- Directional parallax changes addressed cache texels while preserving constant incident radiance between eight neighbors.
- Combined local tracing and importance-selection shader compilation with world caching enabled and disabled.

The sealed-room test caught an implementation defect where advancing all tied DDA axes sampled outside light from a different wall cell. The tracer now resolves tied boundaries one face at a time, preserving the adjacent light-cell relationship.

[Source-cell tests](../VanillaGraphicsExpanded.Tests/Unit/LumOn/Scene/LocalTraceCellCaptureTests.cs) verify supported opaque cubes, unsupported partial/noncolliding/transmissive geometry, most-solid-layer checks and normalized lighting. [Artifact tests](../VanillaGraphicsExpanded.Tests/Unit/LumOn/Scene/LocalTraceRegionArtifactTests.cs) verify independent payload ownership and omission of uncaptured companion data.

### Validation and limits

The local suite passes **30 tests: 22 GPU cases and 8 CPU cases, zero failures or skips**. This includes both combined importance-selection shader variants.

The prior 73-test regression suite passes with no failures or skips, including the existing flat-wall defect characterization. Production/test builds and seven SPIR-V compilations succeed. The explicit local-path shaders execute in the GPU tests; the production runtime enables this variant while older comparison fixtures retain the legacy branch.

A broader selection passed 271 of 274 tests. Both missing-import assertions in TraceSceneDebugShaderCoordSpaceTests and the introduced-slab assertion in WorldProbeSchedulerTests also fail in an isolated export of unchanged revision cd76f8de6365645705c280e6d7da0bdda3890173. They were not changed by this work.

Receipts: [local suite](../artifacts/local-trace-final.log), [local TRX](../artifacts/TestResults/local-trace-final.trx), [73-test regression](../artifacts/local-trace-regression.log), [broader run](../artifacts/local-trace-broad-regression.log), and [unchanged-revision baseline](../artifacts/local-trace-head-baseline.log).

These tests establish controlled shader and publication correctness. They do not measure live frame time, main-thread snapshot cost, production update-budget pressure, camera-motion history behavior or real-scene appearance. Unsupported geometry remains unresolved by design. Direct irradiance fallback and the irradiance viewer still require the separate visibility repair.
