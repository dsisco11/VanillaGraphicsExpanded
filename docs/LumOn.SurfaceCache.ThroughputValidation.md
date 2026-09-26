# Surface Cache throughput validation

## Verdict

The delayed-publication defect is fixed; the broader validation item remains open for the separately recorded consumer and fixture failures. The pre-fix audit below preserves its original receipts.
Across completed runs, using the latest result for repeated cases, 932 distinct cases yield **905 passed
and 27 failed**, with zero skips. The failures are 15 of the historically tracked 19 consumer cases,
11 fixture/coverage cases outside that list, and one new backlog regression. These are completed
failing receipts, not a clean regression claim. The separately tracked 19 were excluded from the
initial selection and then executed in the supplemental consumer run.
No game process was launched; user-run convergence remains the following checklist item.

## Geometry and material screen-fixture repair

The five `SealedRoomUsesSharedHitLighting` variants and four formerly failing material-readiness
variants now bind real produced Surface Cache lighting. `SurfaceLightingScreenTraceFixture` borrows
the original geometry and owns bounded cache storage; production capture, reset, seed and combine
dispatches produce outgoing radiance. Failed capture or seeding leaves readiness zero. No final
lighting is injected and no production rendering code changes.

The original signed coordinates, coverage sizes and room shapes remain. Authored material emission
supplies the original 0.25 radiance target independently of packed voxel lighting. Assertions compare
all RGB channels, opaque-hit identity, unchanged hit distance, confidence and classification. Ready
black lighting has confidence one and outcome three; unavailable lighting has confidence zero and
outcome four. Bright resolved lighting has outcome two.

The material lifecycle test is renamed `CapturedSurfaceReadinessChangesRequireRecapture`. Missing
surface descriptors remain unavailable after a registry-only change and resolve after geometry
material-table recapture plus cache publication. A present surface with a missing derived lookup is
now a positive case: the cache consumer uses the surface descriptor, not the legacy derived hit-color
table. Unsupported shapes retain their unavailable classification even when material capture succeeds.

Independent source review and root inspection verified ownership, publication guards, production
chunk-slot order and the preserved case coverage. The following focused receipt records completion;
the other failure categories and the broader validation checklist remain separate.

Subagent-run verification passed **48/48 tests, zero skipped**: all 14 cases in the two repaired
classes (nine formerly failing plus five negative controls), and 34 adjacent producer, consumer,
shared-surface and resource-reuse cases. Production and test projects built successfully; existing
xUnit analyzer warnings remain, and the initial rebuild also reported the previously noted obsolete
parameterless `BlockPos` constructor in fallback publication. No new production behavior or gameplay
performance claim is involved.

Receipts: `artifacts/TestResults/surface-screen-cache-fixtures.trx` and
`artifacts/surface-screen-cache-fixtures.log`. Selection: `SharedTraceSceneScreenTests`,
`LumOnNearFieldMaterialReadinessTests`, `SharedTraceSceneSurfaceTests`, `SurfaceLightingConsumerTests`,
`SurfaceLightingProducerTests`, and `NearFieldResourceReuseTests`. Independent final review found
no remaining implementation blocker. The historical broad-suite totals above are not a rerun result.

## Probe-ring preservation fixture repair

`ProbeRingPreservesOverlapWithinStableGeometryCoverage` now selects supported overlap from the
published origin, spacing and authored room geometry, independently of observed confidence. Both
gather variants require all 27 room probes to populate; solid centers and neighboring rooms without
requested cache surfaces are not readiness requirements.

Atlas and metadata addressing use the actual resource resolution, tile size, widths and shift ring
offsets. With ordinary uploads paused, every RGBA direction and both metadata components in the
surviving populated tiles must remain unchanged. The test also retains cache dependency revision,
resource identity, anchor alignment and final-image round-trip checks. All newly introduced edge
slots must remain zero/unavailable. Those edge slots start outside the lit room, so companion
prefilled-tile tests supply the separate nonvacuous proof that reuse clears old light.

Independent source review and root inspection found no remaining implementation issue. This repair
changes only test expectations and addressing, with no production rendering or runtime cost changes.

Subagent verification passed **19/19 tests, zero failures or skips**: both repaired gather variants,
both `SignedStreamingRejectsReusedUnavailableSlots` variants, all `SurfaceLightingPartialWorldProbeTests`
and all `WorldProbePartitionEvaluationTests`. This includes prefilled-slot clearing for positive,
negative and no-overlap shifts. Production and test builds succeeded with the existing CS0618
`BlockPos` and xUnit analyzer warnings. Receipts: `artifacts/TestResults/probe-ring-repair.trx` and
`artifacts/probe-ring-repair.log`. Other failure categories and broad-suite completion remain open.

## Source and restoration synchronization repair

The eight variants of `RetainedHistoryFollowsSourceLighting`,
`SourceChangesReachRetainedComposition`, `DoorwayClosureAndReopeningReachRuntimePixels` and
`SealedNeighborRejectsLocalizedLeakage` now compare completed direct-only lighting workloads.
Their synchronization helper disables indirect bounce work, waits for requested captures and
initialized lighting, and observes completion of the direct refresh sweep for the current geometry
revision before freezing the producer. Existing progressive-bounce helpers remain separate.

Consumer synchronization requires two successful full-tile updates for each supported room probe
after the producer freezes. The first can retire an older in-flight request; the second proves a
new admission against the frozen lighting. A temporary budget covers the finite fixture grid so
unresolved neighboring probes cannot starve these refreshes; the original budgets are restored.
Screen radiance and metadata must then remain unchanged across a complete directional sweep.
Both consumer waits share the existing 160-frame limit. Neither positive final pixels nor expected
darkness serves as the readiness predicate.

The doorway fixture also requests the actual side, floor and ceiling faces inside its opening.
Previously omitted faces left real ray hits waiting for unrequested cache lighting. Door edits now
dispatch the engine chunk-change event through registered subscribers, including re-enabling probes
disabled while their centers were inside the closed door. Source-only changes continue to exercise
retained directional history without this geometry-edit notification.

The original numerical tolerances, source-off darkness, immediate neighbor rejection, resource
identity, history revision and restoration assertions remain. The changes are confined to tests
and fixtures; they do not alter production rendering or establish gameplay performance.

Subagent-run regression passed **19/19 tests, zero failures or skips**: the eight repaired cases,
five `SurfaceLightingTemporalComponentTests`, two `SurfaceCacheDynamicLightingTests`, both probe-ring
preservation variants and both progressive-bounce variants. Production and test compilation succeeded
with the five existing xUnit analyzer warnings. Receipts:
`artifacts/TestResults/surface-freshness-final.trx` and `artifacts/surface-freshness-final.log`.
This run preceded the final tightening that shares the consumer frame allowance between both waits.
The final source then passed **8/8 selected cases, zero failures or skips**, within that shared limit:
`artifacts/TestResults/surface-freshness-bounded-final.trx` and
`artifacts/surface-freshness-bounded-final.log`. Independent final source review and root inspection
found no remaining issue. The other failure categories and broader validation remain open.

## Bounded dark-reload convergence repair

Both `UnavailableGeometryDiffersFromValidDarkness` variants now establish their full-tile workload
before bright warmup. Changing the texel batch size during reload would reset producer history,
invalidating a retained-decay test. The fixture requires nonzero accumulated indirect lighting,
retains the original unload darkness and zero-confidence checks, and delivers a real `NewlyLoaded`
chunk notification on reload.

Requested-page observations use the pooled tile-readback owner. After the current direct refresh
finishes, direct lighting must be dark while indirect lighting remains positive; the indirect atlas
and lighting dependency revision must be unchanged. Requested pages must account for all resident
pages, and the configured allocations must fit every page in each stage within the shared publication
ceiling. Each decay frame must complete a full indirect sweep and publish a newer generation.

The sweep bound follows the fixture's diffuse reflectance and temporal history cap: with reflectance
0.25 and history cap four, the peak-energy contraction bound is 0.85 per completed sweep, with a small
half-float rounding allowance used only to derive the sweep count. Energy is read once per temporal
service window and must decrease, exposing stalled history. Original darkness tolerances remain
unchanged. Producer convergence and subsequent world/screen freshness share the original 160-frame
reload allowance; final world radiance must be dark and resident probe confidence must recover.

These observations exposed a separate framebuffer-cache defect: restoring a draw framebuffer after
a temporary readback left the combined framebuffer cache pointing at the deleted temporary object.
A later blit attempted to restore that stale ID. The correction keeps the combined query cache in
sync with its draw-binding alias and prevents a combined binding query from overwriting a distinct
read binding. Actual combined binds continue to update both bindings. This is graphics-state
bookkeeping, with no added per-frame queries or changes to lighting algorithms.

The original reload cases reproduced as **2/2 failures** in
`artifacts/TestResults/surface-dark-reload-baseline.trx`. The three new framebuffer regressions also
failed before the cache fix: `artifacts/TestResults/framebuffer-alias-before.trx`. They cover cached
and uncached combined-binding queries with distinct read/draw bindings, and a real layer readback
followed by a blit. Independent implementation review and root inspection found no remaining issue.

In the corrected five-case run, both reload variants completed 72 full indirect sweeps within the
derived 72-sweep bound and used **117 of 160 reload frames**, including consumer recovery. Retained
indirect peaks decreased from approximately 10.52/10.56 to 0.0000849/0.0000847 in directional/SH9 modes.
The same indirect atlas and lighting dependency revision survived; direct lighting was dark, and
the final world-probe radiance and confidence assertions passed. Receipt:
`artifacts/TestResults/surface-dark-reload-fixed.trx`, with the corresponding log in `artifacts`.

Final subagent verification passed **30/30 tests, zero failures or skips**, in approximately 1 minute
25 seconds of test execution. Coverage includes the two reload variants, the prior eight repaired
source/restoration cases, the retained-indirect direct-refresh control, and framebuffer binding,
unbinding, blend, texture readback and pixel-pack regressions. Production and test builds succeeded
with the existing obsolete `BlockPos` constructor and five xUnit analyzer warnings. Final receipts:
`artifacts/TestResults/surface-dark-reload-final.trx` and `artifacts/surface-dark-reload-final.log`.
The remaining failure categories, broader completion gate and gameplay acceptance remain open.

## Backlog fix and focused verification

Completed CPU results now retain their original lifetime, immutable source/ready-hit page dependencies,
observed chunk identities and terrain accessor in `SurfaceFallbackCommitDependencies`. The publication
owner checks that shared context once before each budgeted drain. Changed identities or failed terrain
lookups reject delayed CPU results while preserving displayed lighting. Partial drains retain the context;
empty queues and reset release it.

The change allocates one context per nonempty completed CPU batch and shares existing immutable arrays.
It adds no per-texel dependency copies or GPU reads. Chunk validation is bounded by the existing
512-dependency cap and reuses one block-position scratch value. Actual timing of this fix is unmeasured.

Subagent-run final verification passes **118/118 focused tests** and the production build has **zero
warnings and errors**. Runtime regressions cover chunk withdrawal, chunk replacement without an edit
event, captured-page identity loss outside the remaining queued origins, throwing terrain access, and
valid pause/resume across zero publication credit. Independent final source review found no remaining
confirmed blocker in this fix after the terrain exception path was made fail-closed.

Receipts: `artifacts/TestResults/backlog-fix-regression.trx`, `artifacts/backlog-fix-regression.log`,
and `artifacts/backlog-fix-build.log`. The broad consumer/fixture suite was not rerun for this fix;
its pre-fix results below remain separate evidence. The stale alternating-budget documentation was
also reconciled with independent allocations.

## Contract and independent review

The authority is the Surface Cache tracing throughput section of
[the task list](LumOn.WorldProbeSurfaceLighting.todo), including its preservation and initial-seed
independence constraints. The independent reviewer consulted the following current repository sources:

| Requirement | Controlling document | Review result |
| --- | --- | --- |
| Bounded diagnostics, attempts versus useful progress, timing interpretation | [Tracing baseline](LumOn.SurfaceCache.TracingBaseline.md) | Existing counters and matched receipts distinguish completion from attempts; pending-commit accounting is examined below |
| Coverage deferral, publication wake, finite fair retry sweeps | [Capture admission](LumOn.SurfaceCache.CaptureAdmission.md) | Identity-bound eligibility and dependency stamps inspected; unavailable sources do not spend GPU capture credit |
| Signed coverage, streaming, bounded storage/uploads, shared consumers | [Geometry coverage](LumOn.SurfaceCache.GeometryCoverage.md), [shared geometry contract](LumOn.TraceSceneGeometryContract.md) | Current 192/256 coverage and readiness ownership supersede older consolidation limits |
| Unsupported/outside fallback, full-batch outcomes, delayed dependency validity | [Geometry fallback](LumOn.SurfaceCache.GeometryFallback.md) | Bounded worker and pre-query checks present; completed backlog loses required dependencies |
| Long rays, distinct sky/distance/budget outcomes, retry wake | [Traversal budget](LumOn.SurfaceCache.TraversalBudget.md) | Authoritative sky boundary, finite-segment distinction, bounded delay and independent buckets inspected |
| Missing hit lighting, complete geometry retention, fairness | [Hit retries](LumOn.SurfaceCache.HitLightingRetries.md) | Bounded retention and dependency-aware queries present; final backlog handoff is the identified gap |
| Independent allocations, initial readiness, history, publication ceilings | [Update budgets](LumOn.SurfaceCache.UpdateBudgets.md), [lighting contract](LumOn.SurfaceCache.LightingContract.md) | Independent cursors, shared full-tile bound and deduplicated combination inspected |
| Matched costs and completion evidence | [Budget measurements](LumOn.SurfaceCache.UpdateBudgetMeasurements.md) | Valid controlled shader/service-model evidence; no claim of full renderer or gameplay speedup |
| Shared world-probe lighting and lifecycle | [World-probe architecture](LumOn.WorldProbeLighting.ArchitectureAndIssues.md), lighting contract | Geometry/cache consumer guards remain; historical fixture exclusions require explicit accounting |

The review ran separately from the test agent and made no implementation changes. Root inspection
independently confirmed the deferred dependency loss and reconciled the review with the linked contracts.

## Pre-fix findings

| Finding | Evidence | Required action |
| --- | --- | --- |
| Completed CPU fallback backlog loses hit-page and chunk identity dependencies | `LumonSceneRelightUpdateRenderer.Fallback.cs`: `pendingCommits`, `PollFallback`, `ApplyFallback` | Retain immutable observed hit-page/chunk dependencies with delayed completions and revalidate at actual drain/submission |
| Obsolete scheduling description | `LumOn.SurfaceCache.LightingContract.md:95` still describes alternating seed/indirect work; its later scheduling section and production code use independent allocations | Reconcile the obsolete sentence with independent budgets |

The production defect is at the transition from a validated query result to delayed publication.
`PollFallback` checks its observed CPU chunks and ready hit-page identities, then clears those owners.
`ApplyFallback` stores only the estimate, source page, global lifetime and a CPU-origin flag.
When page/texel credit defers that estimate, a later drain calls only `HitOriginCurrent`.

A hit page can be evicted/reassigned without changing the surviving source page or global lighting
dependency revision. An observed CPU chunk outside GPU coverage can be replaced without `ChunkDirty`.
Neither case is necessarily caught by the remaining global/source checks. The geometry-fallback and
hit-retry contracts expressly require these identities to remain valid before committing; permission
to retain stale lighting does not permit publishing a newly completed result against changed identity.
Tests of edits while a worker or query is pending do not cover this later backlog interval.

The new production-runtime test
`SurfaceFallbackRuntimeTests.BackloggedCompletedFallbackRejectsWithdrawnCpuChunks` reproduces the
CPU dependency loss. It captures a multi-page fallback batch, reduces indirect publication credit,
waits for `pendingCommits > 0`, then withdraws CPU chunks without changing GPU geometry revisions.
The expected `fallbackCommitted` count remains 3; the actual count becomes 4. This is an accepted
commit submission after dependency withdrawal, not merely a missing assertion inferred from source.
The original test and failing receipt are retained as pre-fix evidence. Hit-page eviction during backlog was
source-confirmed through the same lost-dependency handoff; the fix validation adds a retained-page capture-identity regression.

The reviewer narrowed this finding after checking admission order: the one retained-hit completion
is enqueued first and always fits positive page/texel credit in the same call. Subsequent CPU fallback
completions are the reachable delayed backlog. The proposed `hitCommitted` overcount specifically
from backlog deferral was therefore dismissed, not reported as another confirmed defect.

## Regression evidence

Initial command: `artifacts/surface-throughput-review-command.ps1`.
Receipt: `artifacts/TestResults/surface-throughput-review-regression.trx` and
`artifacts/surface-throughput-review-regression.log` (893 total, 886 passed, seven failed, zero skipped).

| Initial failures | Count | Disposition |
| --- | ---: | --- |
| `WorldProbePartitionEvaluationTests.Teleport_LateSuccessfulCompletionCurrentlyMarksReassignedSlotValid` | 1 | Resolved test discrepancy: replaced obsolete defect characterization with a `Dirty` safety assertion; renamed test passes |
| `LumOnNearFieldMaterialReadinessTests` ready-light cases | 4 | Fixtures expect direct voxel lighting without supplying the Surface Cache required by the current screen consumer; retain exact failure evidence and reconcile fixture coverage |
| `SurfaceLightingSpatialRuntimeTests.ProbeRingPreservesOverlapWithinStableGeometryCoverage` | 2 | Remain outside the known 19; current failures occur waiting for all world-probe confidence values, before overlap assertions |

| Run | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Initial focused/shared-consumer regression | 893 | 886 | 7 | 0 |
| Supplemental shared-screen and historical consumer classes | 49 | 29 | 20 | 0 |
| Corrected assertions and backlog reproduction | 10 | 9 | 1 | 0 |

The selections overlap; do not add their totals. The 932-case latest-result union normalizes the
renamed teleport test to the same logical case. Supplemental and targeted receipts are
`artifacts/TestResults/surface-throughput-review-consumers.trx` and
`artifacts/TestResults/surface-throughput-review-targeted.trx`, with corresponding logs in `artifacts`.

The five historically excluded `SharedTraceSceneScreenTests.SealedRoomUsesSharedHitLighting` cases
all fail and remain separate from both the initial seven and the 19 user-confirmed failures. Their
fixtures omit the required Surface Cache snapshot. Four material-readiness cases have the analogous
raw-voxel-light assumption. The two ring integration cases assume all 8-cubed probes must become
confident despite limited requested cache pages, and also retain two-probe indexing assumptions.
These 11 assertions were not weakened or removed; fixture reconciliation remains an evidence gap.

Passing replacement/component coverage includes `ScreenProbesConsumePublishedCache` at multiple
coverage sizes, `AnchorShiftClearsReusedSlotsAndPreservesOverlappingDirections`, shared geometry ring
preservation and scheduler origin-shift tests. These establish useful current-contract behavior but
do not turn the remaining failing integration fixtures into a clean suite.

Of the historical 19, four now pass: `CacheReplacementInvalidatesComposedHistory(sh9:false)`,
`RecreatedDarkCacheRejectsRetainedFinalLighting(sh9:false)`, and both variants of
`ProgressiveBounceReachesRuntimePixels`. No root-cause fix was made for those cases here, so a current
pass is not a claim of a verified repair. The other 15 reproduce. The original list remains preserved
in the geometry-fallback report and the separate final TODO.

The transport test `FailedCaptureRetriesAfterGeometryBecomesAvailable` previously read a capture SSBO
before any capture dispatch. Its apparent pass/fail could depend on uninitialized contents. It now
asserts resident pending capture, no `Capturing` flag, no submitted GPU capture failures, and successful
publication after geometry recovers. It and the corrected teleport safety test pass in the targeted
run. These were test-contract corrections; the subsequent production fix is recorded below.

The final production build passed with zero warnings and errors:
`artifacts/surface-throughput-review-build.log`. Test compilation retains the five existing unrelated
analyzer warnings. The initial and supplemental command scripts are
`artifacts/surface-throughput-review-command.ps1` and
`artifacts/surface-throughput-review-supplemental-command.ps1`.
The deduplicated case ledger and test-agent report are
`artifacts/surface-throughput-review-case-results.txt` and
`artifacts/surface-throughput-review-test-report.md`.

## Measurement evidence and limits

The preserved matched update-budget JSON contains separate diagnostics-off/on ABBA blocks, identical
attempted page/texel counts, and verified completed-texel counts for instrumented legs. Resetting pages
before each measured sweep prevents seed work from being mislabeled when it is already initialized.
Its 18-versus-nine simulated service frames compare equal work; more work per frame increases the
per-frame cost. GPU intervals, submission/readback wall time and explicit poll time remain separate.

The baseline also records resolved and outside-coverage workloads, demonstrating that failed attempts
can cost more while producing no useful texels. Geometry expansion records different-volume startup,
movement and edit costs rather than claiming a speedup. Historical gameplay counters are not a matched
post-change comparison. Scheduler sorting, complete publication, geometry upload, delayed CPU fallback
and live-scene convergence are not established by the update-budget microbenchmark.

The later user-run acceptance item is intentionally open. Conservative invalidation, count/byte rather
than frame-time bounds, no independent distant-surface lighting provider, and more expensive larger
coverage are documented design choices rather than additional review defects.
