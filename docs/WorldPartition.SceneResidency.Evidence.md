# Scene residency migration evidence

Status: complete. Implementation, second review, final validation and independent completion audit satisfy both item 4 tasks.

## Contract traceability

| Task | Controlling source | Requirements | Implementation and evidence |
| --- | --- | --- | --- |
| Scene adapters | WorldPartition.CoverageAndLifecycle.Proposal.md, Existing scene consumers; Scope and ownership | One coordinator owns residency; preserve tracing priority and capture/relight queues; remove replaced registry and transition scheduling | SceneResidencyMigrationTests, TraceSceneRegionSchedulerTests, renderer call-path review; 341 selected passing cases |
| Publication | Same proposal, Lifecycle and publication contract; Budgets and fairness | Identity validation before GPU writes, acknowledged readiness, cancellation and world teardown, shared limits | PartitionDomainUpdateTests and scene adapter tests: late work, reset, dirty revisions, slot generations, upload deferral and sustained two-way fairness |
| Coverage | Same proposal, Streaming sources and coverage; Registration, identity, and layout | World-zero alignment, independent instances, retained overlap, previous scene loaded/active and heat policy | SceneResidencyMigrationTests: negative/large movement, overlap, retirement, heat expiry and independent registrations |
| Source layout | Same proposal, Existing scene consumers | Shared contracts in top-level WorldPartition, thin composition root, domain-specific queues remain in scene | Reference scan found no production use of the removed registry, transition driver or desired-state helpers; production build clean |

Both checklist items inherit the entire approved proposal. The proposal was read in full. Near-field behavior remains covered by its existing regression suite; world-probe migration is reserved for item 5.

## Implementation and second review

- `PartitionCoordinator.DomainUpdates.cs` authorizes the existing scene content queues with the same registration generation, incarnation, revision and request identity used by automatic providers. Shared limits apply before starting workers and before invoking uploads. Completed results retain their lease during a temporary upload-budget deferral. Impossible payloads remain explicit budget backlog.
- `TraceSceneRegionScheduler.Residency.cs` registers independent 32-block tracing cells. Window coverage and retirement belong to the coordinator; distance/near-first priority, loadedness probing and millisecond domain backoff remain in the existing scheduler. Worker cancellation tokens reach the existing chunk executor. Publication uses the existing GPU dispatcher under coordinator authorization and source-version checks. Uploads are authorized per region through the existing GPU dispatcher.
- `LumonSceneRegionScheduler.Residency.cs` supplies loaded/active bounds and bounded heat sources. It acknowledges stable GPU slot generations and reads desired/actual observations back into domain queue eligibility. Page validity and capture/relight work remain consumer-owned.
- The global kind-keyed registry, scene transition heap/driver, duplicate desired-state implementations and unused state-machine/hysteresis helpers were removed. Shared contracts now live under `WorldPartition`; packed keys are explicitly consumer-local work tokens.
- `WorldPartitionModSystem` supplies the shared coordinator and world-leave lifetime boundary. Consumer resets unregister their instances. Worker acknowledgements and tracing dirty events use concurrent queues; coordinator state stays on its owning thread.

Second review checked the full proposal against runtime call paths, not only adapter tests. It corrected: required-before-loaded admission ordering; diagnostics sampled before acknowledgement; missing request identity on failure acknowledgements; cancelled-worker credit being released before actual completion; a completion dequeued at the frame time limit being lost; and unnecessary automatic scanning of externally scheduled unready cells. `PartitionCellInfo` is a value snapshot, so single-cell queries do not allocate.

The old pure desired-state tests are replaced by cases through the real scene adapter. The removed helper tests exercised obsolete algorithms; shared coordinator coverage/lifecycle tests and adapter movement, heat and slot-generation cases are the replacement evidence.

## Independent review resolutions

The independent audit reconstructed the contract from the approved proposal and task list. Its fairness findings were fixed and reviewed again:

- Domain admission now reserves service for the oldest blocked domain consumer, while service-sequence accounting also guarantees automatic providers an opportunity under continuous domain contention.
- Positive-byte uploads share the coordinator's persistent upload-turn reservation.
- Zero-byte scene slot acknowledgements bypass byte-upload reservations without consuming or clearing another request's turn.
- Coordinator retirement invokes the near-scene page cleanup callback before ring remapping. The renderer's separate departing-window scan is removed.

Sustained domain/domain and automatic/domain tests exercise both directions of starvation pressure. A scene adapter test reproduces the formerly stranded zero-byte acknowledgement. The independent source re-audit reported no remaining implementation blockers.

## Final validation

All builds and test execution used the validation subagent. The final source was rebuilt after the last audit fixes.

| Evidence | Result | Receipt |
| --- | --- | --- |
| Focused coordinator and adapter cases | 84 passed, no failures or skips | `artifacts/item4-verified-focused-tests.log`, `artifacts/TestResults/item4-verified-focused.trx` |
| Broad scene, tracing, near-field and partition regressions | 339 passed, including 113 GPU cases; no failures or skips | `artifacts/item4-verified-regression-tests.log`, `artifacts/TestResults/item4-verified-regression.trx` |
| Async runtime-wiring GPU case, fresh host | 1 passed | `artifacts/item4-verified-gpu-runtime.log`, corresponding TRX |
| Async tracing-to-relight GPU case, fresh host | 1 passed | `artifacts/item4-verified-gpu-relight.log`, corresponding TRX |
| Production C# build | Zero warnings and errors | `artifacts/item4-verified-build.log` |

The focused cases are included in the broader set, not added again: **341 unique passing selected cases, including 115 GPU cases**. Builds used `EnableSpirv=false`; GPU tests executed the actual shader paths. No new live gameplay or performance claim is made.

Two unchanged shader source-contract assertions fail because they expect the earlier world-space bridge: `TraceSceneDebug_UsesTerrainBridgeWorldspaceConversion` and `ScenesOverviewDebug_UsesTerrainBridgeWorldspaceConversion`. Their test source and shader inputs are byte-identical to HEAD (`artifacts/item4-baseline-source-check.log`). They are recorded as pre-existing, outside this residency change; the shaders remain untouched.

An initial combined run also encountered a texture-unit exception in the async runtime-wiring GPU test and a hang during the tracing-to-relight GPU test. Both pass independently in fresh test hosts. Their test sources and the failing texture-binding inputs also match HEAD. The final broad run excludes these two cases and the two known shader assertions; the two GPU cases are then validated separately. This establishes selected regression coverage without claiming the original all-in-one invocation passed.

Original failed/stalled receipts remain under `artifacts/item4-regression-first-stalled.log`, `artifacts/item4-regression-diagnostic-tests.log` and the diagnostic TRX.

## Scope disposition

Near-field remains the fixed 48-block, 3-by-3-by-3 domain. Existing scene capture/relight queues and GPU storage remain domain-owned; residency and authorization have one coordinator owner. Probe residency evaluation belongs to the next checklist item.

Final independent receipt review confirmed 341 unique passing cases, 115 GPU cases, a clean production build, justified baseline exclusions, and no remaining required evidence gaps. Both item 4 checklist entries are complete.
