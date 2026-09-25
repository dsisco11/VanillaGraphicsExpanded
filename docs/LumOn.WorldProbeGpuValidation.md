# L0 GPU tracing validation

## Contract and scope

This report records the focused validation and separate review task in
[the implementation checklist](LumOn.WorldProbeSurfaceLighting.todo). The production
contract and resource ownership are described in [GPU routing and publication](LumOn.WorldProbeGpuRouting.md).
It covers optional L0 tracing against existing uploaded geometry, selective bounded
CPU fallback, Surface Cache lighting, and coherent atlas publication. L1 and higher
retain CPU tracing. It does not replace user-run in-game visual acceptance.

## Correctness coverage

The focused suite exercises production shaders, buffers, fences, collision traversal,
cache-query resolution, scheduler tickets and atlas publication in a headless GL context.
The new real-worker routing checks supplement the existing fake-backend routing tests.

| Requirement | Evidence owners |
| --- | --- |
| CPU/GPU full-cube parity, signed origins at zero and ±2^24, face normals and exact endpoints | `WorldProbeComputeTraceTests`, `WorldProbeTraceOutcomeTests` |
| Sky boundaries, finite distances, traversal exhaustion and unpublished geometry remain distinct | `WorldProbeComputeTraceTests`, `BlockAccessorWorldProbeTraceSceneTests` |
| Unsupported collision holes/hits, coverage exits, actual CPU-established sky | `WorldProbeCpuFallbackGpuTests` |
| Only designated fallback directions retrace, original origin/selector/distance, bounded worker credit and storage | `WorldProbeCpuFallbackTests` |
| AO, confidence, signed distance, sky intensity and importance metadata parity | `WorldProbeComputeTraceTests`, `WorldProbeResidentPublicationTests` |
| Lit/valid-black/missing-light hits and no vanilla-light dependency | `WorldProbeGpuBackendTests`, `WorldProbeCpuFallbackGpuTests`, `SurfaceLightingWorldProbeTransportTests` |
| Partial publication preserves ready directions, retry identity survives resident release | `SurfaceLightingPartialWorldProbeTests`, `WorldProbeResidentPublicationTests` |
| Compact readback, range ownership, allocation reuse and resident backpressure | `WorldProbeResidentPublicationTests`, `WorldProbeComputeTraceTests` |
| Obsolete geometry/cache/tickets, slot retirement, resource replacement and flag changes | `WorldProbeResidentPublicationTests`, `WorldProbeCpuFallbackGpuTests`, `WorldProbeTraceRoutingRuntimeTests`, scheduler retirement/origin tests |
| Flag-off CPU behavior and flag-on L1+ CPU behavior, including real workers | `WorldProbeTraceRoutingTests.CpuRoutesPreserveRealWorkerResults`: off L0/L1/L2, on L1/L2 |
| Mixed GPU/CPU commits and insufficient-budget rejection without metadata-only publication | `WorldProbeResidentPublicationTests`, `WorldProbeSchedulerBudgetTests`, consumer runtime tests |

The final unified run passed **213/213 tests**, with zero failures or skips, in
3 minutes 8 seconds. This includes the five added real-worker routing cases. An initial
assertion compared `ImmutableArray` storage identity; it was corrected to compare each
sample and all metadata, and the final unified run passes those cases.

Receipts: `artifacts/TestResults/world-probe-gpu-validation.trx`,
`artifacts/world-probe-gpu-validation.log`, and the exact filter in
`artifacts/world-probe-gpu-validation-command.ps1`.

## Separate implementation review

The independent review agent inspected production code without editing it or running
the tests. It found **no confirmed correctness blocker** within this task's contract.
Its inspection covered:

- Level routing and ticket retirement before backend replacement, retaining displayed history.
- Integer/fraction origins, double-precision collision anchors, inclusive endpoints and explicit outcomes.
- Queue/allocation/frame-credit bounds, backpressure and selective original-segment CPU fallback.
- Exact resident ranges, reference-counted lifetime, compact descriptor readback and worker isolation
  from GPU resource ownership.
- Original geometry/cache/ticket validation at delayed drains and immediate commit, including
  independent identity retained after resident release during lighting-only retries.
- Storage/command barriers before indirect tracing, fence-covered completion, and image writes
  before metadata publication and consumer barriers.
- Shared metadata integration, compact ready-hit descriptor omission, and all-or-nothing admission
  under the remaining upload budget.

Inspected owners include `LumOnWorldProbeGpuTraceBackend`, `WorldProbeTraceBatch`,
`WorldProbeCpuFallbackService`, `WorldProbeResidentAnswers`, `WorldProbeGpuLease`,
`WorldProbeGpuIntegration`, `WorldProbeHybridCommit`, renderer trace-routing/surface-lighting
partials and the trace/completion/commit shaders. The reviewer independently confirmed
that performance evidence is required before checking off the selected task, and that
live visual acceptance is a separate requirement.

## Measurement boundaries

The opt-in `WorldProbeRoutingMeasurementTests.MatchedFlagOffOnWorkloads` uses the
production router, CPU service/collision tracer, GPU backend, Surface Cache query
batch and atlas uploader. Scheduling claims are controlled and the Surface Cache is
already seeded. Scheduler selection, cache production, scene streaming and dynamic
edits are outside the timed workload.

The matrix contains a supported enclosure and the same enclosure with one wall
classified as unsupported by GPU geometry. That wall still has full-cube CPU collision;
this measures the fallback path rather than partial-shape collision complexity. Each
probe traces 64 directions from the same fixed origin. Batch sizes are one and eight,
with eight distinct atlas slots for the latter. Geometry/cache locality is deliberately hot.

Each of three ABBA blocks runs flag off/on/on/off. Each leg starts with cleared atlas
contents and eight warmup batches, followed by 128 measured probes: 128 single-probe
batches or 16 eight-probe batches. This gives 48 measured legs, 6,144 probes and 393,216
primary rays across both routes, excluding warmup. Every result must succeed; final
nonzero radiance, metadata, visibility and distance textures must match between routes.
Actual CPU trace calls and zero vanilla-light reads are checked separately.

Hardware: Intel Core i9-12900K (24 logical processors), NVIDIA GeForce RTX 4090,
NVIDIA 591.86, OpenGL 4.3 context, Windows build 26200, .NET 10.0.12. Measurements use
the Debug test build and existing GPU timer-query abstraction. No GPU test job runs
concurrently with the experiment.

Controlled measurements use identical admitted probes, selected directions, geometry
and lighting with the flag off and on. Warmup and alternating order reduce first-use
and ordering bias. CPU collision execution, render-thread submission, GPU elapsed time
and end-to-end completion latency are separate quantities; overlapping or nested scopes
must not be added into a claimed frame-time saving. Fallback frequency is based on actual
CPU calls under the GPU route, not on inferred GPU coverage.

GPU query intervals enclose contiguous trace/completion submission, cache-query
submission and atlas commit separately. They exclude asynchronous polling waits;
their sum is elapsed GPU command-interval time, not a hardware utilization measurement.
The CPU route's empty trace timer includes query overhead. Completion latency runs
from admission through GPU-confirmed atlas completion for the whole batch. Process CPU
time includes workers, polling, assertions, allocation and driver work, and is subject
to OS accounting granularity. Worker traversal duration is elapsed call time rather
than thread CPU time. Render-call duration includes active polling calls and is not
additive with process CPU or worker elapsed time. The harness polls aggressively rather
than once per rendered game frame.
Each batch waits for its commit timer result before the next batch starts. This verifies
GPU-complete latency but serializes batches, so it does not measure multi-frame pipeline
overlap or sustained gameplay throughput.

The separate reviewer also inspected the measurement harness and found no invalidating
ownership, timing-span or matched-count issue. In particular, each batch drains all
results and releases leases, keeping subsequent trace submission inside its timer.

The headless fixture's terrain accessor and small deterministic scenes are correctness
and comparison workloads. `ControlledBlockAccessor` uses `DispatchProxy` over a
dictionary-backed `ControlledVoxelWorld`; CPU traversal includes that proxy/reflection
overhead rather than the game's terrain-access implementation. Their timings do not establish game-thread cost, streaming
behavior, representative gameplay throughput or a live frame-time improvement. Visual
acceptance remains a user-run check. No game is launched by this validation.

## Measured results

The final fresh-atlas experiment on 2026-09-25 passed its output/work assertions:
**1/1 opt-in measurement test**, zero skips, 12 seconds. Each table row aggregates six
128-probe legs (768 probes / 49,152 primary rays); timing units differ by column.
GPU intervals sum the three nonoverlapping command intervals, not nested scopes.

| Scene | Probes/batch | GPU flag | Process CPU µs/probe | GPU intervals µs/probe | Completion ms/batch | CPU rays/probe |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| Supported | 1 | Off | 854 | 162.3 | 0.572 | 64 |
| Supported | 1 | On | 529 | 147.4 | 0.398 | 0 |
| Supported | 8 | Off | 387 | 46.2 | 1.927 | 64 |
| Supported | 8 | On | 102 | 22.2 | 0.644 | 0 |
| Mixed fallback | 1 | Off | 610 | 141.5 | 0.473 | 64 |
| Mixed fallback | 1 | On | 529 | 135.5 | 0.473 | 10 |
| Mixed fallback | 8 | Off | 346 | 46.1 | 2.116 | 64 |
| Mixed fallback | 8 | On | 122 | 27.3 | 0.921 | 10 |

GPU fallback frequency is **0%** in the supported scene and **15.625%** in the mixed
scene, exactly 10 of 64 rays per probe. Flag-off CPU calls are primary tracing, not
fallback. All routes produced matching nonzero radiance and probe metadata; no vanilla
lighting was queried.

For each ABBA block, compare the mean of its two on legs with the mean of its two off
legs using measured block wall time. Negative means the GPU route was faster in this
controlled workload; these are observed differences, not statistical confidence bounds.

| Scene / batch | On-versus-off wall changes in the three blocks |
| --- | --- |
| Supported / 1 | −28.1%, −26.6%, −33.1% |
| Supported / 8 | −67.6%, −56.8%, −73.2% |
| Mixed fallback / 1 | +3.7%, −5.3%, +6.6% |
| Mixed fallback / 8 | −50.7%, −63.0%, −51.7% |

Supported workloads and mixed batches of eight improved in every measured block.
The mixed single-probe case has **no consistent latency benefit**; its aggregate block
wall time was 60.656 ms off versus 61.510 ms on per 128 probes. Reduced CPU ray count
alone does not establish a latency win. None of these differences predicts in-game FPS.

Raw records retain trace/cache/commit GPU intervals, process CPU consumption, worker
traversal elapsed time, render-call elapsed time, completion latency and exact work counts:
`artifacts/world-probe-routing-measurements.json`. Receipts are
`artifacts/TestResults/world-probe-routing-measurements.trx` and
`artifacts/world-probe-routing-measurements.log`. Earlier receipts are preserved with
the `-initial` suffix; this report uses only the rebuilt run that clears atlas contents
before each leg's warmup.

The final production build/deployment passed with **zero warnings and zero errors**:
`artifacts/world-probe-gpu-validation-deploy.log`. Source/test review and both execution
tracks found no production defect requiring a change. The selected validation task is
complete; live visual acceptance and representative in-game performance remain separate.

## Reproduction

Run from the repository root, with no other GPU tests or timing experiments active.
The correctness command builds the current tests and shader contracts before execution.

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
$classes = @(
    'WorldProbeComputeTraceTests', 'WorldProbeGpuAdmissionTests',
    'WorldProbeGpuBackendTests', 'WorldProbeResidentPublicationTests',
    'WorldProbeCpuFallback', 'WorldProbeTraceRouting', 'WorldProbeTraceOutcomeTests',
    'SurfaceLightingConsumerRuntimeTests', 'WorldProbeScheduler',
    'WorldProbeTraceServiceTests', 'SurfaceLightingEnvironmentTests',
    'SurfaceLightingProducerTests', 'SurfaceLightingWorldProbeTransportTests',
    'SurfaceLightingConsumerTests', 'SurfaceLightingPartialPageTests',
    'SurfaceLightingPartialWorldProbeTests'
)
$filter = ($classes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj `
    --filter $filter --logger 'trx;LogFileName=world-probe-gpu-validation.trx' `
    --results-directory artifacts/TestResults `
    -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true

$env:VGE_RUN_ROUTING_MEASUREMENTS = '1'
$env:VGE_ROUTING_MEASUREMENT_OUTPUT = Join-Path $PWD 'artifacts/world-probe-routing-measurements.json'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj `
    --no-build --no-restore --filter FullyQualifiedName~WorldProbeRoutingMeasurementTests `
    --logger 'trx;LogFileName=world-probe-routing-measurements.trx' `
    --results-directory artifacts/TestResults
```

Measurement is opt-in and makes no timing-threshold assertions. Correctness assertions
check matched outputs and work counts; timing values are evidence to interpret with the
boundaries above. Saved execution commands also reside in
`artifacts/world-probe-gpu-validation-command.ps1` and
`artifacts/world-probe-routing-measurements-command.ps1`.
