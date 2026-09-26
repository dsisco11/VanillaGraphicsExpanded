# GPU integration-test performance analysis

Date: 2026-09-25. Scope: the GPU integration suite and its shared test patterns, including
shader contracts, GPU resource lifetimes, Surface Cache producers, and complete lighting transport.
The source/restoration regression is a representative workload, not the scope boundary.

The initial investigation below preserved production rendering behavior, test assertions, tolerances
and completion requirements, and removed its temporary instrumentation. Subsequent implementation
of within-test resource reuse is recorded at the end of this report. Build/test measurements are
delegated to a subagent, with a separate implementation reviewer. No game is launched.

## Measured baseline

The uninstrumented baseline at source revision `370c6c34` built successfully in **73.889 seconds**,
including approximately 43 seconds in the shader build. The subsequent `Category=GPU` run used
`--no-build --no-restore` and took **1,207.326 seconds (20.12 minutes)** of command wall time.
It reported **1,962 cases: 1,956 passed, three failed, three skipped**. This is one complete suite
sample, not a statistical estimate of suite variance or a passing correctness gate.

Environment: Debug, .NET 10, Windows; one GPU test process at a time. The instrumented context
reported NVIDIA GeForce RTX 4090/PCIe/SSE2, OpenGL 4.3.0 NVIDIA 591.86. Fresh test processes were
used, but driver and filesystem caches were not cleared: these are not cold-cache measurements.

The 135 reported classes account for 1,201.460 seconds of summed case durations. Class totals
below are attribution by test family, not isolated GPU timings. The 21 `SurfaceLighting*` classes
sum to 455.963 seconds; these overlap the individual rows below and must not be added to them.

| Family | Cases | Summed case seconds | Interpretation |
| --- | ---: | ---: | --- |
| `SpirvGraphicsLifecycleTests` | 237 | 186.922 | Repeated linking/reloading is intentional coverage |
| `LumOnDirectWorldProbeVisibilityTests` | 82 | 141.551 | Already uses per-test variant/target reuse; needs separate attribution |
| `SurfaceLightingRuntimeScenariosTests` | 12 | 72.918 | Repeated full producer/consumer transitions |
| `SurfaceLightingConsumerRuntimeTests` | 13 | 70.376 | Transport, worker and publication integration |
| `SurfaceLightingSpatialRuntimeTests` | 10 | 68.798 | Movement/mixed consumers; includes one failed case |
| `SpirvInventoryTests` | 500 | 56.765 | Large variant inventory, many individually cheap cases |
| `SurfaceLightingPbrLifetimeTests` | 6 | 43.101 | Full composition and retained-resource transitions |
| `SurfaceLightingTemporalComponentTests` | 5 | 39.191 | Multi-step temporal component workloads |
| `LumOnNearFieldGeometryDebugFunctionalTests` | 15 | 36.323 | Includes the three 65-position sweeps |

Evidence: `artifacts/gpu-suite-profile-build.{log,json}`, `gpu-suite-profile-full.{log,json}`,
and `artifacts/TestResults/gpu-suite-profile-full.trx`, with `.cases.csv` and `.families.csv`
exports beside the TRX. These are timing receipts, not evidence that the outstanding broader
correctness gate has passed.

### Representative repeats and build isolation

Before the profiling run, a 33-case representative selection passed twice in **70.024** and
**70.175 seconds** of command wall time. It spans the camera sweep, SH9 projection, buffer/state
contracts, producer refresh and four cache-replacement cases. The exact filter is in
`artifacts/gpu-suite-profile-representative.filter`; receipts are
`gpu-suite-profile-repeat-1.trx` and `gpu-suite-profile-repeat-2.trx`. The pair was consistent,
but two samples do not establish a general variance bound. Profiling narrows this selection to
the three camera sweeps and four replacement cases; it is not a third full 33-case repeat.

The cached, no-restore instrumented build took **9.599 seconds**, with the shader catalog already
current. An earlier instrumentation-build attempt entered unavailable-network restore after its
package-cache environment was omitted; that owned attempt was stopped, retained in logs and excluded
from comparisons. This illustrates why build/restore setup must be kept separate from GPU runtime.

### Temporary attribution: what actually costs time

Seven instrumented cases passed in **65.510 seconds** of command wall time. The three camera
sweeps account for 33.905 seconds of summed case duration, of which **33.161 seconds (97.8%)**
was inside 195 `ComponentShaderPrograms.Create()` calls. That scope includes program construction,
initialization, binary specialization/linking and validation; it is not a pure driver compiler timer.
This is the strongest measured low-risk candidate: keep all camera observations and reuse the
invariant program inside each test.

The four replacement cases account for 28.605 seconds of summed case duration:

| Scope or counter | Aggregate across four replacement cases | Interpretation |
| --- | ---: | --- |
| Consumer constructor bodies | 10.919 s | Excludes field initializers |
| Graphics startup/registration, nested in constructor | 10.668 s | Full production graphics set, including debug-family expansion; do not add to constructor time |
| Consumer frames | 378 calls, 13.853 s | 161 frames per PBR case, 28 per basic replacement case |
| Cache callbacks, nested in frames | 13.352 s | Includes production callbacks, lazy setup and waits; not isolated GPU tracing time |
| Final/world observations, nested in frames | 0.465 s | Readbacks, finite scans and GL error check |
| Requested one-millisecond sleeps | 346 calls, 3.493 s | Mean elapsed 10.10 ms; scheduling effects included |
| Eager wait diagnostics | 48 calls, 0.107 s | Includes mandatory intermediate finite assertions |
| Requested-lighting readiness | 24 calls, 0.077 s | Includes readiness mapping and 576 requested-page reads, 442,368 bytes |
| World readback helper | 438 calls, 0.187 s, 219 MiB | Overlaps observations/diagnostics/assertions; helper calls only |
| Consumer disposal | 0.044 s | Small in this sample; not all suite/context teardown |

The four fixtures recorded zero `RuntimeProbeWorld.WorkerReads`. This means no counted CPU
world-block traversal, not proof that all asynchronous work was absent. Do not remove worker
opportunity globally based on these GPU-path cases. Investigate why each wait is pending before
changing the sleep policy, and preserve frame/completed-work comparisons and delayed-worker tests.

These measurements change the implementation priority: program reuse within a scenario comes
first; registration and first-frame/lazy setup need further attribution before changing integration
boundaries. The wait policy deserves a controlled experiment. Diagnostic/readback cleanup remains
useful, but its measured cost does **not** justify calling it the dominant slowdown or undertaking
a risky synchronization rewrite for a presumed large saving. The 219 MiB payload is real; large
payload alone is not evidence of a large elapsed-time cost.

Raw evidence: `artifacts/gpu-suite-profile-scopes.jsonl`, the instrumented TRX,
`artifacts/GpuIntegrationPerformanceEvidence.md` and its exact commands/filters. Only seven cases
were instrumented; there are no exclusive GPU timestamps, allocation-only timings or whole-suite
readback counters. Program registration and frame scopes still contain multiple costs. Separate
these further when implementing the follow-up tasks instead of extrapolating four cases to the
whole suite. No optimized candidate was run during that initial investigation, so its attribution
measurements alone demonstrate no speedup; see the subsequent matched implementation runs below.

All four instrumented source files were restored byte-for-byte and the temporary helper removed.
The restored source rebuilt successfully in **6.376 seconds**, with current shader assets. The
retained changes from that initial investigation were documentation and follow-up tasks only.

### Failures and skips observed during measurement

The unchanged-source broad run failed:

- `GpuComputePipelineSpirvIntegrationTests.DirectFileUsesExplicitContractDespiteArbitraryFilename`:
  `InvalidOperation` was reported before SPIR-V creation.
- `GpuFramebufferBlendStateIntegrationTests.AttachmentBlendDisable_OverridesGlobalBlendEnable`:
  the same GL error category was reported before shader creation.
- `SurfaceLightingSpatialRuntimeTests.MixedConsumersFollowReplacementPolicy(sh9: true)`:
  `Dark channel 0: 0` at the expected-lit assertion.

An isolated four-case repeat passed both GL cases and the mixed-consumer non-SH9 control, but
failed the SH9 case again (`gpu-suite-profile-isolated-failures.trx`). The GL failures are therefore
context/order-sensitive candidates, not diagnosed causes. The SH9 failure is reproducible in this
selection; it must not be hidden by changing timing, skipping the case or loosening assertions.
This investigation does not establish when any of these failures first appeared.

The three existing skips concern `IndirectTint_AppliedToHitRadiance`,
`DistanceFalloff_AppliedToHitRadiance`, and the obsolete L1
`Gather_SHFromProjected_StableVsIntegration` comparison. No new skip was introduced. Compare
future candidate outcomes against this exact baseline rather than claiming all GPU cases passed.

## Harness inventory

Paths below are relative to `VanillaGraphicsExpanded.Tests/GPU/` unless stated otherwise.

| Test family | Existing execution pattern | Repeated work to measure | Coverage that must remain |
| --- | --- | --- | --- |
| Shader/component contracts | `Fixtures/LumOnShaderFunctionalTestBase.cs`, `ComponentShaderPrograms.cs`, `Helpers/ShaderTestHelper.cs`; real programs and small authored textures | Binary asset reads, SPIR-V specialization/linking, program/interface setup, textures, fullscreen geometry, output readback | Real shader contracts, variants, complete numerical/directional outputs |
| Buffer/framebuffer/program lifetime | `Gpu*IntegrationTests`, `SpirvGraphicsLifecycleTests`, shared `RenderTestBase` | Fresh resource creation, mapping, binding, synchronization, disposal | The creation, replacement, failure and disposal paths under test; sharing the subject would bypass coverage |
| Surface Cache producer | `Fixtures/SurfaceCacheRuntimeFixture.cs`; controlled engine inputs and registered callbacks | Geometry/material setup, page capture/lighting, per-page readiness observations, geometry edits | Real capture/publication, partial readiness, stale versus invalid identity, bounded service |
| Full lighting consumer/PBR | `Fixtures/SurfaceLightingConsumerRuntimeFixture.cs`, `RuntimeLightingHost.cs` | Full shader registration per fixture, full callback chain per frame, world atlas scans, asynchronous worker service, temporal convergence | Production owner wiring, cache-to-world-to-screen transport, retained histories, composition and lifecycle transitions |
| Workload/cost diagnostics | `SharedGeometryCostMeasurementsTests`, `SurfaceWorkDiagnosticTests` | Deliberate coverage/size matrices, completion counts, GPU drains | Bounded work, source revisions, upload/storage costs; do not replace with a tiny numerical scene |

`GpuTestCollection` already shares a `HeadlessGLFixture` and serializes tests within the GPU
collection. `NearFieldMaterialCaptureCollection` has a separate context and disables collection
parallelization to protect singleton material state. Context creation is therefore not repeated
for every case. Per-case program and scene ownership is still repeated.

The production shader build emits SPIR-V ahead of testing. Runtime methods named `Compile*`
load/specialize/link those binaries; they are not evidence of GLSL source compilation on each test.
The distinction matters when attributing shader build versus test execution time.

## Source-confirmed repeated costs

### Observation and failure diagnostics

`SurfaceLightingConsumerRuntimeFixture.Frame()` reads and scans both `IndirectFullTex` and the
complete world radiance atlas after every frame, when allocated. `Energy()` checks every component
for finiteness as well as computing peak RGB. These are real assertions, not redundant logging.

For the spatial fixture's resolution 8, one level and 8 by 8 direction tiles, the world atlas is
512 by 64 texels. Its float RGBA readback allocates/transfers 524,288 bytes per call; 100 such
observations request 50 MiB before any predicate, diagnostic or final assertion reads. This is
derived payload, not measured GPU bandwidth or an estimate of elapsed time saved.

`DynamicTexture2D.ReadPixels()` (production `Rendering/DynamicTexture2D.cs`) allocates a float
array, creates/binds a temporary framebuffer, reads synchronously, and disposes the framebuffer.
Reducing the number of calls can save more than array allocation alone, but must preserve
synchronization and the observed domain.

`SurfaceLightingConsumerRuntimeFixture.RunUntil()` eagerly interpolates its failure message on
success too. The message invokes seven texture observations: final, world, world confidence,
screen history, filtered history, gather, and anchors. The final `condition()` call is also repeated
after the loop, potentially repeating a costly predicate. `SurfaceCacheRuntimeFixture.RunUntil()`
similarly formats diagnostics unconditionally, although its message primarily inspects CPU state.

The consumer message has a hidden assertion contract: `Energy()` checks finiteness on history,
filtered history and gather too. Those checks are not all duplicated by `Frame()`. Simply making
the entire interpolation lazy would remove successful-path checks. Separate mandatory observations
and assertions from optional diagnostic formatting, and reuse existing same-frame observations
where equivalent; do not count all seven reads as safely removable overhead.

`AllRequestedLightingReady()` maps readiness storage and, once page readiness is established,
reads outgoing texels page by page to reject partial initialization. Repeated predicate evaluation
can therefore perform many GL operations even for a small final image. Removing this inspection
and accepting a page's ready flag would weaken the current initialized-texel contract.

`CompleteConsumers()` already avoids final brightness as a readiness criterion. It requires two
successful world publications (the first may have been admitted before refresh), followed by
stable screen history and metadata at complete-sweep intervals. Preserve those semantics when
optimizing observation. A valid sample is not necessarily a fresh sample.

### Synchronization is partly implicit

`Fixtures/TestUniformRing.cs` uses one persistent, coherent uniform-buffer page. Its `BeginFrame()`
resets allocation through the production ring and explicitly documents reliance on readbacks.
It does not call `GpuUniformRingBuffer.EndFrame()` to insert a fence. Production `BeginFrame()`
waits only for a previously inserted fence before resetting the page write offset.

Consequently, deleting synchronous readbacks is not a safe isolated cleanup: subsequent frames
could overwrite uniforms still used by submitted GPU commands. More ring pages alone only postpone
the hazard. An explicit frame/submission lifetime must cover every reuse, including
`RenderTestBase.EnsureContextValid()`, cache `Frame()` and `PrimeGeometry()` entry points. Some
render-target helper calls also invoke `EnsureContextValid()`, so one test method is not necessarily
one uniform allocation interval.

Failure-only diagnostics are the lower-risk first candidate while ordinary frame readbacks remain.
Even that candidate needs failure-path and asynchronous completion validation; extra readbacks may
have incidental timing or GL-binding effects.

### Repeated initialization and CPU harness work

`RuntimeLightingPrograms.Initialize()` invokes `VgeShaderPrograms.RegisterAll()` per consumer
fixture, invoking 20 registration entry points, with the debug entry expanding into multiple
programs, including paths the current scenario may not draw.
`ComponentShaderPrograms.Create()` already provides a narrower, production-program component path.
These are distinct integration boundaries: replacing full registration in all runtime tests would
remove startup/wiring coverage.

There is also avoidable-looking repetition *inside* individual tests, where ownership can stay
isolated. `LumOnNearFieldGeometryDebugFunctionalTests.GeometryView_PublicationIntervalCameraSweep`
renders 65 camera positions for each of three origins. Its `RenderGeometry()` creates the same
configured debug program and a new output framebuffer on every call. `ComponentShaderPrograms`
retains all these programs until test disposal. Reusing one program and output target within each
test while updating all camera/binding inputs would preserve all 65 observations without requiring
cross-test sharing. The monolithic-versus-dedicated entrypoint test must still create both variants.
For the three sweep cases this would change 195 program/target creations to three, while retaining
195 draws and readbacks. That is an operation-count opportunity, not a measured elapsed-time saving.
`SurfaceLightingTemporalTestBase.Accumulate()` likewise creates a temporal shader for every history
step, and `NearFieldShaderTestBase.Trace()` creates a trace shader for each invocation. Temporal
tests need every history step and intermediate assertion; they do not inherently need a new
program at each step. Audit variant/configuration differences before applying within-test reuse
to these helpers. Shader binding theories such as `LumOnUniformTests` are a separate granularity
question: grouping assertions by program could reduce linking, but needs preserved per-binding
failure reporting and an explicit coverage map rather than silently dropping theory rows.

There is an existing local model: `DirectWorldProbeVisibilityTestBase` caches programs by variant
within the test owner and reuses scene-owned targets. It deliberately avoids screen-family
allocation for debug consumers. Extend that ownership pattern where appropriate rather than adding
a process-wide live-program cache. Its relatively expensive tests cannot be assumed to suffer
from the same per-draw relinking problem; profile their geometry, variants and actual draws.

`BinaryShaderApiFixture` rereads binary assets when requested and creates proxy objects. Immutable
asset caching is less invasive than sharing live `GpuProgram` objects, but override/reload tests
must still observe changed bytes, missing assets and `BeforeRead` callbacks. Live programs contain
mutable settings, uniforms and engine/context ownership; they are not immutable reusable inputs.

`SurfaceLightingConsumerRuntimeFixture.Frame()` recreates terrain arrays/raster inputs every frame.
`SpatialLightingScene.Feedback()` rebuilds authored patch sets; readiness and feedback submission
can request them repeatedly. `RuntimeRenderEvents.Render()` filters, sorts and snapshots callbacks
on each stage. These are candidates for measured scene-input or registration-version caching, with
invalidation for camera, room, door, material and registration changes. Their presence alone does
not establish that they dominate runtime.
Reusing array storage while rewriting every element is safer than caching authored values:
`Receiver` can be a delegate whose captured state changes without replacing the delegate itself.

The consumer wait loop sleeps one millisecond after each simulated frame to let real workers run.
Actual sleep duration and useful worker progress must be measured. Removing the sleep, increasing
budgets, or replacing workers with synchronous mocks changes scheduling; none is automatically a
coverage-preserving speedup. Delayed-worker and stale-completion tests must retain actual overlap.

## Candidate requirements and risks

Start with within-test program/target reuse, then investigate setup inside runtime callbacks and
the wait policy. The safety constraints below apply even to candidates with low measured cost.

1. **Reuse invariant programs/targets within a test, then reduce immutable setup across tests.**
   Start with repeated camera/direction sweeps whose program settings and target dimensions do not
   change. This keeps test isolation and every scenario while avoiding repeated setup inside a
   case. Verify all changing uniforms, bindings and output initialization on each draw. Keep
   independent resources for simultaneous history branches and actual recreation/lifetime tests.
   Measure asset I/O versus specialization/linking before
   choosing a cache. Share immutable authored data or binary bytes with explicit invalidation;
   retain fresh mutable resources and startup/reload/lifetime coverage. Live shader sharing is a
   separate, higher-risk proposal requiring context and state-reset design.
2. **Review simulation work by test contract.** Classify fixed loops as required temporal/work
   samples or mere settling. Replace only settling with completion-driven gates; retain history
   convergence, current directional publications, matched bounce work and any minimum observation
   window. Do not lower tolerances, reduce ray coverage or increase budgets in bounded-budget tests.
3. **Separate required assertions from failure diagnostics.** Small-to-medium change in shared wait
   helpers. Retain the complete message on failure and explicit successful-path finite checks for
   final, world and intermediate outputs. Make only optional diagnostics conditional, and avoid
   re-evaluating a successful predicate merely to assert it. Verify predicate side effects and
   exception behavior before caching its result.
4. **Give test GPU submission an explicit uniform-buffer lifetime.** Medium cross-harness change;
   prerequisite for reducing synchronization. Validate multiple frames in flight, context teardown,
   ring reuse and helper re-entry. Preserve bounded failure behavior and no extra test skips.
5. **Reuse observations within a frame and batch requested-page checks.** Medium change after the
   lifetime contract. Associate snapshots with frame plus resource identity/generation; invalidate
   after writes/recreation. Preserve every required finite-value, alpha/readiness and directional
   check. Compare a reusable readback owner/pooled buffers and batched reads with existing GPU
   abstractions before proposing new raw GL plumbing. Sparse domains should not require reading
   unused atlas storage; tests asserting the entire atlas must retain that coverage.
6. **Review integration boundaries and scheduling last.** Keep explicit end-to-end representatives
   for production registration, transport, composition and lifetime. Consider focused GPU fixtures
   for repeated numerical permutations only with an assertion/failure-mode coverage map. Do not
   parallelize contexts in-process while engine platform, shader imports, material registry,
   samplers and the test uniform ring have shared ownership. Process isolation is a possible future
   experiment, not a free speedup on one GPU; include contention and duplicated setup in timings.

## Measurement and validation contract

Record build wall time separately from `dotnet test --no-build --no-restore`. Capture the exact
filter, configuration, driver/renderer, test counts, failures/skips and fixture frame/work counts.
Report first invocation and subsequent invocations accurately: a new process is not a cold driver
or filesystem cache. Per-test summed durations omit runner/context costs and can overlap; they
are not a replacement for process wall time.

Instrumented CPU elapsed scopes identify where the render thread spends time. A synchronous
readback can charge earlier GPU work to its caller, and workers overlap the render thread. These
scopes must not be called exclusive GPU execution times or added across nesting boundaries.
Instrumentation must preserve the existing assertions, calls and ordering. Repeated uninstrumented
measurements establish the timing envelope around the diagnostic attribution.

For subsequent implementation use repeated baseline/candidate runs with identical filters and
assertions, compare frames and completed work, retain failure diagnostics, and get a separate
implementation review. A quicker run with changed work or additional skips is not accepted as an
equivalent speedup. Keep this investigation separate from the outstanding correctness completion
gate in `LumOn.WorldProbeSurfaceLighting.todo`.

## Independent source review

A separate reviewer checked the harness recommendations and confirmed the missing test-ring fence
dependency, the global-state restrictions on parallelism, and the intermediate finite-value
assertions hidden inside diagnostic interpolation. The recommendations above incorporate those
constraints. The reviewer also confirmed that within-test reuse is appropriate to investigate for
the 65-position camera sweep, provided every draw updates its inputs and overwrites or explicitly
clears the reused output. This is a source review of proposed changes, not validation of an
implemented optimization.

## Within-test program and target reuse implementation

The geometry-debug harness now retains one program per dedicated/monolithic identity and one
framework-owned render target per test. The three camera sweeps still execute all 195 draws and
readbacks; they create three programs/targets rather than 195. `RenderQuadTo` still clears the
target before each draw, and camera, world-origin and geometry bindings are updated every time.

`NearFieldShaderTestBase` retains programs by all eight variable shader selections authored by the
harness. Fixed world levels and HZB selection remain unchanged. Geometry, Surface Cache, histories,
terrain inputs, uniforms and suppression are rebound on every invocation. Its scene-owned targets
already supported reuse; no shared history or process-wide program cache was introduced.

`SurfaceLightingTemporalTestBase` retains its invariant temporal program. Each history branch still
owns separate ping-pong targets and binds its own textures and frame parameters on every step.
The existing `ComponentShaderPrograms` and `ShaderTestFramework` owners dispose the retained objects.
`ComponentShaderPrograms.Create` itself remains unchanged, including fresh creation for lifetime tests.

Existing scenarios, loops, readbacks, numerical bounds, per-frame finite checks and sleep policy
are unchanged. A separate regression, `TraceRebindsInputsAfterBranchAndVariantChanges`, interleaves
branches, suppression and a world-cache specialization change before restoring the original bright
draw. It checks exact restored outputs, source flags, separate targets and clean GL state. This new
case is excluded from the matched timing filter and included in affected regression validation.

The independent reviewer found no missing shader selections, stale bindings, ownership changes or
removed assertions, and reviewed the new regression's nonzero lighting and specialization checks.

### Matched timing results

The subagent ran the same 77 cases twice on the original binary, then twice on the candidate,
using `--no-build --no-restore` in fresh test processes with one GPU process at a time. All four
runs passed 77/77 with identical test-name/outcome sets and no skips. This is sequential AABB
measurement, not randomized or ABBA sampling; driver caches were not cleared.

| Command wall time | Run 1 | Run 2 | Mean |
| --- | ---: | ---: | ---: |
| Original | 112.050 s | 108.558 s | 110.304 s |
| Reuse | 38.236 s | 38.169 s | 38.202 s |

The selected workload's mean wall time fell **65.4%**. This is a measured improvement for these
77 cases, not an extrapolated whole-suite speedup. The candidate build took approximately 60
seconds, including shader catalog compilation, and is excluded from the test timing comparison.

| Family | Cases | Original mean summed case time | Reuse mean summed case time |
| --- | ---: | ---: | ---: |
| Geometry debug | 15 | 34.755 s | 3.749 s |
| Temporal components | 5 | 42.228 s | 6.849 s |
| Near-field functional | 57 | 28.864 s | 24.197 s |

Evidence: `artifacts/invariant-reuse.filter`, `invariant-reuse-comparison.csv`,
`invariant-reuse-summary.json`, and baseline/candidate `invariant-reuse-*-1/2.trx` receipts under
`artifacts/TestResults`. The original 20.12-minute suite baseline and its unresolved failures remain
historical evidence; this implementation does not close the separate broad correctness gate.

The separate affected regression passed **117/117, zero skips**, in 75.404 seconds of command wall
time. It covers the affected near-field, shared-scene, material-readiness, Surface Cache hit/transport,
temporal and geometry-debug families, including the new branch/variant round trip and existing
borrowed-resource ownership test. See `artifacts/invariant-reuse-regression.filter` and
`artifacts/TestResults/invariant-reuse-regression.trx`. No production rendering or global shader
factory changes were made, and the sleep-policy task remains separate.

### Additional within-test shader reuse

The shadow receiver comparisons now retain one direct-lighting program per test. The partial-metallic
combine case creates one program for its three material inputs. The gather partial-validity,
sample-stride, and leak-threshold comparisons each create one program for both draws. All dynamic
bindings, readbacks, and assertions remain in place, with program disposal still owned by the existing
per-test `ComponentShaderPrograms`. No cross-test sharing was introduced.

This removes eleven redundant program creations across the ten directly affected cases. Validation
covers all three containing test classes; these additional changes have no matched timing baseline.

Validation passed: **35/35 cases, zero failures or skips** (11 combine, 18 gather, 6 direct-shadow).
The subagent-run build completed with zero errors and five existing analyzer warnings; the focused
test command took 12.262 seconds. Receipt: `artifacts/TestResults/additional-shader-reuse.trx`.
This duration is a validation receipt, not a measured speedup.

### Contract-only SPIR-V program loading

Removed eager `StageShader.LoadAndApply` from `GpuProgram.CompileAndLink` and deleted the unused
stage-source wrapper. The binary loader now installs its engine uniform dictionary directly from
`GpuProgramInterface.Uniforms`, whose compiled contracts include active numeric locations and
inactive `-1` entries. No GLSL regex scan or reconstructed source is needed for these bindings.

The consumer audit found no other production readers of the removed wrapper's source/AST fields.
Vertex and fragment engine slots remain initialized by `Initialize`; optional geometry slots are
prepared before committing a linked candidate. Specialization/link status checks, raw driver logs,
interface checks and failed-reload preservation remain intact. Build-generated source maps are a
separate pending task; runtime binary loading does not reconstruct GLSL for diagnostics.

Independent source review passed. Regression coverage rejects non-SPIR-V asset reads during the
catalog reload inventory and compares the engine dictionary against active/inactive contract entries
across reload.

Subagent validation passed: matched baseline and candidate each **378/378, zero skips**, with identical
test-name/outcome sets; the additional binary-only contract-dictionary regression passed **1/1**.
The build passed with zero errors and six existing warnings (87.23 seconds). Receipts are
`artifacts/TestResults/glsl-removal-{baseline,candidate,binary-only}.trx` and corresponding logs.

The single sequential baseline/candidate comparison took **224.521 seconds versus 414.730 seconds**
(TRX run elapsed, build excluded). This is a materially slower observed candidate run, not evidence
of a speedup or an isolated cost of GLSL processing. Its cause is unassigned; the candidate build
regenerated the shader catalog, and this measurement does not isolate driver/cache or environment
effects. Further controlled profiling remains necessary before drawing a performance conclusion.
The inventory contains no geometry stages; optional geometry-slot construction was source-reviewed,
not exercised by this catalog. No live-game acceptance or whole-suite result is claimed.

### Dependency-driven integration-test completion

`SurfaceLightingConsumerRuntimeFixture.RunUntil` no longer sleeps after every frame. It checks the
requested condition after rendering, then observes one pending GPU fence or CPU progress event.
GPU waits use `GpuFence.Wait` on the original context thread and never consume the owner's fence.
CPU notifications do not dequeue results; render callbacks retain collection and publication.

Both CPU services report completion, idle and shutdown. Fallback additionally wakes when ray credit
is exhausted so the next frame can replenish it. Notifications are allocated lazily only for actual
observers. The deliberately held worker exposes an entry milestone, raced against CPU progress so
rejected claims cannot cause a false timeout. Held-and-entered workers return control to the test.

Existing simulated-frame bounds remain, with a cumulative ten-second external-wait budget per
RunUntil invocation and dependency-specific timeout messages. Existing readbacks, finite/nonempty
checks and uniform-buffer synchronization remain unchanged. No per-frame full-pipeline drain was
introduced. The GPU test source inventory contained one unconditional sleep site; it is removed.

Validation and review passed: 22 focused completion cases and a 53-case regression (zero failures or
skips), covering consumer, runtime-scenario, PBR lifetime, CPU notification, held-worker and fence
ownership cases. Build warnings were existing analyzer warnings and unavailable NuGet vulnerability
metadata. Separate review caught and resolved the held-worker/no-entry race; final predicate and wait
logic passed review. Completion predicates run once per rendered frame, with an additional check only
for the explicitly held-worker milestone; existing eager diagnostic assertions still execute.

Timing is inconclusive. Two initial four-case baseline runs averaged 29.784 seconds but used earlier
build outputs. A controlled sleep-policy comparison using current production and identical staged
shader hashes took 31.460 / 28.532 seconds; final dependency-wait runs took 60.649 / 52.025 seconds.
All passed identical four-case selections. This sequential comparison showed a slower candidate and
must not be presented as a speedup; its cause is not established.

A subsequent temporary instrumentation run passed the same four cases in approximately 20 seconds
of reported test duration. It recorded 48 RunUntil calls, 318 wait-helper calls and only 6.179 ms total
wait-helper elapsed, versus 7,583.672 ms total RunUntil elapsed. Frame counts remained 161 for each PBR
case and 28 for each basic case (378 total), matching the historical workload. This run shows neither
additional frames nor long completion waits, but does not explain the earlier timing variability.
Instrumentation was removed byte-for-byte, the final fixture rebuilt, and all 348 staged SPIR-V hashes
remained unchanged. No whole-suite performance claim is made.

Evidence: `artifacts/dependency-waits-*.log/json`, corresponding TRX under `artifacts/TestResults`,
and the temporary instrumentation receipt. Broader regression command wall time was 171.135 seconds.

## Remaining-cost profiling after harness changes

The completed follow-up investigation is recorded in
[Remaining GPU integration-test costs](GPU.IntegrationTestPerformance.RemainingCosts.md), including
source-boundary mappings, current full-suite results, scoped attribution and ranked follow-up work.

The current 33-case selection passed twice in 38.009/35.088 seconds. The full 1,966-case GPU run took
1,265.019 seconds: 1,961 passed, two recurring failure signatures and three skips. No overall-suite
speedup or clean correctness gate is claimed. The prior isolated four-case candidate rerun also
passed in 25.951 seconds, so the earlier 52–61 second result was not consistently reproduced.

Measured priorities are repeated variant linking (uniform checks and selected direct-visibility
scenarios), broad runtime shader registration, and repeated source parsing in test-side validation.
Later frames still link shaders, so subsequent-frame scopes are not pure steady-state rendering.
Input generation/upload and completion waits are small measured costs in the selected scenarios.

Subagent-run scoped selections passed 141/141 and 9/9. Independent review verified the numeric
receipts, nested accounting and assertion-boundary constraints. All temporary instrumentation was
removed, 13 source files restored byte-for-byte, 348 staged SPIR-V hashes verified unchanged, and
the final uninstrumented build passed. Remaining correctness failures retain their separate backlog.

The subsequent [compiled uniform validation refactor](GPU.UniformContractValidation.md) removes
GLSL processing from `LumOnUniformTests`, preserves its 124 critical-value labels, and reduces
that selection from 142 links to nine. Debug/Release validation and timing limits are recorded
in the linked report; this does not change production loading or establish a whole-suite speedup.

The subsequent coverage audit superseded that broad suite with existing binding tests and focused
production-setter packing coverage. The linked report retains historical timings and describes the
current consolidation; the custom SPIR-V test reader is no longer needed.

## Required wait observations and failure-only diagnostics

The consumer wait retains unconditional nonempty/finite checks for final, world, trace, filtered and
half-resolution gather outputs. Their five energies are retained as one boundary-local value and
reused when a failed wait needs a message. Confidence and anchor readbacks, plus program/log
formatting, now occur only on failure. Successful waits therefore omit two optional texture
readbacks; per-frame final/world checks and synchronization remain unchanged.

The cache wait also defers self-check strings, source summaries and history formatting until
failure. It evaluates its predicate once initially and once after each simulated frame, retaining
the result rather than invoking it again for the assertion. Consumer worker-held milestone checks
and external-wait budgets are unchanged.

Focused tests exercise successful and exhausted waits, complete diagnostic fields, exact predicate
counts, and NaN injection into each intermediate consumer output despite a successful predicate.
This change does not remove mandatory output scans, reduce their pixel domain or alter uniform-ring
retirement.

Subagent validation: 22/22 regression cases passed; final focused checks passed 2/2, including
the one-frame predicate transition. Separate implementation review passed.

A temporary four-case replacement run passed 4/4 and retained 378 frames (161/161/28/28).
Across 48 successful wait boundaries, required output validation took 49.840ms. Temporarily
re-enabling and separately timing optional diagnostic collection took 12.332ms, including 96
optional GPU readbacks. Those optional calls are absent from the final success path. This is a
small isolated measurement, not a whole-suite speedup estimate. No per-frame observations were
removed. Instrumentation was restored byte-for-byte, followed by a passing final Debug build and
focused run. Evidence: `artifacts/RuntimeWaitObservations-*` logs, scopes, summary, restoration
hashes and matching TRX files.
