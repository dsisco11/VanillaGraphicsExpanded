# World-probe sealed-room reproduction tests

## Purpose

Separate incorrect world-probe light generation from lighting introduced by spatial interpolation. These deterministic tests use controlled voxel data with the production block accessor trace scene, integrator and GPU shaders. They do not require a running game.

## Reusable fixtures

The fixtures live in [Tests/Fixtures/WorldProbes](../VanillaGraphicsExpanded.Tests/Fixtures/WorldProbes).

| Fixture                 | Responsibility                                                                                                                                                                   |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ControlledVoxelWorld    | Mutable solid cells, material IDs/collision boxes, independent light values, loaded-cell policy and recorded light queries. Supports room shells, block edits and light regions. |
| ControlledBlockAccessor | Adapts those cells to the engine accessor interface. Unexpected API calls fail loudly.                                                                                           |
| LoadedChunkSentinel     | Represents loaded chunk presence without simulating chunk internals.                                                                                                             |
| WorldProbeRoomScenario  | Builds the reference room and runs complete 16×16 directional traces.                                                                                                            |
| WorldProbeAtlasData     | Packs successful, complete trace results into single-level radiance/visibility/confidence arrays. Rejects missing or duplicate directions.                                       |

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

| Exterior block light | Interior sample X                         | Expected directional cache radiance |
| -------------------- | ----------------------------------------- | ----------------------------------- |
| 0                    | 0.75                                      | 0                                   |
| 1                    | 0.5, exactly at the interior probe center | 0                                   |
| 1                    | 0.75, still inside the sealed room        | 0 (previously 0.125)                |

The final case originally reproduced across-wall interpolation: exterior weight (0.75-0.5)/2 = 0.125 contributed despite the enclosing wall. It now requires zero radiance through both gather modes and black in the paired luminance diagnostic.

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

- A grid 0.01 world units inside the wall originally characterized the defect; after the repair, every sample must be accepted.
- A grid 2 world units inside the wall must retain all samples.

Rejected pixels must be black in the irradiance view and zero in the confidence view. Accepted pixels must retain the source confidence and the expected tone-mapped unit-radiance diffuse integral, pi/(1+pi). Thus the test checks the displayed failure directly, rather than inferring it solely from a CPU copy of the visibility formula.

The current tests require zero rejected pixels, retaining the original exact-ray ground truth and inset control. The dense near-wall case also runs through both final-gather modes. The original characterization results below are historical evidence of the repaired failure.

The tests report rejection counts through test output; no image or CSV files are generated.

### Original planar-wall results

The focused suite passed **73 tests, 0 failures, 0 skipped**, including both new GPU cases:

| Wall inset | Exact unobstructed segments | False GPU rejections | Accepted |
| ---------- | --------------------------- | -------------------- | -------- |
| 0.01       | 16,384                      | 6,228 (38.01%)       | 10,156   |
| 2          | 16,384                      | 0                    | 16,384   |

Matching irradiance/confidence assertions attribute black pixels to rejection rather than absent source lighting. This is a controlled single-probe reproduction, not an exact reconstruction of a particular live scene.

Validation receipts: [test log](../artifacts/wall-visibility-reproduction.log) and [TRX](../artifacts/TestResults/wall-visibility-reproduction.trx).

## Local world tracing and cache handoff

The screen-probe trace shader now resolves screen misses through local voxel geometry before sampling distant world radiance. The subsequent direct-visibility repair is recorded separately below.

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

These tests establish controlled shader and publication correctness. They do not measure live frame time, main-thread snapshot cost, production update-budget pressure, camera-motion history behavior or real-scene appearance. Unsupported geometry remains unresolved by design. Direct irradiance fallback and the irradiance viewer use the subsequent repair described below.

### Compact textures and region-ring ownership

Local lighting and diffuse/emission material textures now use normalized RGBA8. The region texture uses R8UI readiness only; the CPU owns slot identities and versions, and the shader derives wrapping offsets from the integer anchor.

[Ring regressions](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Ring.cs) verify positive/negative single-region moves on each axis, diagonal movement with three regions per axis, and movement beyond the full ring extent. They check exact readiness preservation for overlapping regions, clear newly assigned slots, reject delayed uploads for evicted regions, then exercise shader sampling after replacement publication. A byte-upload control verifies tightly packed 3D rows and restoration of the caller's unpack alignment.

[Lighting controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.Lighting.cs) test RGBA8 quantization at zero, dim intensities and full intensity. Existing local emission, sealed-room, stale-version, large-coordinate and parallax cases remain applicable. Quantization is intentional: normalized steps are 1/255, while shader output and boosted emission remain HDR.

Compact-format validation: **47 local tests and 15 texture/format/upload regressions passed, with zero failures or skips**. The build succeeded. Receipts: [local tests](../artifacts/local-trace-compact-final.log), [local TRX](../artifacts/TestResults/local-trace-compact-final.trx), and [texture regressions](../artifacts/local-trace-compact-textures.log). These checks validate correctness and quantization behavior; no live performance measurement was made.

## Direct irradiance visibility repair

Direct irradiance now uses local voxel segment tracing instead of nearest directional-depth rejection. The shared [consumer harness](../VanillaGraphicsExpanded.Tests/GPU/Fixtures/DirectWorldProbeVisibilityTestBase.cs) binds the production geometry/readiness textures and runs debug modes 31/32/33, atlas gather, and SH9 gather. Gather cases explicitly invalidate screen probes to exercise world fallback.

[Direct visibility cases](../VanillaGraphicsExpanded.Tests/GPU/LumOnDirectWorldProbeVisibilityTests.cs) require sealed rooms to reject an exterior bright cache, open doorways to restore its lighting, cache-only suppression to preserve selected confidence, and unavailable resources or exhausted budgets to stay dark. Clear geometry remains visible even when the nearest recorded angular depth is deliberately too short.

[Neighborhood controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnDirectWorldProbeVisibilityTests.Neighborhoods.cs) verify that blocked red neighbors do not contaminate visible green neighbors, including ring-remapped storage. Enclosing corners block exterior probes. Positive and negative world offsets of 16,777,216 preserve both closed-wall and open-doorway outcomes.

The dense wall test now requires zero false rejections rather than merely reproducing them. Unit radiance must retain its diffuse integral and source confidence; the debug display must retain pi/(1+pi).

These tests exercise production shaders over controlled full-cube geometry. They do not establish live visual results, runtime traversal cost, arbitrary partial-block support, or visibility beyond the local published window. The original three unrelated broader-suite failures and their unchanged-revision baseline remain recorded above.

### Direct visibility validation results

**Initial implementation validation: 39 direct-visibility cases and 216 regression cases passed, zero failures or skips.** The regression selection includes local tracing, prior world-probe controls, shader compilation, UBO/layout binding and debug routing. Build succeeded with the same six unrelated warnings.

| Consumer                    | Wall inset | Exact clear segments | False rejections | Accepted |
| --------------------------- | ---------- | -------------------- | ---------------- | -------- |
| Irradiance/confidence debug | 0.01       | 16,384               | 0                | 16,384   |
| Irradiance/confidence debug | 2          | 16,384               | 0                | 16,384   |
| Atlas gather fallback       | 0.01       | 16,384               | 0                | 16,384   |
| SH9 gather fallback         | 0.01       | 16,384               | 0                | 16,384   |

Receipts: [direct visibility tests](../artifacts/direct-visibility-final.log), [direct visibility TRX](../artifacts/TestResults/direct-visibility-final.trx), [regression tests](../artifacts/direct-visibility-regression.log), and [regression TRX](../artifacts/TestResults/direct-visibility-regression.trx).

### Clipmap and local-window regression validation

The reusable atlas fixture now supports multiple vertically stacked levels. The direct-consumer harness accepts per-level origins and ring offsets. Its depth and normal textures now cover the full declared screen size: gather uses integer guide fetches, so the previous 1x1 inputs allowed undefined out-of-range reads. The corrected fixture supersedes the earlier direct-consumer receipts.

[Clipmap controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnDirectWorldProbeVisibilityTests.Clipmaps.cs) add 18 cases across the irradiance viewer and both gather modes:

- Red fine-level and green coarse-level lighting retain the diffuse integral through the fine interior, overlap band, and fine-volume boundary, using different nonzero ring offsets.
- Walls reject both levels; an occluded coarse level cannot replace visible fine lighting; unavailable fine metadata selects visible coarse lighting.
- Probe and receiver positions beyond either local-window X boundary remain unresolved, while an inside control remains lit.
- Moving the geometry window retains overlapping published cells, rejects stale air in a reused slot, rejects newly published solid geometry, and accepts the slot after clear geometry is published.

**57 direct-visibility cases passed, zero failures or skips**, including all prior direct controls and the four dense wall cases. The corrected guide textures retain zero false rejections across all 16,384 samples in each wall case. No production shader changes were needed.

The existing regression selection also passed all **216 cases**, zero failures or skips, after the atlas fixture extension. Build succeeded with the same six unrelated warnings.

Receipts: [focused test log](../artifacts/direct-visibility-clipmaps.log), [focused TRX](../artifacts/TestResults/direct-visibility-clipmaps.trx), [regression log](../artifacts/direct-visibility-clipmaps-regression.log), and [regression TRX](../artifacts/TestResults/direct-visibility-clipmaps-regression.trx). Live scene appearance and runtime performance remain unverified.

### Lighting-effect display

The effect view compares linear luminance of normal and world-suppressed lighting. Black indicates zero luminance difference, orange an increase, blue a decrease, and purple unavailable comparison data. The debug panel offers 1x, 10x, 100x and 1000x gain, with 10x as the default. Gain changes only display sensitivity and does not reset the lighting histories.

The GPU controls cover positive, negative, zero, mixed-channel and weak differences, gain amplification, packed-parameter independence, and unavailable comparison output. Equal-luminance color changes are intentionally not represented by this luminance view.
Validation: 47 focused tests passed with zero failures or skips. Build and seven shader compilations succeeded, with the existing six unrelated warnings. Receipts: [test log](../artifacts/world-probe-effect-colors.log) and [TRX](../artifacts/TestResults/world-probe-effect-colors.trx).

### Camera-bob coordinate regression

The [moving-camera GPU fixture](../VanillaGraphicsExpanded.Tests/GPU/LumOnDirectWorldProbeVisibilityTests.CameraMotion.cs) renders fixed clear and blocked segments beside a voxel boundary. It changes the inverse-view camera height and compensates the view-space receiver, so the world geometry and reconstructed player-relative receiver remain stationary. The harness uses the production CPU bridge to populate the frame UBO.

Nine cases cover irradiance debug, atlas gather and SH9 gather at world offsets zero and plus/minus 16,777,216, with fractional player origins. Each case cycles through zero, positive, negative and restored bob and checks both lighting and occlusion. The old camera-derived bridge failed all nine cases by rejecting clear segments. The [unit controls](../VanillaGraphicsExpanded.Tests/Unit/LumOn/LumOnFrameWorldSpaceBridgeTests.cs) additionally cover negative chunk boundaries and fractional precision beyond the exact-integer range of float.

Before-fix receipts: [log](../artifacts/world-probe-camera-bob-before.log) and [TRX](../artifacts/TestResults/world-probe-camera-bob-before.trx).

After the repair, all **73 focused tests** passed, including the nine previously failing camera-bob cases, the existing direct-visibility controls and signed-origin unit cases. The broader selection passed **222 regression tests**. Both selections had zero failures or skips; build succeeded with the same six unrelated warnings.

After-fix receipts: [focused log](../artifacts/world-probe-camera-bob-after.log), [focused TRX](../artifacts/TestResults/world-probe-camera-bob-after.trx), [regression log](../artifacts/world-probe-camera-bob-regression.log), and [regression TRX](../artifacts/TestResults/world-probe-camera-bob-regression.trx). Live in-game appearance remains unverified.

### Screen-hit versus off-screen lighting

[Screen-hit controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceFunctionalTests.ScreenHits.cs) use the reusable local trace harness with a constant screen-depth plane and the same published voxel room. A screen-only control identifies directions that actually hit the screen plane; assertions then compare those directions against the off-screen local result. This prevents a test that accidentally exercises only misses from passing.

Before the repair, two lit-room cases returned zero on screen versus approximately 0.251 and 1.0 off screen. An unavailable-source case incorrectly published confidence 1. All three failed; the genuinely dark control passed. Receipts: [before log](../artifacts/screen-hit-lighting-before.log) and [before TRX](../artifacts/TestResults/screen-hit-lighting-before.trx).

Controls additionally require cache suppression to preserve local lighting and metadata, material emission to be counted once with its GI boost, visible emission to survive an unavailable local scene, and a known opaque hit with missing material to reject borrowed screen emission. These are controlled shader results, not a live performance or visual capture.

After-fix validation: **54 local-tracing tests passed**, zero failures or skips. The final-revision broader selection passed **306 tests**, zero failures, with two existing explicit skips for indirect tint and distance falloff. Build succeeded. Receipts: [local tests](../artifacts/screen-hit-lighting-after.log), [local TRX](../artifacts/TestResults/screen-hit-lighting-after.trx), [regression log](../artifacts/screen-hit-lighting-regression.log), and [regression TRX](../artifacts/TestResults/screen-hit-lighting-regression.trx).

Live scene appearance and the added traversal cost remain unverified. The WP lighting-effect view compares cached world radiance specifically; the repaired local hit lighting remains present in both diagnostic branches.

### Trace-outcome diagnostic

The Probes panel's **Probe-Atlas Trace Outcome** view displays the latest recorded trace classification for each atlas direction. It reads the raw trace metadata rather than deriving an outcome from temporally filtered radiance. Directions not traced during the current update retain their previous classification.

| Color   | Meaning                                                                                       |
| ------- | --------------------------------------------------------------------------------------------- |
| Red     | Hit distance of 0.02 blocks or less; possible self-intersection                               |
| Green   | Resolved hit with a radiance component above 0.00001                                          |
| Yellow  | Resolved hit with radiance at or below that threshold                                         |
| Magenta | Unavailable lighting or geometry, exhausted traversal budget, or zero-confidence cache sample |
| Cyan    | World-cache sample with positive confidence                                                   |
| Blue    | Legacy sky approximation                                                                      |
| Black   | No recorded outcome                                                                           |

Near-zero hits take priority over lighting readiness; red alone does not prove self-intersection. Yellow establishes that the trace accepted a dark lighting result, not that the live scene should physically be dark. These outcomes are encoded in existing metadata bits 16 through 18 without changing radiance, confidence, or allocating another atlas. Cache suppression preserves the classification.

For the indoor-black investigation, allow the atlas directions to update while viewing the affected room, then record the dominant colors. The live cause remains unresolved until this diagnostic is observed in the affected scene.

Diagnostic validation: **40 focused tests passed**, zero failures or skips. The controls exercise actual near-zero, lit, dark, unavailable, budget-limited, cache and sky traces; all seven display colors; renderer routing and buffer requirements; and outcome preservation through temporal filtering. Build succeeded with six existing warnings. Receipts: [focused log](../artifacts/trace-outcome-diagnostic.log) and [focused TRX](../artifacts/TestResults/trace-outcome-diagnostic.trx). Live in-game colors remain unverified.
The complete local-tracing selection also passed all **61 tests**, zero failures or skips, including the earlier screen-hit and cache-suppression controls. Receipts: [local regression log](../artifacts/trace-outcome-local-regression.log) and [local regression TRX](../artifacts/TestResults/trace-outcome-local-regression.trx).

### Captured material-readiness reproduction

The live observation motivating this case is mostly magenta trace outcomes, occasional red, and apparently normal world-probe irradiance. The existing synthetic GPU scenes assign material identity 1 directly, so they do not exercise live cell capture's dependency on material-registry readiness.

The new candidate scenario uses a loaded, lit, opaque cube room. It runs production cell capture, material resolution, GPU scene publication, and the actual probe trace shader. Capture before material readiness stores opaque geometry with material identity zero; the local hit shader rejects that identity as unavailable lighting. Updating the material registry alone does not rewrite captured cell identities. A ready-from-start control and recapture of the unchanged room distinguish this dependency from geometry, missing chunks, light values, or traversal limits.

This is a controlled candidate mechanism, not confirmation of the live root cause. Normal startup builds derived material data on the block-texture event; the test does not establish that the affected game session captured its room before that event. It does not reproduce the complete scene scheduler, occasional red hits, or mostly white temporal confidence. No rendering behavior is changed by this reproduction work.

[The reproduction tests](../VanillaGraphicsExpanded.Tests/GPU/LumOnLocalTraceMaterialReadinessTests.cs) passed all **5 cases**. In each of the three missing-material variants (surface missing, derived lookup missing, both missing), the same room's real CPU world-probe integrator returns lit samples while every tested local GPU direction reports a geometric hit, zero radiance, zero confidence, and outcome 4. GPU region readiness remains 1. Registry readiness alone leaves the scene revision and results unchanged; recapture restores outcome 2, confidence 1, and approximately 0.251 RGB. Ready-from-start capture passes. The partial-block control remains unavailable by the current geometry-support contract.

Reusable support consists of [production cell publication](../VanillaGraphicsExpanded.Tests/GPU/Fixtures/LocalTraceVoxelFixture.cs), the [shared shader harness](../VanillaGraphicsExpanded.Tests/GPU/Fixtures/LocalTraceShaderTestBase.cs), and a [scoped material-readiness fixture](../VanillaGraphicsExpanded.Tests/Fixtures/WorldProbes/ScopedPbrMaterialFixture.cs). Material data uses the production derived-surface builder; a test-only reflection seam installs its result and restores the original registry state. The collection runs exclusively to protect singleton users. Region scheduling and game event ordering are outside this fixture.

The full local-tracing selection passed **66 tests**, zero failures or skips. Receipts: [reproduction log](../artifacts/local-material-readiness-reproduction.log), [reproduction TRX](../artifacts/TestResults/local-material-readiness-reproduction.trx), [regression log](../artifacts/local-material-readiness-regression.log), and [regression TRX](../artifacts/TestResults/local-material-readiness-regression.trx). These passing characterization tests assert the current failure mechanism and its recovery control; they are not evidence of a production fix.

### Local-tracing geometry viewer

Select **Probes → Local-Tracing Geometry** to inspect the actual uploaded local voxel scene. Camera rays traverse the production geometry and region-readiness textures, independently of screen depth, probe radiance, and material lighting. No separate debug voxel upload is created. The first non-air or unavailable cell terminates the ray.

| Color | Meaning |
| --- | --- |
| Cyan, with face shading and dark voxel edges | Supported opaque voxel with a nonzero material identity |
| Orange, with face shading and dark voxel edges | Opaque voxel whose material identity is zero |
| Magenta | Unsupported or unavailable cell geometry in a published region |
| Purple | Unpublished region |
| Blue | Local GPU scene unavailable |
| Black | Ray misses the volume or leaves it through known air |
| Yellow | Debug traversal limit reached |

The diagnostic clips rays to the local volume and uses its own 2048-cell traversal limit; it does not visualize the shorter production ray budget. It uses integer world chunks plus fractional camera-relative coordinates, preserving large-world precision and camera translation. Unsupported and unpublished cells stop traversal deliberately so missing data cannot masquerade as empty space. Cyan denotes occupancy and material identity, not verified material lighting readiness.

Validation: **182 focused/regression tests passed**, zero failures or skips; build succeeded with six existing warnings. Twelve new GPU cases cover actual uploaded geometry/readiness, missing material identities, camera movement, origins at plus/minus 16,777,216, fractional-origin equivalence, outside-volume clipping, and dedicated/monolithic shader parity. Existing direct-visibility, shader-compilation, UBO, and UI routing checks also passed. The traversal-limit color and live in-game appearance remain unverified. Receipts: [test log](../artifacts/local-geometry-debug.log) and [TRX](../artifacts/TestResults/local-geometry-debug.trx).
