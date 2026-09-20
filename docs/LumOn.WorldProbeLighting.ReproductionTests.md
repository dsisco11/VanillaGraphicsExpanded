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

## GPU coverage and current-defect characterization

[Sealed-room GPU cases](../VanillaGraphicsExpanded.Tests/GPU/LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.SealedRoom.cs) populate all eight probe tiles from actual CPU trace results, then run forced screen misses through production atlas tracing, two frames of separate temporal histories, filtering, both atlas and SH9 gather, and the signed lighting-effect shader.

| Exterior block light | Interior sample X | Expected directional cache radiance |
| --- | --- | --- |
| 0 | 0.75 | 0 |
| 1 | 0.5, exactly at the interior probe center | 0 |
| 1 | 0.75, still inside the sealed room | 0.125 |

The last expectation deliberately characterizes an existing defect: the shader interpolates bright exterior probes across the wall. The exterior weight is (0.75−0.5)/2 = 0.125. Both sides have equal positive confidence, all directions are initialized, and there are no sky misses. World-radiance suppression must preserve metadata and eliminate the lighting; the normal branch must remain brighter than neutral gray after gather.

When spatial visibility is repaired, replace the leakage expectation with darkness while retaining the bright exterior and probe-center controls. A passing characterization test is not a claim that leaking through the wall is desirable.

## Evidence limits

This isolates a mechanism capable of producing above-gray lighting in a sealed interior. It does not establish that this mechanism caused a particular live scene's result.

The fixture bypasses asynchronous scheduling, upload budgets, incremental publication and cache invalidation. It uses one clipmap level with no ring offset. Temporal reprojection is disabled; each branch has two deterministic frames. Gather output is supplied directly to the debug shader at half resolution, so production upsampling, final composition, camera motion and live renderer orchestration are outside this test.

## Validation

The focused build and suite passed: **42 tests, 0 failures, 0 skipped** (36 CPU and 6 GPU cases). GPU cases executed with a valid OpenGL context. The three voxel-derived GPU cases each exercise both gather modes.

Receipts: [build/test log](../artifacts/sealed-room.log) and [TRX results](../artifacts/TestResults/sealed-room.trx).

The reproduction confirms that dark CPU-generated interior tiles can yield positive screen-probe radiance through interpolation with bright exterior tiles. The all-dark and interior-center controls remain neutral gray; the across-wall case remains above gray through both gathers and the diagnostic shader. No production lighting behavior was changed.
