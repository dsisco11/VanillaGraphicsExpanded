# Surface Cache update budgets

The relight producer now admits captured-page initialization, direct refresh and indirect updates
independently. A page can receive multiple operations in a frame; it is combined and published once.

| Setting | New default | Range | Meaning |
| --- | ---: | ---: | --- |
| `RelightSeedPagesPerFrame` | 8 | 0–256 | Initial captured-page seed batches |
| `RelightDirectPagesPerFrame` | 8 | 0–256 | Published-page direct refresh batches |
| `RelightIndirectPagesPerFrame` | 4 | 0–256 | Published-page indirect batches, including delayed completion |

Each batch still uses `RelightTexelsPerPagePerFrame` (default 64). Indirect work additionally depends
on rays per texel, traversal steps and trace distance. Zero disables only that allocation; disabling
indirect work does not prevent initial publication or direct-light updates. Budget edits do not reset
history, surface readiness, bucket cursors or dependency identity.

Only the three independent settings are supported. Missing settings use the 8/8/4 defaults;
there is no migration from the former shared budget.

## Selection and bounds

Each operation has its own physical-identity cursor. Half its page admissions, rounded up, go to
round robin; the rest prefer recently visible pages and nearby source chunks. A single page credit
alternates fair and priority turns. Captured but incomplete pages retain a separate seed allowance.
Source invalidation conservatively prioritizes resident direct lighting for a bounded number of
successful visits, while reserved fair turns continue serving background pages. Page identity changes
retire that priority state. This is conservative scene-wide source prioritization, not a claim of
precise per-light influence tracking.

Direct and indirect texel bucket cursors remain independent. Exhaustion delays are checked before
indirect admission; delayed buckets do not consume GPU page credit, and other buckets can proceed.
Seeding keeps its existing successful-bucket completion tracking.

The sum of admitted stage batches is capped at `65536 / tileTexels`, preserving the existing reset,
combine and carry-forward ceilings. Scarce full-tile slots rotate between requesting stages. The
default 16x16 pages allow all 20 configured batches. Larger pages or extreme configured budgets may
receive smaller effective allocations; unused stage allocations are not borrowed by other stages.

Delayed CPU fallback and retained-hit commits share indirect credit. With at least two credits, up
to half are offered to delayed work; a single credit alternates delayed and new-trace turns. Unused
delayed credit remains available for tracing. Completed results that exceed page or sixteen-texel
commit limits remain queued rather than being discarded. The queue drains before collecting another
completed producer batch, so it holds at most one sixteen-texel CPU result plus one retained-hit
texel. World, resource, geometry and source-page identities are checked again when draining.

## Submission and measurement

Three separately owned work SSBOs let seed, indirect and direct operations be submitted before any
of their completion maps. Existing GPU barriers order atlas writes. CPU completion reads still occur
before combination/publication, and output status remains checked. This removes between-stage
submission waits without publishing unknown work or adding an asynchronous publication lifetime.
Each staging SSBO is bounded at 256 sixteen-byte descriptors: at most 12 KiB total.

The matched shader/readback experiment and its limits are recorded in
[Update budget measurements](LumOn.SurfaceCache.UpdateBudgetMeasurements.md). The throughput
allocation intentionally spends more work per frame; controlled scheduling frames are not live game
frame timings. User-run convergence and final scene-specific tuning remain separate tasks.

## Validation

Subagent validation passed 139 focused cases, the corrected opt-in measurement, and a final five-case
readiness diagnostic check. The final production build passed with zero warnings and errors. Test
compilation retained five existing unrelated analyzer warnings. Coverage includes defaults and
serialization, stage and publication bounds, admission fairness, independent buckets and exhaustion
wakes, direct progress with indirect disabled, live budget changes without dependency reset, and
fallback completion retained across disable/resume. Root source review checked aggregate publication
limits, duplicate-page combination, delayed-result lifetime rejection and per-operation SSBO ownership.

Receipts: `artifacts/TestResults/surface-budgets-focused-final.trx`,
`artifacts/surface-budgets-focused-final.log`,
`artifacts/TestResults/surface-update-budget-readiness.trx`, and
`artifacts/surface-update-budget-final-build.log`. The measurement report links its separate receipts.
The later broad throughput review and user-run visual acceptance items remain separate from this
implementation task. No game process was launched.
