# Production runtime lighting coverage

Status: the original migration and seven earlier dependency/resource-ownership tasks have recorded validation below. Section 12 is reopened for production shader-interface usage and allocation reuse. Existing component exceptions and per-input owner allocation do not satisfy these newly added requirements. Earlier receipts preserve all original 101 cases and disclose 16 baseline-confirmed pre-existing failures and three existing skips; they do not establish completion of the new tasks.

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

The headless host supplies terrain raster/feedback, voxel source data, material tables and camera state. It invokes the existing production registration entry and dependency initialization. The full game startup (Harmony installation, UI, networking, terrain tessellation and `ClientMain` bulk snapshot adapter) is outside this harness. Constructor injection supplies the camera and voxel adapter. Remaining reflection is confined to the explicitly listed engine/material fixtures and a read-only pending-query observation; it never replaces provider wiring, pass execution, shader parameters, scheduling or history. Full PBR composition is subsequent work.

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

`RuntimeLightingPrograms` forwards engine registration and lookup calls to the production `VgeShaderPrograms.RegisterAll` entry. It has no shader-class map. Lookup observations record programs actually requested by consumers, separately from startup registration.

`RuntimeLightingHost` calls `WorldProbeModSystem.StartClientSide` and the main mod's normal `LumOnModSystem.SetDependencies` handoff. Those owners construct buffers/renderers and connect providers. The fixture observes registered renderer callbacks and existing buffer accessors; it does not read or write their private dependency fields. Headless setup supplies camera readers, configuration and the geometry source factory through ordinary mod constructors, because the real source adapter requires a live `ClientMain` world. The source supplies controlled voxel/material/light records and retains the real source cache, partition scheduler and GPU publication. Probe collision reads use the controlled engine accessor.

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

Original migration second implementation review: verified that the production diff adds only terrain-bridge renderer unregistration on disposal; no test APIs or state were added. Verified production shader registration and provider composition replace the fixture copies; broad tests do not create pass intermediates or upload lighting. Reviewed every original scenario against the mapping above, preserved focused confidence/occlusion/retention checks, and removed the shared manual downstream pass chain. Shader lookup observations now distinguish actual consumer selection from startup registration.

Independent audit identified final-pixel assertion preservation gaps in the first migration. These were addressed with the deterministic budget-controlled runtime comparisons above and an explicit dark-cache recreation scenario. The final combined verification below includes those corrections.

Initial executed evidence: normal SPIR-V-enabled build; `artifacts/surface-runtime-migration/migration-first.{log,trx}` passed 31/31 with zero skips (consumer runtime 6, new runtime scenarios 10, hit component 7, temporal component 5, world transport 3). The later strengthened numerical run passed 12/12 (`migration-numeric.{log,trx}`) and spatial run passed 10/10 (`migration-spatial.{log,trx}`).

Final executed evidence: the normal SPIR-V-enabled application/test build verified current shader binaries and contracts. The combined cross-collection regression passed **101/101, zero skips**: composition 11, gather 18, temporal shader 17, cache runtime 5, consumer runtime 6, hit component 7, producer 7, runtime scenarios 12, spatial runtime 10, temporal component 5, world-probe transport 3. Receipts: `artifacts/surface-runtime-migration/migration-final-verified.{log,trx}`. All executable changes were included; subsequent source edits only organized regions/comments. Five existing unrelated analyzer warnings remain. These are headless GPU results, not live-game validation.

Second review was repeated after the strengthened final-output checks and confirmed the scenario mapping, source-only controls, complete capture observations, retained confidence/occlusion assertions and absence of production test hooks. Independent completion audit passed with high confidence after directly reviewing the final log/TRX and coverage mapping. All six implementation tasks are satisfied; no required findings or evidence gaps remain.

## Dependency and resource ownership follow-up

Task traceability: the seven additional Section 12 tasks inherit the lighting contract's publication/lifetime, geometry-hit consumer and retained-history requirements. The migration must preserve every existing numerical assertion and the 101-case baseline. Executed evidence includes the affected runtime/component suites, allocation/recreation checks, an untouched-HEAD comparison, second review and independent completion audit.

Inventory and implemented ownership:

| Fixture family / dependency | Existing owner or boundary | Migration |
| --- | --- | --- |
| RuntimeLightingHost private camera/source/config writes | Mod composition dependencies; engine camera and voxel adapter | Constructor injection; retain default game adapters and normal mod wiring. Read created renderers from actual event registrations and existing buffer accessors. |
| RuntimeLightingPrograms private bootstrap invocation | Main mod shader registration | Extract the production registration responsibility and reuse it; mock engine shader registry with Moq. |
| Pending GPU query observation | Private asynchronous transport state | Retain one read-only field lookup: outputs cannot distinguish a submitted GPU batch from work still on the CPU. Read Pending without polling or mutating its fence. |
| NearFieldShaderTestBase probe inputs/output | LumOnBufferManager, world-probe resources, GBufferManager | Populate production allocations; retain fixed-ray parameters as deliberate component inputs. |
| Temporal component anchors/mask/velocity/jitter | Existing retained LumOnBufferManager and PMJ texture owner | Reuse owners rather than allocate duplicate formats. |
| Surface enclosure/page fixtures | Physical surface atlas resources and virtual page table | Reuse production atlas allocation; extract only cohesive allocations needed independently of residency/budgets. |
| Runtime primary depth/color, terrain upload/feedback | Engine-owned framebuffer boundary | Central engine-input fixture owns simulation formats and upload layout. |
| Older isolated shader functional cases | Per-pass resource owners and shared shader input helpers | Shared normal/material helpers use GBufferTextures; engine color/depth helpers use EngineTerrainBuffers. Visibility inputs and paired diagnostic intermediates use production owners. Explicit remaining component exceptions are listed below. |
| Engine platform/material registry reflection | Concrete engine singleton and immutable material publications | Review separately from lighting dependency mutation; document unavoidable engine-boundary inspection/setup until real public loading paths can represent the controlled scene. |

### Reflection and engine services

`RuntimeLightingHost` no longer changes global configuration or renderer fields. `RuntimeLightingPrograms` calls the same extracted registration entry as main-mod startup/reload. `RuntimeEngineServices`, the shader registry, world accessor, mod loader and probe block accessor use typed Moq setups. The headless event dispatcher retains its public-interface adapter to record arbitrary subscription add/remove pairs, order actual registered renderers and drive teardown; this is event-loop simulation, not private-state access. `BinaryShaderApiFixture` retains its existing asset/logger forwarding adapter, shared with binary reload tests. Neither adapter selects lighting passes or writes lighting dependencies.

Remaining reflection is evaluated individually:

| Site | Reason and boundary |
| --- | --- |
| `SurfaceLightingConsumerRuntimeFixture.surfaceQueries` | Read-only observation proves invalidation occurs while a submitted GPU batch is pending. Final pixels cannot distinguish this from queued CPU work. No mutation, forced completion, fence polling or new production accessor. |
| `EngineShaderPlatformScope` | An uninitialized concrete engine platform supplies the shader uniform singleton. Its real constructor starts native window/audio subsystems; the property setter is not exposed through the abstract engine API. Restores the original platform/current shader. Native platform initialization is outside coverage. |
| `ScopedPbrMaterialFixture` | Saves/restores the derived material array and one surface dictionary entry. The production lookup builder creates authored face data. Public rebuild/clear paths rebuild global material state and increment generations; they cannot restore the exact prior singleton state or independently withhold surface versus derived publication. This fixture models already-loaded material publications, not disk-cache loading. |

The normal mod constructors continue to read `ConfigModSystem.Config`, `LumOnCameraState.Read` and `TraceGeometryWorldSource`. Their injected constructors are ordinary composition dependencies, not test control APIs. Live-reload creation paths use the same dependencies.

### Allocation and execution inventory

| Family | Resource ownership and retained control |
| --- | --- |
| Consumer runtime, runtime scenarios, spatial runtime | Mod owners allocate everything downstream. `EngineTerrainBuffers` owns simulated engine primary color/depth/output, and uploads authored terrain/feedback to `GBufferManager` attachments. Frames invoke registered callbacks only. |
| Fixed-ray near-field/hit and temporal components | `LumOnBufferManager` owns anchors, trace/history/meta/filter targets, mask, velocity and HZB. `LumOnWorldProbeClipmapGpuResources` owns synthetic world inputs. `GBufferTextures` owns terrain guides. Temporal fixture retains its production manager and PMJ owner. Explicit rays/parameters and the trace/temporal operations remain under component control; no gather/composition E2E claim. |
| Direct-world visibility components | Production world resources, screen manager, G-buffer companions and engine fixture allocate inputs/output. Authored world atlas values, fixed matrices, visibility budgets and single consumer invocation isolate interpolation/occlusion. |
| Surface enclosure and shared page | `SurfaceAtlasTextures` centralizes depth/material/direct/indirect/outgoing allocations, also used by `LumonScenePhysicalAtlasGpuResources`. Residency and byte admission remain in the physical owner. `LumonScenePageTableGpuResources` owns virtual page storage. Fixtures control page work and explicit producer dispatch for numerical component assertions. |
| Base shader scene inputs | The per-texture helpers and owner list were removed. `LumOnShaderFunctionalTestBase.SceneInputs` owns one lazy `ShaderSceneInputs` per test scene, containing engine targets and production G-buffer companions. `SharedGeometryDebugTests` borrows depth, normal, patch and output resources across repeated renders. Only the scene owner disposes them; resizing retires previous borrows. |
| Paired world-probe diagnostic component | Production screen managers own independent branches and intermediate targets; engine/G-buffer owners supply guides. The explicit two-frame trace-result-to-diagnostic comparison is a shader-chain component checking suppression preserves confidence and signed display colors. It is not runtime scheduling/history evidence. |
| World transport | Existing production query batch, clipmap GPU resources and upload owner; explicit scheduling remains confined to transport assertions. Runtime scheduling is exercised separately. |

Intentional low-level allocation exceptions are not evidence of production allocation or orchestration:

- `SurfaceCacheDebugBindingTests`, texture lifetime/readback, framebuffer blend and program-layout tests deliberately choose targets/formats, mismatched target sentinels and attachment layouts to test those contracts. `LightingResourceOwnershipTests` instead queries actual driver formats from shared production owners and checks replacement/disposal.
- Single-shader filter, temporal, gather, SH projection, PIS, anchor, velocity, upsample and combine functional cases retain explicit per-test signals/targets where they assert channel packing, invalid anchors, confidence discontinuities, exact history/neighborhood values, quadrature coefficients or resolution-edge behavior. Common engine/terrain helpers now use owners. These cases exercise a shader's input/output contract independently of runtime allocation; their hard-coded precision/layout expectations are deliberate component expectations, not reusable E2E fixture layouts.
- HZB component tests retain `HzbTestPyramid` for explicitly selected mip counts and odd/edge dimensions. The runtime uses `LumOnBufferManager`'s pyramid.
- Older voxel-capture/relight/feedback compute cases and `SharedSurfaceInputFixture` retain hand-packed page/voxel/face tables to isolate packing, misses, counters, bounds, work completion and sub-production-size volumes. Shared runtime/source fixtures use `TraceGeometryGpuScene` and partition publication. These low-level tables must not be reused as production-runtime setup.
- Debug-only functional tests retain synthetic channel/mode inputs and display targets so color decoding is tested independently of upstream lighting.
- `PbrLumOnPipelineTargets` and the older PBR full-chain integration cases remain explicitly shader-chain coverage. They do not satisfy Section 13's future cache-originating, production-PBR requirement; that work must extend the mod-driven host rather than reuse that manually sequenced chain.

No lighting assertion was removed or relaxed. Component fixtures retain only their stated operation or comparative diagnostic chain; all broad source/history/movement/lifetime scenarios remain production-driven. The executed normal SPIR-V build, cross-context regression, baseline comparison and independent completion review are recorded below.
Second implementation review of the follow-up confirmed that default configuration/camera/source adapters, shader registration order, atlas byte admission and terrain sampling policy remain unchanged. Constructor dependencies also flow through live-reload creation paths. The fixture diff preserves the previous numerical/confidence/occlusion assertions; new assertions query driver storage, retirement and framebuffer completeness. All renderer/provider wiring and broad pipeline execution remain with production owners. The independent source review found no further implementation defect; final completion audit also passed after independently checking the combined run and baseline receipts below.

The expanded cross-context run exposed a sampler lifetime gap: static `GpuSamplers` objects retained handles after a headless collection destroyed its unshared GL context. `GpuSamplers.Dispose` now participates in normal `GpuResourceManagerModSystem` shutdown before the deletion manager retires, and the headless context uses that same lifecycle operation before destruction. `SamplerShutdownRetiresHandlesAndAllowsFreshOwnership` checks actual GL validity and replacement ownership. This is production resource cleanup, not a test-only reset API. The new calls are at shutdown, not in per-frame rendering or uniform setters.

Follow-up current-source verification: normal SPIR-V-enabled build passed (five existing unrelated analyzer warnings). The expanded 39-class cross-context run contained 421 cases (418 executed, three skipped): **402 passed, 16 failed, three existing skips**. All original 101 migration cases passed, as did all four new resource-ownership cases. The sampler-related InvalidOperation cascade from `finalcandidate.{log,trx}` no longer occurs. Receipts: `artifacts/surface-resource-ownership/current-verified-build.log` and `current-verified.{log,trx}`; the explicit class inventory is `finalcandidate-classes.txt` in that directory. This is headless GPU evidence, not in-game validation.

The remaining failures are seven `LumOnTraceOutcomeDebugFunctionalTests` palette cases using an unknown/ambiguous declared stage pair, five `SharedTraceSceneScreenTests.SealedRoomUsesSharedHitLighting` cases expecting geometry light/confidence without surface-cache publication, and four `LumOnNearFieldMaterialReadinessTests` cases retaining the same earlier lighting assumption. An untouched archive of HEAD `215d6b288bb856958976ba989f01b4c1d33aa47c` reproduced all 16 failures: 21 cases, five passed, 16 failed, zero skips. Every failing test name and exact error message matches the current run. Baseline receipts are `baseline-old-tests-verified.log`, `baseline-old-tests.trx`, `baseline-failure-comparison.json` and `baseline-head-sha.txt` in the same artifact directory. These pre-existing test maintenance gaps remain unresolved; the expanded suite is not all green. The three unchanged explicit skips are the obsolete L1 gather comparison and the trace shader's geometry-hit tint/falloff cases; no new skip or assertion relaxation was introduced.


Final independent completion audit passed: all seven follow-up obligations are satisfied, including the original 101 assertions, four new owner contracts, cross-context cleanup and exact baseline failure comparison. No required implementation gap remains. The 16 older failures and three explicit skips remain disclosed maintenance gaps, not a fully passing expanded suite.


### Base shader fixture allocation reuse

The base-class allocation work is complete. The four per-texture helpers had only one caller (depth/normal in SharedGeometryDebugTests); unused color/material helpers were removed. The caller now also reuses the scene's patch attachment and output framebuffer. Unchanged dimensions retain all five textures and the framebuffer. Distinct ShaderSceneInputs instances own separate resources even at identical dimensions. No production code or shader interaction changed. Remaining related-fixture allocations and the shader-interface migration stay open in Section 12.

Focused verification passed on the final source: normal SPIR-V-enabled build, zero errors (five existing unrelated warnings); 24/24 GPU cases passed with zero skips across contexts. Coverage: ShaderSceneInputsTests 2, SharedGeometryDebugTests 12, LightingResourceOwnershipTests 4 and SurfaceLightingConsumerRuntimeTests 6. Assertions cover all texture identities and framebuffer reuse, population readback, independent same-size scenes, resize retirement and disposal. Second review and independent focused source review found no issues. Receipts: `artifacts/scene-input-reuse/build-final.log` and `focused-final.{log,trx}`. This completes the base-class portion only; remaining fixture allocation and shader-interface work stays open.
