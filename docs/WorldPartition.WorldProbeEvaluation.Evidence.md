# World-probe residency evaluation and final acceptance

Status: complete. Following evaluation and audit, the user explicitly decided not to migrate world probes. The earlier deferral is superseded by this architectural decision.

Contract: [item 5](WorldPartition.CoverageAndLifecycle.todo#5-world-probe-evaluation-and-final-acceptance) and the complete [approved proposal](WorldPartition.CoverageAndLifecycle.Proposal.md), read before implementation. No nested normative documents are required. The original proposal permitted deferral when equivalence and removal of duplicate residency work were not demonstrated. The subsequent user decision excludes probe migration; the proposal now records that decision.

## Decision

Do not register world probes with the shared coordinator. This is an intentional ownership boundary, not pending migration work. Keep `LumOnWorldProbeScheduler` as the sole owner of probe spatial coverage and lighting scheduling. Near-field geometry and both migrated scene consumers remain independently complete; no temporary probe adapter or second residency owner is introduced.

The world-zero lattice is representable. The evaluation initially recommended deferral based on the absence of demonstrated lifetime/publication equivalence and a justified cost/removal benefit, not an incompatible grid or a need for an offset origin. This evaluation makes no measured performance claim for an unimplemented adapter.

## Requirement traceability

| Task | Controlling proposal sections | Requirements | Evidence / disposition |
| --- | --- | --- | --- |
| Evaluate one partition per level | World-probe clipmaps; Registration, identity, and layout; Budgets and fairness | Fixed zero grid; preserve lattice/ring; stale-work rejection; bounded frame cost; eliminate duplicate ownership | Mapping analysis and `WorldProbePartitionEvaluationTests`; existing topology/origin-shift tests; lifetime and cost analysis below. Explicit deferral. |
| Preserve lighting responsibilities | Scope and ownership; World-probe clipmaps | Importance, directional budgets, confidence/history, atlas format remain consumer-owned; residency is not valid lighting | No production or shader changes. Probe scheduler/budget/importance/integrator and GPU regression selection verifies existing behavior; limitations below are not represented as passing safety tests. |
| Final acceptance | Existing scene consumers; Near-field geometry migration; Verification and acceptance; Observability | Delegated build/regressions; current architecture/invariants; no replaced temporary lifecycle owners; near-field independent of conditional probe migration | Prior foundation, coverage and scene evidence plus current receipts below. `WorldCellStateMachine.invariants.md` explicitly records the probe exception. |

## Coordinate and ring evaluation

For level spacing `s`, resolution `N` and integer snapped anchor coordinate `a`, production topology uses `origin/s = a - N/2` and sample coordinate `p/s = a - N/2 + i + 0.5`. One logical cell per sample can therefore use `floor(p/s)` on the fixed-zero grid with extent `s`.

- Even `N`: the sample is at the cell center, offset `0.5*s`.
- Odd `N`: the sample is on the cell's minimum face, offset zero. This is a consumer sampling convention, not an offset partition grid. Reusing the existing clipmap volume bounds verbatim would select `(N+1)^3` cells for odd `N`; the source must instead describe the envelope of the sample-owning logical cells. At `N=9`, these counts are 1,000 versus 729.
- Sampling positions and shader interpolation must remain unchanged. Logical source bounds are not a replacement shader origin.
- `UpdateOrigin` uses the existing two-world-block anchor deadband at every level. Required bounds would have to follow that same anchor decision. Generic retention alone would not reproduce this rule.
- `ring = wrap(ring + delta, N)` preserves the physical slot of overlapping samples. The existing `WorldProbeSchedulerOriginShiftTests` cover signed and diagonal shifts, deadband behavior and newly introduced slabs. New evaluation tests verify fixed-zero sample mappings at negative, fractional and large positions for both odd/even resolutions.

Relevant source: `LumOn/WorldProbes/LumOnClipmapTopology.cs`, `LumOnWorldProbeScheduler.cs`, and `LumOnWorldProbeLayout.cs` under the production project. Shader origins, interpolation and atlas packing are not edited.

## Lifetime and publication evaluation

The current probe request stores level, local/storage indices and importance flags, with no world-cell incarnation, content revision or request identity. `TryClaim` and `Complete` address the current storage slot. Small shifts mark an in-flight entering slot dirty-after-completion; this protects its eventual scheduler state, not GPU publication authorization.

The renderer currently appends every successful result to its upload list before calling `Complete`. The uploader writes by the result's storage index without checking logical ownership. On a whole-window teleport, `MarkIntroducedSlabsDirty` fills states with Dirty; a late successful `Complete` can then mark the reassigned slot Valid. The new `Teleport_LateSuccessfulCompletionCurrentlyMarksReassignedSlotValid` test deliberately characterizes that existing defect: its passing result proves the unsafe transition exists, not that stale work is rejected. GPU stale-slot publication is established here by source-path inspection, not a rendered reproduction.

A separate probe publication fix needs an immutable logical identity carried from selection through worker claim and render-thread upload; cancellation alone is insufficient. Overlap must retain valid work while reassigned slots reject late results. Lighting dirty-after-flight rules must remain separate from residency transitions. Tests must cover partial shifts, teleports, return to an old coordinate, dirtying, reset/world teardown and delayed results at the actual publication boundary.

This is a pre-existing probe limitation discovered by evaluation. It is recorded for a separate corrective change; completing this conditional evaluation does not certify the probe pipeline as free of stale-result defects. No behavior is changed to conceal it.

## Cost and removable-work evaluation

Production defaults in `VgeConfig.WorldProbeClipmapConfig` are `N=20`, three levels: 24,000 probe slots. The shared coordinator currently allows 16,384 resident cells across all consumers. A one-cell-per-probe adapter could not acknowledge all default slots under that cap, even before accounting for near-field and scenes. Raising the cap would require a new resource decision, not merely forwarding existing requests.

Existing steady-anchor coverage checks are constant work per level. A one-axis step dirties `N*N` entering slots; multi-axis steps visit their slabs, and teleports reset level arrays. Lighting selection separately scans the probe population and retains dense arrays for lifecycle, age, retry, importance, queued age and in-flight flags. These lighting responsibilities must remain under the proposal.

The current coordinator adds a dictionary cell object and two source-membership sets per requested cell, plus request/cancellation state when updating. Its `Pump` includes a scan for the reserved upload request across registrations, even with no matching reservation. Adopting it at probe granularity would add this work to the retained lighting scans. Incremental coverage avoids visiting overlapping interiors during range updates, but does not by itself eliminate these other costs. These are source-derived counts and complexity observations, not timings or measured allocated bytes.

Potentially removable code includes spatial wanted-set/lifetime bookkeeping and entering/leaving membership selection. Ring mapping, introduced-slot lighting invalidation, importance, directional scheduling and GPU clearing still belong to the probe consumer. No adapter has demonstrated a net removal of duplicate ownership code while preserving those responsibilities. Registering an entire level as a single cell would avoid per-probe costs but would not transfer slot-level residency authority and would fail the intended evaluation.

The user reviewed this evaluation and decided that the proposed integration provides no benefit. Migration is not planned. Address stale-result publication within the existing probe system independently.

## Final architecture and prior acceptance evidence

`WorldPartition` owns near-field geometry and scene-consumer residency through registered backends. Their domain queues and GPU storage remain specialized. The removed kind-only registry, cell-driven state machine, window hysteresis helper and scene transition heap remain removed. The scene residency backend files are steady-state integration boundaries, not competing lifecycle owners. Probe coverage remains explicitly outside coordinator registration under the explicit decision to retain the existing probe owner.

- [Foundation evidence](WorldPartition.Foundation.Evidence.md) records identity, coverage, lifecycle, budgets and reusable provider tests. Its descriptions of legacy consumers are historical, superseded by item 4.
- [Coverage diagnostics and final acceptance](WorldPartition.CoverageDiagnostics.Evidence.md#final-completion-review) records the current fixed 48-block near-field window, worker capture, source reuse, GPU publication, diagnostics, runtime limits and the user's live alignment/lag confirmations. Earlier expanding-window measurements in that file are historical and superseded by its final review.
- [Scene residency evidence](WorldPartition.SceneResidency.Evidence.md) records migrated ownership, fairness, teardown, source regressions and baseline exclusions.

The task list's former item 2 evidence link pointed to a nonexistent file. It is replaced with the existing current coverage/publication evidence, rather than reconstructing an unsupported historical 227-test receipt.

## Validation and second review

Second review re-read the task and complete proposal, checked the new tests against the production topology and scheduler, traced successful results through renderer drain and GPU upload, and checked coordinator coverage/scheduling and default capacities. It confirmed that odd resolutions can map without an origin exception, that dirty-state bookkeeping is not publication authorization, and that domain arrays cannot simply be deleted as duplicate residency. Production code and shaders remain unchanged; no probe adapter was introduced. The missing historical evidence link was replaced with the available current near-field acceptance record. Removed registry/state-machine symbols have no remaining production definitions; the old directory contains no files.

No new live gameplay or adapter performance measurement is claimed. Existing live acceptance is not reopened by this documentation/test-only evaluation.

The broader regression run exposed one obsolete test setup: `OriginShift_MarksIntroducedSlabDirty_AndSelectsItFirst` moved 1.1 blocks at spacing 1, so the existing two-block deadband prevented the shift the test expected. The fixture now uses spacing 4 and moves 4.1 blocks, asserts the actual one-cell origin/ring shift, then retains its original entering-slab priority assertion. Second review confirmed this matches the existing anchor policy and does not weaken the expected slab behavior. Production code is unchanged; the original failed receipt is retained and the affected scheduler suite is rerun.

All builds and tests were run by the validation subagent. Final unique selected coverage is **486 passing cases, including 152 GPU cases, with zero skips**, using the corrected rerun for the formerly failing case. This is an aggregate of selected runs, not a claim that the original broad invocation passed.

| Receipt | Result |
| --- | --- |
| `artifacts/item5-build.log` | Production C# build, zero warnings/errors |
| `artifacts/item5-evaluation-tests.log`, `artifacts/TestResults/item5-evaluation.trx` | Six new evaluation cases passed |
| `artifacts/item5-regression-tests.log`, `artifacts/TestResults/item5-regression.trx` | 482 passed, one obsolete scheduler fixture failed; 149 GPU cases passed |
| `artifacts/item5-corrected-scheduler-tests.log`, `artifacts/TestResults/item5-corrected-scheduler.trx` | Rebuilt scheduler/evaluation subset: 11 passed, including the corrected case |
| `artifacts/item5-direct-visibility-tests.log`, `artifacts/TestResults/item5-direct-visibility.trx` | One representative direct sealed-room GPU case passed |
| `artifacts/item5-gpu-runtime.log`, `artifacts/TestResults/item5-gpu-runtime.trx` | One async runtime-wiring GPU case passed in a fresh host |
| `artifacts/item5-gpu-relight.log`, `artifacts/TestResults/item5-gpu-relight.trx` | One async tracing-to-relight GPU case passed in a fresh host |

Selection covers probe topology, origin shifts, budgets, importance, integration, atlas mapping, radiance resolve, lighting effect, fallback and visibility, together with shared coordinator, near-field and scene regressions. The initial wider run was stopped because the 62-case direct-visibility group was projected to add roughly 25 minutes without production changes; its partial log remains at `artifacts/item5-regression-initial-partial.log`. Only one representative case of that group is counted in final coverage; 61 variants are not claimed. The two unchanged shader-source assertions documented in item 4 remain excluded. The async GPU cases are deliberately separate hosts. SPIR-V build integration used `EnableSpirv=false`; selected GPU cases compiled/executed actual shaders. Test compilation retains existing analyzer warnings.

The independent audit read the full contract, approved the explicit deferral and corrected scheduler fixture, and independently aggregated the final receipts: 486 unique passing selected cases, including 152 GPU cases. It found no remaining required implementation or evidence gaps and authorized all three item 5 checklist entries to be marked complete. The existing stale-result defect remains a documented corrective follow-up; this verdict does not certify stale-publication safety for the independently managed probe consumer.
