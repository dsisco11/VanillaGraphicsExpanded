# Optional L0 GPU tracing: routing and bounded compute

The first task introduces `WorldProbeClipmap.EnableGpuTracing`, serialized and enabled
by default. Enabled routing sends L0 admissions to `LumOnWorldProbeGpuTraceBackend`;
L1 and higher use the CPU backend. Disabling it routes every level to the CPU backend.

The GPU backend now traces L0 against existing uploaded geometry and samples the
Surface Cache at resolved hits. It does not upload additional terrain. Coverage exits
and unsupported collision shapes are explicitly classified as needing CPU fallback.
That fallback is the next task: these admissions currently preserve history and retry
instead of publishing incomplete geometry or fabricated sky.

## Shared admission and publication

Both routes use the existing scheduler, admission tickets, work items, direction
selection and trace/upload budgets. The CPU queue retains its 2,048-item bound. GPU
admission holds at most 8,192 queued rays plus one submitted or completed batch of
8,192 rays. Each ray visits at most 512 cells. The maximum configured 64-by-64 atlas
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

The current integration reads back the bounded answer payload and uses the existing
atlas upload path. Keeping directional payloads GPU-resident and merging selective
CPU fallback are subsequent tasks, not performance claims for this implementation.

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
borrows geometry and cache resources and owns only its dispatch/readback resources.

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

Selective CPU fallback and GPU-resident hybrid publication remain open tasks. Live
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
