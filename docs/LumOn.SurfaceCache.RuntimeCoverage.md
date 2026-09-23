# Production runtime lighting coverage

Status: the original migration and its six tasks are verified below. The task list now includes additional dependency-injection, reflection-reduction and resource-ownership work. The implementation boundaries and receipts here describe the existing migration; they do not establish completion of those additions.

## Contract and traceability

Section 12 of `LumOn.SurfaceCache.TestCoverage.todo` is governed by `LumOn.SurfaceCache.LightingContract.md` (read in full).

| Task | Controlling contract | Implementation and verification obligation |
| --- | --- | --- |
| Scenario inventory | Geometry-hit consumers; final-lighting verification | Account for each original scenario and its confidence, distance, numerical and lifetime assertions. |
| Shared runtime bootstrap | Atlas/publication; geometry-hit consumers | Exercise production shader registration and mod-owned renderer/provider composition. |
| Production ownership | Atlas/publication; estimator/history | Registered callbacks own lighting resources, ordering, bindings, scheduling and lifetime; fixtures supply engine sources and observe outputs. |
| Scenario migration | All lighting terms; estimator/history; geometry-hit consumers | Both gathers, bounded source transitions, emission, bounce, darkness, unavailable data, doorway and history. |
| Cleanup | Section 12 ownership requirements | Remove duplicated runtime program map and broad manual pipelines; retain narrowly scoped component assertions explicitly. |
| Validation | All above | Normal shader-enabled build, cross-collection GPU runs, teardown/restart, second review and independent audit. |

## Engine boundary

The headless host supplies terrain raster/feedback, voxel source data, material tables and camera state. It invokes the existing production registration entry and dependency initialization. The full game startup (Harmony installation, UI, networking, terrain tessellation and `ClientMain` bulk snapshot adapter) is outside this harness. Test-only reflection may bridge private engine-adapter fields; it must not replace provider wiring, pass execution, shader parameters, scheduling or history. Full PBR composition is subsequent work.

## Scenario inventory

The initial inventory is retained here while migration proceeds. Original assertions must remain until their replacement or explicit component classification is verified.

| Original scenario | Assertions to preserve | Intended coverage |
| --- | --- | --- |
| OffscreenLightRemovalAndRestorationReachFinalPixels | All boundaries light/dark; >32 hits; valid-dark confidence 1; exact restored component output | Runtime source transitions plus scoped deterministic hit component |
| SealedRoomRejectsBrightExterior | Dark at all boundaries; enclosing hits and confidence 1 | Runtime dark neighboring rooms plus hit component |
| EmissiveOnlyCacheLightsFinalPixels | Emission with zero albedo; trace 4 +/- .01; disabled source dark | Runtime emission policy plus numerical hit component |
| MultipleBouncesReachFinalPixels | Two successive increases >10%, >1% | Runtime progressive publication plus isolated estimator component |
| InvalidPagesDoNotReachFinalPixels | Missing/stale mapping dark; every confidence zero | Runtime streaming plus stale lookup component |
| RecreatedDarkCacheDoesNotReuseBrightLighting | Bright/dark/bright across resource replacement | Runtime lifetime transitions |
| RetainedHistoryFollowsSourceLighting | Exact untouched RGBA, unchanged revision, reset on source changes, eight-frame sweep, restored RGB .01 | Runtime history plus temporal component |
| BounceGenerationPreservesAndAccumulatesHistory | No reset, untouched directions exact, fresh samples blend | Runtime progressive history plus temporal component |
| UnavailabilityAndDependencyRevisionRejectHistory | Reset and zero confidence; dependency-only reset without geometry change; untraced directions cold | Runtime dependency transitions plus temporal component |
| CacheResourceRecreationRejectsPopulatedHistory | Populated history rejected; dark replacement and recovery | Runtime lifetime plus temporal component |
| DoorwayClosureAndReopeningReachRetainedFinalPixels | Door occupancy, exterior light unchanged, dark close, shorter hit distances, resolved confidence, restored RGB .01 | Runtime doorway plus temporal component |
| ProducedLightingReachesWorldProbeAtlasAcrossLightChanges | Actual CPU hits, source transitions, dark confidence retained, unavailable failure | Runtime world source transitions plus transport component |
| WorldProbeCacheContributionReachesFinalPixels | World atlas to screen misses and both gathers, off/on | Runtime mixed/source cases |
| FailedCaptureRetriesAfterGeometryBecomesAvailable | GPU failure marker then retry without replacement page | Retained capture scheduling component |
| RuntimePublicationAndInvalidationReachWorkerHits | Single scheduled hit, dependency revisions, resource retirement, teardown | Runtime lifecycle plus single-hit transport component |

## Implemented ownership boundaries

`RuntimeLightingPrograms` forwards engine registration and lookup calls to the production `VanillaGraphicsExpandedModSystem.LoadShaders` entry. It has no shader-class map. Lookup observations record programs actually requested by consumers, separately from startup registration.

`RuntimeLightingHost` calls `WorldProbeModSystem.StartClientSide` and the main mod's normal `LumOnModSystem.SetDependencies` handoff. Those owners construct buffers/renderers and connect providers. The fixture reads created owners for assertions; it does not set provider fields. Headless setup substitutes only the existing camera readers and geometry source factory, before any frame executes, because the real source adapter requires a live `ClientMain` world. The source supplies controlled voxel/material/light records and retains the real source cache, partition scheduler and GPU publication. Probe collision reads use the controlled engine accessor.

Startup normally clamps world-probe resolution to at least eight. The tiny enclosure fixture reapplies its explicitly configured one/two/four-probe-axis topology after startup and requests normal resource recreation. This keeps bounded test work inside the authored resident scene; configuration lower-bound validation is not claimed by these rendering scenarios.

The primary terrain framebuffer and G-buffer are simulated engine inputs, not lighting intermediates. Frames upload terrain depth, normals/materials and patch feedback, then execute registered callbacks in engine order. Production owns anchors, all lighting pass resources, shader variants/bindings, barriers, cache generations, history clearing/swaps, worker/query/upload scheduling and disposal. The test uniform allocator supplies the engine-wide uniform service; it never writes per-pass lighting parameters.

The standalone `SurfaceCacheRuntimeFixture` mode remains focused cache lifecycle/capture coverage. Broad consumer tests use its engine inputs with mod-owned composition. `SurfaceLightingHitComponentTests` retains fixed-ray numerical/validity checks; `SurfaceLightingTemporalComponentTests` retains exact untouched-direction, blend, rejection, confidence and hit-distance checks. Neither component fixture assembles filtering/gather/upsample/composition. `SurfaceLightingWorldProbeTransportTests` isolates CPU-hit/query/upload transport and capture retry semantics. The duplicated downstream pipeline helper has been removed.

`LumOnCombineFunctionalTests` remains dedicated composition coverage. Migrated E2E output is the production full-resolution indirect texture passed to composition, not an independently assembled `lumon_combine` result. Full production PBR accounting remains the next planned item.

## Coverage mapping after migration

| Original assertion group | Current runtime proof | Retained precise component proof |
| --- | --- | --- |
| Source off/on at trace, filter, gather and output; restored illumination | `SurfaceLightingRuntimeScenariosTests.RetainedHistoryFollowsSourceLighting` (both gathers, every final RGB channel and exact restored final output); `RegisteredConsumersPropagateLightingAcrossLifetime` also observes world atlas | `SurfaceLightingHitComponentTests.OffscreenLightRemovalAndRestorationReachHitRadiance`: >32 actual hits, dark confidence exactly one, restored directional output exact |
| Bright exterior versus sealed darkness | `SurfaceLightingSpatialRuntimeTests.SealedDarkNeighborsRemainDarkWhileMoving` (both gathers, all final RGB) | `SurfaceLightingHitComponentTests.SealedRoomRejectsBrightExterior`: enclosed hit confidence and exact darkness |
| Emission with zero albedo; source-policy removal | `EmissiveOnlyCacheLightsRuntimePixels` (both gathers, all final pixels and intermediate boundaries) | `EmissiveOnlyCacheLightsHitRadiance`: outgoing radiance 4 +/- .01, no reflected input |
| Successive bounce energy | `ProgressiveBounceReachesRuntimePixels` (both gathers): two single-generation budget admissions increase the reference final-pixel channel by >10% then >1%, unchanged history revision | `MultipleBouncesReachHitRadiance`: exact controlled first/second bounce increases of >10% / >1%; temporal component separately requires actual blending |
| Missing and stale lighting | `UnavailableGeometryDiffersFromValidDarkness` and `SignedStreamingRejectsReusedUnavailableSlots`: real unload/reload, slot reuse, no stale final/world illumination, recovered confident darkness | `InvalidPagesDoNotReachHitRadiance`: both unavailable and stale lookup cases, every directional confidence zero |
| Resource recreation, teardown/restart | `RegisteredConsumersPropagateLightingAcrossLifetime` (both gathers): retired cache/probe allocations, increased history revision, cleared world/screen/final outputs, republished lighting; `RecreatedDarkCacheRejectsRetainedFinalLighting` replaces the cache while dark with the same consumers and compares restored final RGB within .01 | `RecreatedDarkCacheDoesNotReuseBrightLighting`, `CacheResourceRecreationRejectsPopulatedHistory` retain deterministic bright/dark/bright resource identities |
| Retained directions and dependency invalidation | Runtime retained-history, source transitions, pending GPU/CPU cases, ring continuity | `SurfaceLightingTemporalComponentTests`: exact untouched RGBA, genuine blended fresh direction, dependency-only invalidation without geometry change, cold untraced directions and zero confidence |
| Door closure/reopening, unchanged exterior source and occlusion | `DoorwayClosureAndReopeningReachRuntimePixels` (both gathers): source geometry edit, history revision changes, all final/intermediate RGB dark on closure and lit after reopening, restored final RGB within .01 | `DoorwayClosureAndReopeningReachRetainedProbeHistory`: exact occupancy/exterior source invariants, shortened distances, all hit confidence one, restored directional RGB within .01 |
| World hits, confidence and publication | Actual workers and GPU queries in consumer lifetime, pending-work, upload-budget and mixed-probe runtime cases | `SurfaceLightingWorldProbeTransportTests`: every CPU direction has a hit descriptor, valid-dark confidence unchanged, unavailable query failure, single scheduled hit uses actual published resource generations |
| Failed capture retries | Focused cache callback fixture retains exact GPU failure marker and recovery without a replacement page | `FailedCaptureRetriesAfterGeometryBecomesAvailable` retained in transport suite |

The old fixed-anchor final/composition assertions are retained at their owning boundaries: exact final source restoration, .01 final doorway/resource-restoration tolerance and both final-pixel bounce thresholds are checked on production runtime output. Hit/temporal components additionally retain exact directional, confidence and occlusion checks. This removes the fabricated-anchor downstream pipeline rather than keeping it as a second E2E implementation. Composition-specific assertions remain in `LumOnCombineFunctionalTests`; full cache-originating PBR numerical accounting is not claimed here.

## Acceptance and review

Runtime scenarios use at most 160 frames for capture admission and at most 160 for consumer settlement (including an eight-frame complete directional observation sweep). Deterministic comparisons pause the producer until all requested pages are captured, admit one full seed or bounce through its normal registered frame, then pause further bounces while consumers settle. This uses ordinary page/texel budgets, not manual dispatches or injected cache values. Darkness is <= .0001 in every RGB channel. Lit final output requires every RGB channel > .001; intermediate outputs must contain finite produced light. Valid-dark recovery additionally requires world confidence >= .25 and populated screen confidence. The progressive-bounce case requires exactly one published generation per admission, reference final-pixel channel increases >10% then >1%, and no history revision change. Source restoration is exact at final output; doorway and recreated-cache restoration use the existing .01 RGB tolerance. Exact directional/occlusion/numerical assertions retain their previous component tolerances.

Second implementation review: verified that the production diff adds only terrain-bridge renderer unregistration on disposal; no test APIs or state were added. Verified production shader registration and provider composition replace the fixture copies; broad tests do not create pass intermediates or upload lighting. Reviewed every original scenario against the mapping above, preserved focused confidence/occlusion/retention checks, and removed the shared manual downstream pass chain. Shader lookup observations now distinguish actual consumer selection from startup registration.

Independent audit identified final-pixel assertion preservation gaps in the first migration. These were addressed with the deterministic budget-controlled runtime comparisons above and an explicit dark-cache recreation scenario. The final combined verification below includes those corrections.

Initial executed evidence: normal SPIR-V-enabled build; `artifacts/surface-runtime-migration/migration-first.{log,trx}` passed 31/31 with zero skips (consumer runtime 6, new runtime scenarios 10, hit component 7, temporal component 5, world transport 3). The later strengthened numerical run passed 12/12 (`migration-numeric.{log,trx}`) and spatial run passed 10/10 (`migration-spatial.{log,trx}`).

Final executed evidence: the normal SPIR-V-enabled application/test build verified current shader binaries and contracts. The combined cross-collection regression passed **101/101, zero skips**: composition 11, gather 18, temporal shader 17, cache runtime 5, consumer runtime 6, hit component 7, producer 7, runtime scenarios 12, spatial runtime 10, temporal component 5, world-probe transport 3. Receipts: `artifacts/surface-runtime-migration/migration-final-verified.{log,trx}`. All executable changes were included; subsequent source edits only organized regions/comments. Five existing unrelated analyzer warnings remain. These are headless GPU results, not live-game validation.

Second review was repeated after the strengthened final-output checks and confirmed the scenario mapping, source-only controls, complete capture observations, retained confidence/occlusion assertions and absence of production test hooks. Independent completion audit passed with high confidence after directly reviewing the final log/TRX and coverage mapping. All six implementation tasks are satisfied; no required findings or evidence gaps remain.
