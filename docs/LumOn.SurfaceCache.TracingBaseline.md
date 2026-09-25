# Surface Cache completion measurements

This records the measurement task in [the implementation checklist](LumOn.WorldProbeSurfaceLighting.todo).
It changes observability, not capture eligibility, geometry coverage, ray completion, scheduling budgets,
or temporal weighting. Those changes remain separate tasks.

## Collection and ownership

`LumOn.LumonScene.SurfaceWorkDiagnosticsEnabled` defaults to true. Turning it off disables the new
GPU counters and timestamp queries without resetting lighting, capture identity, or history. Existing
page-batch readiness counters and render-thread wall measurements remain available.

`SurfaceLightingDispatch` and `LumonSceneCaptureVoxelComputeShader` each own a `SurfaceWorkDiagnostics`
collector. Each collector has at most eight pending samples, each containing 23 uint counters, a
timestamp pair, and a fence. Storage uses the existing GPU buffer/query/fence abstractions. Collection
polls without waiting and maps counters only after their fence signals and timestamps are available.
When the ring is full, the dispatch still runs with instrumentation disabled and increments `skip`.
Unknown completion after a failed fence poll retires the storage rather than reusing it. World leave
disposes the collectors without waiting. Immutable snapshots keep reporting separate from mutable
render-thread aggregation.

There are no per-ray log messages, additional blocking diagnostic readbacks, or changes to existing
publication synchronization. The existing ten-second readiness report also emits bounded
`[VGE] Surface cache work:` aggregate lines. Counts and time totals are cumulative within their owner
lifetime; compare differences between reports to calculate rates. Counter reads may lag the current
frame, and the current callback's wall interval closes after its report.

## Units and interpretation

Stages are `Capture`, `Seed`, `Indirect`, `Direct`, `Combine`, and `Reset`. For each stage:

| Field | Meaning |
| --- | --- |
| `dispatch`, `pages` | All submitted dispatches and page work items, including uninstrumented work |
| `sampled`, `skip`, `readFail` | Collected dispatch samples, disabled/full-ring samples, and collection failures |
| `Texels` | Invocations selected by the stage's page/bucket selector |
| `Completed` | Texels successfully written by that stage; this is not whole-page capture/publication or convergence |
| `Unchanged` | Seed texels already initialized, so no new write was needed |
| `Empty`, `Hidden` | Completed zero/hidden texels; useful initialization, but no traced ray |
| `OriginOutside`, `OriginUnpublished`, `OriginUnsupported`, `Unseeded` | Texels rejected before any indirect ray starts |
| `Nonfinite` | Texel results rejected because their inputs or estimate were nonfinite |
| `Rays` | Rays actually started, not requested rays that an earlier failure prevented from running |
| `Hit`, `Sky`, `Outside`, `Unpublished`, `Unsupported`, `Budget`, `Distance` | Mutually exclusive outcomes for started rays |
| `HitMaterial`, `HitLighting` | Subsets of `Hit` that could not resolve material or the current Surface Cache lighting lookup |
| `CaptureOutside`, `CaptureUnpublished`, `CaptureMaterial` | Mutually exclusive capture-source rejection reasons, in texels |

Thus `Rays = Hit + Sky + Outside + Unpublished + Unsupported + Budget + Distance` for collected work.
Do not add the hit-failure subsets to that total. A multiple-ray texel can have several completed rays
and still retain its previous value when a later ray is unresolved. `Completed` is the successful
texel denominator for that distinction. Capture texel completion precedes the independent CPU identity
guard; `captureIdentityReject` records whole-page rejection at that guard. Existing `captureFail` and
`indirectFail` remain page/batch counters, not ray counters. Zero-valued detailed counters are omitted.

`submitMs` is render-thread wall time around one instrumented dispatch submission, including its
buffer setup and query/fence overhead. `gpuMs` sums available timestamp intervals around stage
submission, including ordered GPU work and possible submission gaps; it is not isolated shader ALU
time. `observedMs` is dispatch-to-collection latency, including the delay until the owner next polls.
GPU and observation totals cover only `sampled` work. They cannot be divided by all submitted pages
when samples were skipped.

`feedbackRenderWallMs` and `relightRenderWallMs` measure their complete enabled callbacks, including
driver waits and diagnostic collection. The feedback callback also performs feedback/residency work,
so its total is not capture-kernel cost. `captureMapMs` and `mapSeedMs`/`mapIndirectMs`/`mapDirectMs`/
`mapCombineMs` measure the existing synchronous map calls, with call counts after `/`. They exclude
subsequent CPU processing and unmapping. These are nested wall intervals; never add them to callback
totals or describe them as CPU execution time.

Queue metrics are bounded by physical page identities and distinguish:

- `captureAdmitted`: age since first capture admission until successful whole-page capture. Pages
  never admitted are absent, so this is a lower bound on residency-to-capture waiting.
- `seedEligible`: age since observed capture eligibility until all initial seed buckets succeed.
- `indirectProgressEligible`: age since observed eligibility until an indirect batch makes any useful
  texel progress, before combine/publication. Subsequent observations start a new interval; this does not claim a full sweep or
  multi-bounce convergence. Empty/hidden completion is included and separately visible in counters.

Each reports pending count, oldest pending age, successful completion count, and summed completion
milliseconds. Retries preserve age. Retirement/reassignment is not successful completion. Slot
generation and page identity prevent reuse from inheriting old ages. Lighting queues restart on
producer resource/settings reset; temporary unavailability can extend an already observed interval.

## Historical gameplay reference

The relevant earlier log is
`D:/CODE/VintageStory/VintagestoryAutomation/Data/Logs/client-main.log`, last written
2026-09-25 00:10:58 local time. At 00:09:46 it recorded 4,657 of 5,006 resident pages awaiting capture,
55,428 failures out of 55,786 capture attempts, and 11,683 incomplete indirect batches out of 11,822.
There was one producer reset and no recorded readback/combine failure. Partial successful texels can
publish even in incomplete indirect batches. These old counters do not provide a per-reason breakdown.

Its saved settings used surface geometry resolution 128, near radius 8 chunks, four relight pages
per frame, 64 texels per page visit, one ray per texel, 64 DDA steps and history limit four. Production
near pages have 16x16 texels at four texels per voxel face edge. Direct/indirect alternating visits
therefore average 128 indirect sample slots per frame for fully seeded pages; this is an admission
bound, not a measured completion rate.

The historical diagnosis is not a matched before/after performance comparison. The new user-run
gameplay counters are needed to attribute that world's current failures. No game process was launched
for this task.

## Controlled baseline and validation

The matched measurement uses the production capture and lighting shaders in a headless GL context,
with identical fixture inputs and ray seeds in instrumentation-off/on ABBA blocks. It measures
the additional GPU diagnostics path and establishes reproducible useful-work counts for future changes; it is not a
gameplay throughput estimate or an algorithmic improvement claim.

Both sides retain lightweight CPU dispatch accounting. The fixture dispatch comparison does not
measure the cost of the renderer's full residency/queue bookkeeping relative to the pre-change binary.
It deliberately synchronizes outside the measured CPU submission/poll intervals so each GPU interval
has completed before collection; this isolates matched dispatches rather than simulating frame overlap.

The 2026-09-25 controlled run passed all 72 legs: capture/direct/indirect, resolved/outside-coverage
inputs, three independent ABBA blocks each. Each leg warms eight dispatches then measures 64 dispatches
of four 8x8 pages, 64 texels per page, one ray per indirect texel and 64 traversal steps. These fixture
pages are smaller than production 16x16 pages; they match 256 selected texels per dispatch but not the
production alternating scheduler, sparse buckets or residency workload. There are 4,608 measured
dispatches in total, with 2,304 successfully collected enabled samples and no enabled skips. The test
asserts identical initialized output texels across every matched off/on leg; unallocated atlas storage
is excluded from comparison.

Hardware/runtime: NVIDIA GeForce RTX 4090, OpenGL 4.3 NVIDIA 591.86, Intel64 Family 6 Model 151
Stepping 2 (24 logical processors), Windows build 26200, .NET 10.0.12. These are warmed Debug
production-shader measurements from the test harness, not game frame timings.

| Stage / workload | Mean GPU interval per dispatch, diagnostics off | Diagnostics on | Successful texels per enabled 64-dispatch leg |
| --- | ---: | ---: | ---: |
| Capture / resolved enclosure | 18.5 us | 70.7 us | 16,384 |
| Capture / outside coverage | 18.3 us | 61.6 us | 0; 16,384 CaptureOutside |
| Direct / resolved enclosure | 22.4 us | 70.0 us | 16,384, including 7,168 hidden |
| Direct / outside coverage | 16.5 us | 76.0 us | 0; 16,384 OriginOutside |
| Indirect / resolved enclosure | 41.5 us | 101.0 us | 16,384, including 7,168 hidden; 9,216 rays/hits |
| Indirect / outside coverage | 91.6 us | 137.7 us | 0; 16,384 rays/coverage exits |

The table averages the six legs per setting. GPU intervals include the fixture's submission sequence,
using an external timer equally on both sides. The additional diagnostics path has measurable cost:
all 18 paired workload/block comparisons increase the interval, by 33.7 to 70.3 us per dispatch.
This is not a claim that the shader itself slowed by that amount: extra counter-buffer submission,
query/fence commands, counter atomics and host submission gaps all contribute. The JSON also retains
CPU submission, collection, process CPU, total wall and internal sampled timestamp intervals. Do not
sum overlapping CPU/GPU intervals or use total fixture wall time as game-frame cost.

| Stage / workload | Mean CPU submission + collection wall per dispatch, off / on | Mean internal sampled GPU interval, on |
| --- | ---: | ---: |
| Capture / resolved enclosure | 18.3 / 85.5 us | 13.9 us |
| Capture / outside coverage | 18.4 / 78.0 us | 13.0 us |
| Direct / resolved enclosure | 24.2 / 77.8 us | 22.1 us |
| Direct / outside coverage | 15.6 / 91.8 us | 13.4 us |
| Indirect / resolved enclosure | 17.8 / 84.9 us | 45.0 us |
| Indirect / outside coverage | 21.4 / 87.6 us | 82.1 us |

The internal interval starts after diagnostic counter reset, whereas the external interval includes
that submission overhead. Neither interval is a standalone measurement of shader atomics. CPU
submission and collection are disjoint intervals; explicit fixture GPU waiting is outside both.

The unresolved indirect case costs more than the resolved enclosure even with diagnostics off,
while completing no texels. This establishes a reproducible example of why attempted work is not a
useful throughput metric. It does not establish the frequency of that case in the user's world.

Measurement receipts:

- `artifacts/surface-work-diagnostics-measurements.json`: hardware, configuration, all 72 legs and counters.
- `artifacts/TestResults/surface-work-diagnostics-measurement.trx`: one opt-in test, passed, no skips.
- `artifacts/surface-work-diagnostics-measurement.log`: build and measurement output.

The measurement entry point is `SurfaceWorkDiagnosticMeasurementTests.MatchedDiagnosticsOffOn`.
Set `VGE_RUN_SURFACE_DIAGNOSTICS_MEASUREMENTS=1` and
`VGE_SURFACE_DIAGNOSTICS_MEASUREMENT_OUTPUT` to a fresh JSON path when reproducing it. Preserve the
same workload configuration and ABBA ordering for future comparisons.

## Correctness and build evidence

All **92 focused regression tests passed**, with zero failures/skips, in 1 minute 50 seconds.
Coverage includes exact outcome classifications, material capture failure, completed versus unchanged
stage counts, off/on numerical parity, pending queue age and identity reuse, bounded collection,
immutable snapshots, disposal, runtime diagnostics toggling without dependency/readiness reset,
world leave, capture/relight callback wiring, publication/partial progress, refresh and consumers.
The normal production build/deployment passed with zero warnings/errors. Test compilation retained
five existing analyzer warnings in unrelated test files.

The broader run exposed and corrected two fixture assumptions: the new unseeded case now explicitly
resets GPU storage before inspecting direct validity; the older seed-outside-coverage case now keeps
its captured source inside coverage while placing only its exterior lighting cell outside. Moving the
source itself outside correctly suppresses seed admission through the existing capture-identity guard.
Neither correction changed production lighting policy or weakened the intended outcome assertions.

Root source review and test-agent inspection found no remaining production blocker in this measurement
change. The tests establish bounded ownership and accounting but do not force a driver fence failure
or deterministic ring saturation. Timing values are reported evidence, not correctness thresholds.
The later throughput implementation review and user-run convergence tasks remain open.

Receipts:

- `artifacts/TestResults/surface-work-diagnostics-regression.trx`
- `artifacts/surface-work-diagnostics-regression.log`
- `artifacts/surface-work-diagnostics-deploy.log`
- `artifacts/surface-work-diagnostics-command.ps1`: exact regression, opt-in measurement and build commands.

For later gameplay comparisons, preserve the same world, camera, coverage, page/texel/ray/step budgets,
history limit, warm-up and observation interval. Record both capture/lighting backlog and differences
in completed work, failure reasons, callback/map wall times and sampled GPU times. Check `skip` and
`readFail` before interpreting a rate. Keep the instrumentation setting the same across algorithmic
comparisons, and do not compare this instrumentation-on run directly against the old aggregate-only
log as a speedup.
