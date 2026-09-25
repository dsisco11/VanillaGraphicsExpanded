# Optional L0 GPU tracing: routing and bounded compute

The first task introduces `WorldProbeClipmap.EnableGpuTracing`, serialized and enabled
by default. Enabled routing sends L0 admissions to `LumOnWorldProbeGpuTraceBackend`;
L1 and higher use the CPU backend. Disabling it routes every level to the CPU backend.

The GPU backend now traces L0 against existing uploaded geometry and samples the
Surface Cache at resolved hits. It does not upload additional terrain. Coverage exits
and unsupported collision shapes use bounded CPU collision fallback. GPU geometry
unavailability, distance limits and traversal exhaustion remain unresolved; they do
not become sky or trigger a second geometry backend indiscriminately.

## Shared admission and publication

Both routes use the existing scheduler, admission tickets, work items, direction
selection and trace/upload budgets. The CPU queue retains its 2,048-item bound. GPU
admission holds at most 8,192 queued rays. Submitted and retained GPU allocations
together hold at most 16,384 rays, with at most 8,192 in one dispatch. Each ray visits
at most 512 cells. The maximum configured 64-by-64 atlas
update plus its cardinal importance ray fits a batch. Queue overflow returns the
original admission to scheduling. The router alternates CPU/GPU completion priority.

## GPU tracing and integration

`WorldProbeTraceBatch` uses the existing geometry and Surface Cache binding owners,
owned shader storage buffers, `GpuIndirectBuffer` and `GpuFence`. It polls without
waiting and maps only after completion. Exact submitted ranges bound partial compute
workgroups and prevent retained capacity from becoming extra work.

Tracing submission now follows the UE radiance-cache pattern:

- Upload one 64-byte probe record containing an integer origin, local fraction,
  tracing limits, atlas resolution and selected-direction range.
- Upload only 4-byte texel selectors for the existing sparse/PIS direction policy.
  A high-bit selector identifies the optional nearby cardinal query. No per-ray
  origin or direction vector is uploaded.
- A GPU setup pass creates 8-byte tile references (probe index and relative
  direction offset), atomically builds the indirect dispatch count, and then
  publishes both through shader-storage and command memory barriers.
- Each indirectly dispatched 8-by-8 group traces up to 64 selected directions for
  one probe. The shader generates directions from texel centers using our existing
  octahedral mapping; the sparse selector list preserves CPU/GPU scheduling parity.
- The output buffer contains 80-byte answers only and is never uploaded from CPU.

For 32 atlas directions plus one nearby query, input is 64 + 33*4 = 196 bytes per
probe instead of 33*128 = 4,224 bytes, excluding batch overhead. This is a layout
comparison, not a measured frame-time improvement. Our record is larger than UE's
16-byte record to preserve integer-world precision and our explicit tracing contract.
Probe scheduling and sparse selection remain CPU-owned; direction construction,
tile generation and indirect trace dispatch run on the GPU.

Ray origins are split into integer cells and local fractions before float conversion.
The shared voxel traversal uses an inclusive endpoint for world probes, matching CPU
hits and verified sky at the distance boundary. Existing visibility consumers retain
their exclusive endpoints. World height is captured once from the game accessor for
the backend lifetime.

Parity testing also found that CPU collision bounds added integer world anchors to
float box offsets before widening to double. Those additions now promote the anchors
first, preserving block boundaries at large signed world coordinates.

Geometry outcomes remain independent of lighting: hit, verified upper-boundary sky,
distance limit, traversal budget, unpublished geometry, coverage exit and unsupported
geometry remain distinguishable. Missing hit lighting retains the exact integer hit
descriptor for existing Surface Cache retries. Valid black lighting remains valid.

Completed ray answers feed the existing CPU integrator as an ordered geometry answer
source; this does not retrace terrain. Shared direction selection and metadata policy
preserve atlas indices, signed log distance, confidence, sky multiplier, short-range
occlusion and importance. Any unresolved primary geometry retains the existing
whole-admission completion gate. Ready hit lighting bypasses a redundant cache query.

Production completion keeps the answer allocation resident. The full-answer readback
method remains available for diagnostic parity tests; the backend uses compact completion.

## Hybrid completion and commit

A completion compute pass exports 16 bytes per direction (outcome, reason, distance
and descriptor index), a four-byte descriptor count, and 80-byte descriptors only for
hits whose lighting is unresolved. Ready RGB stays in the original GPU answer buffer.
Coverage/unsupported fallback needs only the outcome and original admission's selectors
and origin, so it does not copy hit descriptors. The fence covers both traversal and
completion compaction; CPU mapping occurs only after a nonblocking poll signals completion.
Readback is `4 + 16 * rays + 80 * unresolvedHits` bytes. The all-unresolved case is larger
than the previous readback; this is a residency contract, not a measured speedup.

Each admission receives a render-thread lease on its exact resident range. CPU fallback
workers still receive only immutable CPU values. The backend retains the allocation
through delayed fallback and cache queries; it cannot recycle or overwrite live ranges.
The 16,384-ray bound includes retained allocations until their last lease retires.
Exhaustion holds queued work without claiming new tickets. Backend retirement invalidates
all leases, and immutable result copies share an idempotent release.

The common CPU geometry integration still computes AO, importance, confidence and signed
distances from compact metadata. Resolved GPU hits reference their resident indices;
CPU-confirmed hits and unresolved GPU hits use the existing bounded Surface Cache query
path. Those query answers still return through CPU staging. Moving that separate query
resolver entirely onto the GPU is not required for ready trace payload residency.

`WorldProbeHybridCommit` validates the original scheduler ticket, geometry object and
invalidation revision, and cache dependency immediately before atlas dispatch. One
workgroup writes all ready directional samples, synchronizes image writes, and then
writes probe metadata. GPU samples fetch resident RGB; CPU-resolved hits and established
sky use staged values. Image/texture/framebuffer barriers publish the complete commit.
Unresolved primary geometry never reaches this dispatch. Missing lighting publishes only
the ready subset; retry descriptors keep the original ticket without retaining already
committed RGB. A separate `WorldProbeCommitIdentity` retains the original geometry/cache
validation after the resident lease retires, including retries with no ready samples.
Rejection leaves displayed history untouched. Renderer resource replacement
retires the backend before another clipmap generation can consume its results.

Hybrid staging costs 48 bytes per probe plus 32 bytes per ready sample. CPU-only raster
publication retains its 40-plus-24-byte cost. Scheduler admission and publication charge
the corresponding format; for 64 directions an atomic hybrid commit requires 2,096 bytes.
A smaller configured frame budget does not admit that L0 update; cheaper L1 work may still
fit. A publication that cannot fit its remaining budget writes neither directions nor
metadata and returns the admission to normal scheduling.

## Bounded CPU fallback

`WorldProbeCpuFallbackService` owns one worker using the existing collision tracer
with vanilla lighting disabled. Each admitted immutable packet contains the original
probe ticket, ordered direction selectors and retained GPU answers. Only answers
explicitly classified as coverage exits or unsupported geometry are retraced. The
worker restarts those directions from the original double-precision probe origin and
original distance limit, including the separate short cardinal importance segment.
Resolved GPU hits, sky and missing-light hit descriptors are copied unchanged.

Limits apply to queued, running and completed-but-undrained work together:

- At most 64 outstanding fallback admissions.
- At most 8,192 retained directional answers across those admissions.
- At most 256 CPU ray starts per rendered frame; unused credit does not accumulate.
- At most 512 visited cells per ray through the production collision tracer.

When storage credit is exhausted, the GPU backend retains the unqueued readback
admission and its original in-flight ticket until credit becomes available. It does
not restart the GPU trace or copy answer slices repeatedly while the queue is full.
The worker waits asynchronously for frame credit and checks ticket validity before
each CPU trace and after the merge. Cancellation does not wait on terrain access on
the render thread; worker synchronization resources retire after the worker exits.

CPU hits become Surface Cache descriptors with integer block identity, face normal,
local hit fraction and distance. The cache query accepts published unsupported shapes
whose collision was confirmed by CPU, while still validating current block identity,
capture readiness and initialized lighting. Unsupported shapes remain non-traversable
by GPU geometry; missing captured lighting remains a lighting retry.

CPU sky, unavailable geometry, distance limits, traversal exhaustion and invalid input
remain distinct. A CPU-incomplete primary ray fails the admission under the existing
geometry-completion rule, leaving displayed history intact for normal retry. Once
geometry completes, ready GPU directions can publish while unresolved hit lighting
uses the existing partial-publication retry path.

## Lifetime changes

A flag change retires queued and in-flight admissions under each scheduler level's
lock, advances their tickets, and returns eligible slots to scheduling. Deferred
disable/dirty decisions are respected. Old completions cannot complete replacement
admissions. Idle valid probes and displayed atlas history are retained.

The renderer disposes the old router and pending Surface Cache query, clears delayed
descriptors, and creates a new routing lifetime before admitting new work. CPU workers
capture their owning scheduler rather than the mutable renderer scheduler field.
Existing cache dependency and clipmap resource identity changes dispose the entire
router and reset dependent history. World leave and renderer disposal dispose it too.

Each GPU result drain checks the submitted geometry object, its invalidation revision
and the cache dependency revision, including when results span multiple frames.
Scheduler tickets are checked by the existing publication path. Failed dispatches or
readbacks produce failed completions so scheduler requests can retry. The backend
borrows geometry and cache resources and owns its dispatch/readback resources and bounded CPU fallback queue.
Fallback completions repeat the original geometry object/revision, cache dependency
and ticket checks after worker completion; workers never dereference GPU resources.
Flag changes, world leave and backend disposal cancel outstanding fallback work.

## Validation

The initial routing task's subagent-run validation on 2026-09-25 passed all 43 focused
tests with no skips (at that time the L0 entry point used a temporary CPU adapter):

- Exact work-item routing for both flag values and L0/L1/L2, queue rejection, fair
  completion draining, and backend ownership/disposal.
- Enabled-by-default JSON configuration and persistence of both explicit settings.
- Queued/running ticket retirement, rejection of old claims and completions,
  replacement admissions, and preservation of idle valid probes.
- Actual renderer flag changes on to off to on while Surface Cache queries are
  pending: same atlas resources and exact retained pixel values, retired queries,
  and resumed admissions after restoring budgets.
- Existing scheduler, trace service, Surface Cache transport and delayed-result
  regression coverage.

The root reviewed the changes and new tests. Source inspection confirmed that cache
and clipmap replacement dispose the router through `PrepareSurfaceLighting`, and
world leave resets the scheduler before router disposal. These lifecycle paths were
reviewed in source; the new runtime test specifically exercises flag changes.

Evidence: `artifacts/TestResults/world-probe-trace-routing.trx` and
`artifacts/world-probe-trace-routing-tests.log`. Production build/deployment passed
with zero warnings/errors (`artifacts/world-probe-trace-routing-deploy.log`).

The compute implementation's final subagent-run suite passed **141 tests**, zero
failures or skips, in 2 minutes 17 seconds. Coverage includes:

- CPU/GPU full-cube hit parity at zero and positive/negative 2^24 world offsets,
  including all six face normals, material identities and sub-voxel distances.
- Shared direction selection with importance sampling on/off, and both enclosed and
  mixed sky/floor metadata parity (AO, confidence, importance and signed distances).
- Exact surface/sky endpoints, finite distance limits, traversal exhaustion,
  unpublished geometry and distinct coverage/unsupported fallback classifications.
- Lit, valid-black and missing-cache hits; the full backend retains missing-light
  descriptors for cache retries without requiring lighting to trace geometry.
- Bounded admission, maximum atlas selection plus cardinal query, partial compute
  workgroups and exact active-range readback after buffer capacity reuse.
- Rejection of geometry/cache lifetime changes between individual result drains.
- Default L0 compute publication into the real atlas with zero CPU worker traversal
  or vanilla-light reads; both gather modes and atomic upload under both backends.
- Switching routing while actual CPU cache queries or GPU compute fences are
  pending, retaining exact displayed atlas values and resuming new admissions.
- Existing CPU outcome, scheduler/service, producer/environment and transport tests.

Receipts: `artifacts/TestResults/world-probe-compute-final.trx` and
`artifacts/world-probe-compute-final-tests.log`. Production build/deployment passed
with zero warnings/errors (`artifacts/world-probe-compute-deploy.log`). Test compilation
retains five existing analyzer warnings in unrelated tests. Source and test review
found no blocker within this task's boundary.

At that point GPU-resident hybrid publication remained open. Live
visual and matched-workload performance validation remain open. No game was launched.

The compact-input/tiled-dispatch follow-up passed **143 regression tests**, zero
failures or skips, followed by **14 rebuilt admission/backend checks** covering the
final pre-claim input validation. Production build/deployment passed with zero
warnings/errors. New checks cover 64-byte probe/80-byte answer layouts, mixed probes
spanning multiple tiles, partial tails, indirect counter reset on smaller reuse,
disjoint complete output ownership, invalid selectors and dispatch-binding restoration.
Existing CPU/GPU direction and lighting parity checks now exercise GPU-generated
directions through the compact submission path.

Receipts: `artifacts/TestResults/world-probe-tiled-final.trx`,
`artifacts/TestResults/world-probe-tiled-admission.trx`, and
`artifacts/world-probe-tiled-deploy.log`. This validates probe submission and tracing;
it does not establish GPU-resident atlas publication or a measured performance gain.

The bounded CPU fallback implementation passed **180 regression tests**, zero failures
or skips, followed by **12 rebuilt worker checks** with strengthened retained-GPU-lighting
assertions. Production build/deployment passed with zero warnings/errors; test compilation
retains the five existing unrelated analyzer warnings. Coverage includes:

- Selective fallback preserving ready GPU radiance and missing-light descriptors exactly.
- Original signed probe origins, retained cardinal selectors and all CPU ray outcomes.
- Per-frame nonaccumulating ray credit and inclusive queued/running/undrained storage bounds.
- Real partial collision-box hits and holes, and coverage exits followed by CPU-established sky.
- Delayed geometry/cache/ticket rejection and nonblocking worker cancellation.
- Unsupported CPU-confirmed Surface Cache hits, valid-black lighting, stale block identities,
  withheld capture data and existing consumer/partial-page readiness guards.

Root review and the testing subagent's independent source audit found no blocker in
selective traversal, backpressure, cancellation or lifetime validation. Receipts:
`artifacts/TestResults/world-probe-fallback-final.trx`,
`artifacts/TestResults/world-probe-fallback-worker-final.trx`, and
`artifacts/world-probe-fallback-deploy.log`. GPU-resident hybrid publication was the next
task. Measured runtime/performance validation remains open; no game was launched.

The hybrid completion/commit task passed **205 expanded regression tests**, zero failures
or skips, then **30 rebuilt final checks** including three additional retry-identity cases:
**208 distinct tests** across both runs. The final run includes pooled staging and all
final ownership fixes. Production build/deployment passed with zero warnings/errors;
SPIR-V contracts are current. Coverage includes:

- Real resident nonzero/valid-black radiance and all metadata matching diagnostic readback.
- Mixed CPU collision fallback and untouched GPU radiance in the same atlas commit.
- Compact readback byte counts, sparse descriptors and old allocations surviving buffer reuse.
- Inclusive resident bounds and resumption only after every referencing lease retires.
- Geometry/cache/ticket/backend retirement and provider failures rejecting before atlas writes.
- CPU-only lighting retries retaining original identity after GPU allocation release.
- Partial publication/retry histories, flag switching, insufficient-budget atomicity and
  distinct L0/L1 scheduling costs. Lost resident ownership cannot publish placeholder black.

Root review and the testing subagent's independent source audit found no remaining blocker.
Receipts: `artifacts/TestResults/world-probe-resident-regression.trx`,
`artifacts/TestResults/world-probe-resident-final.trx`, and
`artifacts/world-probe-resident-deploy.log`. Live visual acceptance and matched-workload
performance measurements remain open; no game was launched.
