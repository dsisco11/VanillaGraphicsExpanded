# Surface Cache traversal limits and retry scheduling

The Surface Cache producer previously requested an effectively unlimited ray distance with a
64-cell default traversal budget. The shared GPU tracer also capped traversal at 512 cells, while
CPU fallback requested a 512-block segment with only 512 cell visits. Cell visits and ray distance
are different quantities: diagonal traversal crosses boundaries on multiple axes and also visits
the starting cell. Those limits could exhaust before reaching either a surface or the world top.

## Explicit finite segment

| Setting | Default | Range | Meaning |
| --- | --- | --- | --- |
| `LumOn.LumonScene.RelightMaxTraceDistance` | 512 | 1..512 | Maximum segment length in blocks, shared by GPU and CPU fallback |
| `LumOn.LumonScene.RelightMaxDdaSteps` | 1,024 | 0..1,024 | Maximum GPU cell visits per ray, including the starting cell |

Saved explicit step limits remain unchanged; an existing value of 64 must be increased to use the
new allowance. Lower settings remain useful for controlling work and testing exhaustion. These
settings do not change page admission, rays per texel, geometry coverage or upload budgets.

A normalized direction crosses at most approximately `distance * sqrt(3)` axis boundaries, plus
the starting cell and boundary rounding allowance. A 512-block segment therefore needs fewer than
900 visits even with tied boundaries processed one axis at a time. The 1,024-visit ceiling covers
that segment conservatively. The shared GPU loop keeps its runtime bound; existing consumers that
pass smaller budgets retain those budgets.

The producer packs its integer distance into the previously unused `slotOrigin.w` parameter.
Lookup consumers still use only `slotOrigin.xyz`. Fallback requests retain the same distance in
their unused `normal.w` field, preserving the 64-byte request layout. The CPU fallback scene now
allows 1,024 visits per ray while retaining the existing 64 ray starts per producer frame, one
outstanding batch, sixteen texels and 512 chunk dependencies. This raises its theoretical visit
allowance from 32,768 to 65,536 per frame's ray starts; it is not a wall-time guarantee.

Only a verified route to the authoritative upper world boundary completes sky. GPU finite-segment
completion remains `CLEAR`, step exhaustion remains `BUDGET`, and unavailable geometry remains
unavailable. CPU outcomes retain the corresponding distinctions. Neither a finite clear segment
nor an exhausted traversal adds black radiance or ages the previous indirect estimate.

## Delayed retries

GPU distance/step failures set a dedicated bit in the existing work-result word. Existing completion
and partial-progress bits remain separate. The CPU fallback also reports distance/step exhaustion.
Both paths defer the affected page/bucket for 32 producer frames. Successful sibling texels can
still publish through the normal path.

The scheduler advances past a delayed indirect bucket and uses that admission for direct refresh.
Other indirect buckets retain their cursors and remain eligible; initial seeding retains its own
turns. The delay table holds at most 4,096 entries. Repeated reports update an entry, and saturation
evicts the oldest entry, allowing that bucket to retry normally rather than blocking new work.
This is bounded retry suppression, not persistent storage for every possible exhausted texel.

Geometry publication, coverage movement and world edits wake delayed buckets without clearing
their lighting. Page identity changes remove their retry entries; incompatible resource/settings
changes clear the scheduler through the existing lifetime path. A stationary unchanged scene does
not repeatedly dispatch an exhausted bucket on each opportunity. Periodic retries remain necessary
because lighting rays use new sampling seeds. Delaying one bucket also delays any valid texels in
that bucket; it does not discard their results or suppress unrelated buckets.

The self-check reports `traceDistance`, `traceSteps` and `exhaustedBuckets` alongside the existing
fallback counters. The bucket count is retained retry entries, including expired entries awaiting
a revisit or eviction. GPU diagnostics still count distance and budget outcomes separately. No new
readback or fence is needed to obtain the retry bit.

## Validation

The subagent-run focused suite passed **85/85 cases**, with no skips. GPU cases exercise an upward
route beyond 64 visits and a diagonal route beyond 512 visits. They verify successful sky completion
at the known world top with 1,024 visits, distinct distance/budget counters, and unchanged history
for unresolved segments. CPU cases cover a full 512-block axis/diagonal segment and world-height
completion. Configuration, fallback distance transport, queue lifetime and retry scheduling are
also covered. Receipts: `artifacts/surface-traversal-focused.log` and
`artifacts/TestResults/surface-traversal-focused.trx`.

The shared-tracer, world-probe, partial-page and temporal regression selection passed 115 existing
cases. Its new runtime test initially used a fully hidden surface that launched no rays; correcting
the fixture to expose the wall made the isolated runtime test pass. That case verifies production
readback-to-scheduler wiring: eight stable frames retain the indirect attempt count while direct
refresh increases, and geometry invalidation wakes indirect work. Production code did not change
for the fixture correction. Receipts: `artifacts/surface-traversal-regression.log`,
`artifacts/surface-traversal-runtime.log` and their matching TRX files under `artifacts/TestResults`.

The final production build passed with zero warnings and zero errors
(`artifacts/surface-traversal-build.log`). Shader generation compiled 105 stages and 250 variants.

Gameplay convergence and timing remain separate user-run acceptance items. The 19 pre-existing
consumer/runtime failures remain tracked by their own task and are not part of this change.
