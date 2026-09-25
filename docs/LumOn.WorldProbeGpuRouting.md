# Optional L0 GPU tracing: backend routing

The first task introduces `WorldProbeClipmap.EnableGpuTracing`, serialized and enabled
by default. Enabled routing sends L0 admissions to `LumOnWorldProbeGpuTraceBackend`;
L1 and higher use the CPU backend. Disabling it routes every level to the CPU backend.

The compute tracer is the next task. For now the GPU backend forwards each admission
unchanged into the shared bounded CPU queue. This preserves working L0 lighting while
the remaining implementation is pending. This step does not run geometry traces on
the GPU, upload terrain, or establish a performance improvement.

## Shared admission and publication

Both routes use the existing scheduler, admission tickets, work items, direction
selection and trace/upload budgets. The temporary adapter shares the 2,048-item CPU
queue instead of adding another worker or multiplying queue capacity. Its results
are drained exactly once through the CPU backend. The router alternates completion
priority between backends in preparation for independent GPU completions.

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

GPU geometry resource ownership, asynchronous compute completion and per-direction
CPU fallback remain requirements of the subsequent tasks; the temporary adapter owns
no GPU geometry resources.

## Validation

Subagent-run validation on 2026-09-25 passed all 43 focused tests with no skips:

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

Live GPU tracing and performance validation remain pending implementation of compute
tracing. No game was launched for this validation.
