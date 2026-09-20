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
