# Surface Cache capture admission

This implements the coverage-aware capture admission and retry task in
[the tracing throughput checklist](LumOn.WorldProbeSurfaceLighting.todo). Geometry coverage, allocation
budgets, indirect tracing and temporal weighting are unchanged.

## Source eligibility

The capture owner now checks source prerequisites before submitting GPU work. A four-by-four voxel
patch is aligned within one 16-block geometry publication cell. `TraceGeometryCapturePatch` decodes
its exact integer source bounds and face direction; capture identity checks share that decoder.

`TraceGeometryGpuScene` supplies a dependency stamp containing logical/world-clipped coverage
membership, the source publication slot version and the completed material-table revision. A local
publication/withdrawal advances that slot's version. Unrelated geometry publication does not advance
the source's stamp. No geometry or material texture is read back for admission: the owner uses its
existing immutable geometry snapshots and published table snapshot.

All sixteen source voxels must be published. Air is capturable as an initialized empty source. Solid
and unsupported-collision sources require a published material, face-table readiness and a nonzero
face surface ID, matching material capture rather than the stricter geometry tracing contract.
The existing GPU completion and CPU capture-identity guards remain authoritative before publication.

`LumonSceneCaptureAdmission` caches the result by physical page, virtual key, slot generation, source
chunk/patch and dependency stamp. Repeated unavailable inputs do not repeat the sixteen-voxel check.
A source change, reentry into coverage, publication, relevant ownership change or scene replacement
allows reevaluation. Completed table revision changes allow material reevaluation; unrelated table
changes can cause CPU reevaluation but still cannot admit an unavailable source.

## Retry ownership and fairness

New requests may allocate residency while their source is unavailable. They retain `NeedsCapture` and
an explicit pending ticket, without consuming GPU capture work. Source eligibility is checked before
recapture changes page flags, so loss of coverage alone does not erase valid captured lighting.

Tickets include physical ownership and chunk-slot generation. A finite retry sweep snapshots those
identities; retirement or slot reuse cannot turn an old sweep entry into a recapture of its replacement.
Admission records the ticket before submission. Successful capture removes it; GPU/identity rejection
keeps it blocked until its source dependencies change. Failed completion reads or unavailable dispatch
resources retain retry eligibility without treating unsubmitted work as valid capture.

When a sweep finishes, changed demand or scene/table publication triggers a rebuild. Only eligible
pages enter the next sweep; unavailable tickets remain pending. An unchanged scene and unchanged
pending demand do not rebuild the same empty sweep every frame. GPU rejection also remains deferred
under an unchanged dependency stamp. Queue/cache storage is bounded by current resident identities;
pool replacement and world leave retire it.

Each rebuilt sweep prefers recent visible allocation demand, then source-chunk distance, with stable
key ordering for ties. The current feedback compactor reports unmapped pages only, so this is a
60-frame recent-demand signal, not a new per-frame resident-visibility query. New allocation demand
continues through the existing visible-request priority path. No extra feedback pass/readback was added.

The sweep is not resorted or restarted after admission. New requests and repeated explicit dirty
notifications coalesce behind its current cursor, preserving eventual progress for older eligible
pages. At most eight eligible recaptures are submitted per frame. If a source becomes unavailable
mid-sweep, skipping it does not consume that eight-page GPU budget. CPU sweep inspection is bounded
by the finite resident snapshot; this change does not impose a new CPU time budget for rebuilding it.

## Observations and evidence

The existing self-check line additionally exposes:

- `capturePending`: retained retry tickets, including currently deferred sources.
- `captureEligibilityChecks`: full CPU source validations, excluding cached decisions.
- `captureDeferredChecks`: rejected admission checks, not unique pages or GPU failures.

The previous `captureFail` field remains failed/submitted GPU captures. Consequently, zero
capture failures with pending pages can now mean intentional CPU deferral. The
[measurement report](LumOn.SurfaceCache.TracingBaseline.md) describes the other counters and timing
units. Previously recorded gameplay failures are historical evidence, not post-change rates.

Controlled runtime cases establish that 48 out-of-coverage residents do not submit GPU captures while
16 covered patches complete; missing material remains deferred through unrelated publication and
resumes after source recovery. Cache tests distinguish air, unavailable geometry, materialless solid,
valid full-cube and capturable unsupported-collision sources. These checks do not establish game frame
time or convergence; the later user-run acceptance task remains open.

## Validation

The subagent-run focused selection passed 29 tests. The broader task regression passed 121 tests
with zero skips, covering capture admission, partial-page publication, refresh, consumer transport,
signed streaming, page reuse and retained lighting. The normal production build/deployment passed
with zero warnings and zero errors. Receipts and the reproducible command are
`artifacts/TestResults/surface-capture-admission-regression.trx`,
`artifacts/surface-capture-admission-deploy.log` and
`artifacts/surface-capture-admission-command.ps1`.

Two `ProbeRingPreservesOverlapWithinStableGeometryCoverage` cases failed in the initial broader run
and are excluded from the 121-test receipt. Their assertions assume a two-probe-per-axis layout while
the shared fixture defaults to eight; selecting its short-range option instead fails initial lighting
settle because trace reach becomes insufficient. That experiment was reverted without weakening
assertions. This remains an unresolved broader validation gap, not a proven preexisting failure.
The initial result is retained in `artifacts/TestResults/surface-capture-admission-initial-regression.trx`
and the fixture experiment in `artifacts/TestResults/surface-capture-ring-fixture-investigation.trx`.
The other spatial movement/streaming cases pass. No game process was launched.
