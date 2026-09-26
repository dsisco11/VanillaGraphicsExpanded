# Remaining GPU integration-test costs

This investigation profiles the current harness after within-test resource reuse, contract-only
SPIR-V loading, and dependency-driven waits. It does not change production rendering or weaken test
assertions. The source audit is independent of timing attribution; both are recorded below.

## Measurement contract

- Build and restore time are separate from `--no-build --no-restore` test runs.
- One GPU test process runs at a time. Fresh processes do not imply cold driver/filesystem caches.
- Use the existing representative selection and a current broad `Category=GPU` run. Record cases,
  failures and skips; historical results are context rather than matched optimization controls.
- Temporary scopes retain every draw, readback, condition, frame limit and assertion. Restore the
  original source bytes and rebuild before completing the investigation.
- CPU wall scopes around GL calls include driver execution and synchronization. They are not GPU
  execution timestamps. Nested scopes are not additive, and unattributed remainder is not a measured
  subsystem. Managed allocations are measured on the current thread, excluding worker-thread,
  native and GPU allocations; nested scopes overlap.
- Instrumentation can perturb short scopes and nested parent totals. Use it for attribution within
  the observed run, not as an additional uninstrumented benchmark or proof of an optimization gain.

## Current uninstrumented measurements

Source revision: `d9e46b0a`, Debug/.NET 10. The separate build passed in **54.853 seconds** of command
wall time. All current runs used the same recorded staged SPIR-V inputs; Debug shaders use `-O0 -g`.

The unchanged 33-case representative selection passed twice in **38.009 / 35.088 seconds** of command
wall time (mean **36.548 seconds**, zero failures/skips). In the second run, summed case times were
12.565 seconds for PBR replacement, 10.771 for basic replacement, 5.271 for refresh, 2.423 for SH9
projection, and 0.735 for the three geometry camera sweeps. These family totals exclude runner overhead.

One full `Category=GPU` run took **1,265.019 seconds (21m 05s)**: **1,966 total, 1,961 passed, two failed,
three skipped**. This is not a passing suite gate or a matched before/after optimization experiment.
The historical 1,962-case run took 1,207.326 seconds; changed workload and timing variability preclude
attributing the difference to a particular harness change.

| Family | Cases | Summed case seconds | Interpretation |
| --- | ---: | ---: | --- |
| Direct world-probe visibility | 82 | 256.151 | Largest current family; already reuses within-test programs |
| SPIR-V graphics lifecycle | 238 | 181.313 | Fresh links/reloads are intentional coverage |
| SPIR-V inventory | 500 | 79.148 | Binary inventory coverage |
| Surface Lighting spatial runtime | 10 | 70.069 | Includes one failed restoration case |
| Surface Lighting consumer runtime | 13 | 67.213 | Full publication/worker/lifecycle paths |
| Surface Lighting runtime scenarios | 12 | 59.561 | Full source/restoration transitions |
| Debug renderer functional | 6 | 44.230 | Full debug-family startup and real mode callbacks |
| Surface Lighting PBR lifetime | 6 | 35.633 | Required retained-resource composition chain |
| LumOn uniforms | 134 | 27.198 | Repeated pair linking and declaration validation |
| Temporal renderer | 2 | 18.325 | Full runtime fixture around a narrow UBO upload assertion |

Recurring failures:

- `GpuComputePipelineSpirvIntegrationTests.DirectFileUsesExplicitContractDespiteArbitraryFilename`:
  `InvalidOperation` already present at the pre-load GL error check.
- `SurfaceLightingSpatialRuntimeTests.MixedConsumersFollowReplacementPolicy(sh9: true)`:
  expected-lit output reports `Dark channel 0: 0`.

These match historical signatures; no root cause or introduction date is inferred. The previously
observed framebuffer blend failure did not recur. The same three previously documented obsolete or
unsupported-path skips remain. This profiling task does not close their separate correctness work.

Receipts: `artifacts/gpu-remaining-{build,representative-1,representative-2,full}.json/log`,
`artifacts/TestResults/gpu-remaining-*.trx` and adjacent `.cases.csv`/`.families.csv`, plus
`artifacts/gpu-remaining-shader-hashes.csv`.

## Scoped attribution

The temporary instrumented selection passed **141/141**: three geometry camera sweeps, four
cache-replacement cases and 134 uniform cases. Command wall time was **87.028 seconds**. A separate
nine-case direct-visibility camera selection passed **9/9** in **19.008 seconds**. These are attribution
samples, not comparable benchmark totals for the uninstrumented selections above.

### Runtime scenarios and shader loading

| Scope | Calls | Summed seconds | Meaning |
| --- | ---: | ---: | --- |
| Consumer constructor body | 4 | 13.367 | Excludes object field initializers; includes registration |
| Full startup registration | 4 | 12.986 | Creates 120 programs; scenarios request 42 names in aggregate |
| Startup graphics link | 120 | 12.317 | Nested inside registration |
| Later-frame graphics link | 20 | 7.812 | Reconfiguration cost remains after startup |
| First cache frame | 4 | 1.391 | Includes 24 compute links taking 1.070s |
| Subsequent cache frames | 374 | 18.469 | Includes 56 compute links taking 6.185s; not purely steady-state rendering |
| Event dispatch and queued tasks | 1,134 | 8.060 | Includes main-thread tasks; not isolated sorting/dispatch overhead |
| World-probe callbacks | 378 | 5.790 | Inclusive callback work, including applicable lazy compute setup |
| Relight callbacks | 378 | 3.013 | Inclusive renderer work |
| Feedback callbacks | 378 | 1.498 | Inclusive renderer work |
| Consumer input generation | 378 | 0.0046 | Approximately 0.514 MB managed allocation |
| Consumer input upload | 378 | 0.0195 | Includes fallback depth/normal argument generation; approximately 0.160 MB managed allocation |
| Consumer observation | 378 | 0.826 | Existing readback/finite checks retained |
| Eager diagnostic construction | 48 | 0.130 | Retains its success-path assertions |
| Consumer disposal | 4 | 0.089 | Timed owner teardown |

Do not add the nested rows. The 8.060-second dispatch/task bucket cannot justify optimizing LINQ
sorting: queued main-thread work is included, and 7.812 seconds of graphics linking occurs during
subsequent frames. Source inspection confirms shader reconfiguration is queued through main-thread
tasks, but this profile does not isolate pure queue/sorting overhead.

Across the 141-case selection, 658 stage loads spent **0.168s** in argument preparation/asset access,
**0.012s** in binary submission and **2.517s** in specialization. Graphics interface preparation took
**0.097s** for 143 program links. The asset-fixture file-read subset took **0.067s** for 374 reads,
17.652 MB and roughly 17.808 MB of managed allocation; helper-direct file reads are outside this
file-I/O subset. Driver linking is much larger than these measured loading/interface costs.

The four runtime cases retain **378 frames** (161 per PBR case, 28 per basic case). Their 318 wait
observations took **7.888ms** total: 292 GPU-fence observations and 26 with no external work. The
GPU-fence sub-scope took 1.693ms. No CPU-wait/held-worker path was selected in these scenarios; zero
world block-worker reads does not establish that all asynchronous work was absent. Their dedicated
notification/overlap coverage remains in the completed wait-policy task.

World readbacks total **438 calls / 229,638,144 bytes**, taking **0.259s** in their scope; this overlaps
consumer observations and predicates. Readiness checks made 288 page reads / 221,184 bytes. These
measurements support retaining the planned observation cleanup, but not putting it ahead of linking
or removing synchronization before uniform-buffer retirement is implemented.

### Uniform validation

The 134-case family made **142 helper link calls**, totaling **35.248 seconds** in linking
(36.911 seconds including surrounding helper setup). Source processing ran **248 times**, taking
**7.600 seconds** and allocating **3.569 GB** of managed memory cumulatively. Explicit AST parsing ran
**196 times**, taking **2.348 seconds** and allocating **1.366 GB** cumulatively. These are allocation
traffic, not live heap size. The 98 source-member-check parent scopes overlap processing/parsing and
must not be added to those children.

These timings differ from the family's 27.198-second uninstrumented full-suite total. They establish
the repeated work within the profiled run, not a stable per-case cost or a predicted reduction.
The existing source checks remain valuable, but repeated preprocessing/parsing and linking are not
the property under test. Preserve the declaration and real active-interface assertions while sharing
their immutable inputs or grouping checks by exact shader variant.

### Direct visibility

The nine camera cases made **nine component program creations**, totaling **15.121 seconds**; their
graphics links consumed **14.467 seconds**. Their 27 stage specializations took 0.438s, asset file I/O
0.006s and nine compute links 0.096s. The dominant measured cost is the fresh program per theory case,
despite correct reuse across each case's repeated draws. Input uploads were not separately scoped in
this selection; their cost must not be inferred from the unmeasured remainder.

Each consumer is repeated for three signed-origin values while its specialization remains unchanged.
A bounded follow-up can group those origins per consumer and retain fresh scene inputs for every
origin, preserving all clear/blocked segments and bob steps. This would retain per-test ownership
instead of introducing a global live-program cache. No saving is claimed for the other 73 cases
without checking their variant and lifetime requirements.

Attribution receipts: `artifacts/gpu-remaining-scopes.jsonl`, `gpu-remaining-scopes.csv`,
`gpu-remaining-scope-totals.csv`, `gpu-remaining-direct-scopes.jsonl`, and the corresponding
instrumented JSON/log/TRX results.

## Recommended order of follow-up work

1. **Group repeated checks/draws sharing one shader variant within a scenario.** Start with uniform
   validation and the nine direct-visibility camera cases above. Retain every named assertion and
   input, with explicit failure aggregation where grouping changes case granularity. This has the
   strongest direct evidence; effort is moderate because isolation/reporting must be preserved.
2. **Narrow runtime shader registration to the required family.** The four fixtures create 120
   programs but request 42 names. Keep a separate complete-startup registration test and verify late
   lookups after configuration changes. Do not remove their real lighting/lifetime chain. Moderate
   effort; measured registration time is an upper bound, not all removable work.
3. **Replace GLSL uniform checks with compiled-SPIR-V/contract validation.** Compare actual binary
   interfaces and linked active resources against authoritative contracts, including Release
   binaries without debug names. Preserve functional member-read coverage. The dedicated task in
   `LumOn.WorldProbeSurfaceLighting.todo` supersedes the original parsed-source reuse proposal.
4. **Investigate later-frame shader lifetime/reconfiguration.** Twenty graphics and 56 compute links
   occur after the first frame in the four runtime scenarios. Trace owners before deciding whether
   immutable programs can survive resource replacement. This may require production lifetime work,
   not just a test-harness edit; preserve retired tickets, leases and failure/reload coverage.
5. **Narrow the temporal UBO upload fixture**, retaining real normal/debug callbacks and mapped UBO
   assertions. The broad run assigns 18.325 seconds to two cases, but this candidate has source-based
   boundary justification rather than an exclusive constructor profile. Moderate effort.

Do not prioritize a binary-byte cache, fixture input-array pooling, event sorting or more wait-policy
changes from these measurements: their measured costs are small or the relevant exclusive cost has
not been established. Direct-visibility upload reuse remains a lower-confidence source opportunity.
No collection parallelism or whole-suite speedup is claimed.

## Validation and restoration

All 13 temporarily instrumented source files were restored byte-for-byte against recorded hashes;
both temporary profiler helpers were removed. The final uninstrumented rebuild passed in **12.161
seconds**, with zero errors and eight existing warnings. All **348 staged SPIR-V hashes** matched the
initial uninstrumented snapshot. The only lasting changes from this investigation are documentation.

Receipts: `artifacts/GpuRemainingCostsEvidence.md`, `artifacts/gpu-remaining-source-restoration.json`,
the backup/hash manifest and final restored-build receipt. The focused instrumented selections passed
141/141 and 9/9, while the broad correctness gate remains open for the two recorded failures above.

## Assertion boundaries and safe candidates

The source audit below identifies opportunities; measured prioritization follows the profiling
receipts. It does not claim that every repeated operation is expensive.

| Family | Required behavior and failure coverage | Candidate change | Coverage that must remain |
| --- | --- | --- | --- |
| `LumOnUniformTests` | Real stage linking, standalone locations, active owning blocks, source members and macro/define alternatives | Group checks for a shader pair/variant under one linked program; replace source parsing with compiled-SPIR-V/contract validation | Map existing checks to intended compiled-interface guarantees, retain named diagnostics and actual linked-interface checks; functional UBO reads remain separately tested |
| `SurfaceLightingConsumerRuntimeFixture` | Real registered renderer callbacks, engine lookup names, produced cache lighting and consumer publication | Register the required program family through production registration methods, with separate complete-startup coverage | Actual renderer wiring, late lookups after mode/configuration changes, callback teardown and failure reporting |
| `SurfaceLightingPbrLifetimeTests` and `SurfaceLightingRuntimeScenariosTests` | Retained cache/direct/screen resources, history revisions, atlas disposal, fresh restored lighting and numerical composition | Reduce unrelated startup work only | Real producer-to-world/screen-to-composition chain, source edits, invalidation, delayed/stale publication; seeded final textures cannot replace these scenarios |
| `LumOnTemporalRendererTests` | Normal/debug registered callbacks upload rebased history to the actual frame UBO | A smaller real-renderer fixture providing its required dependencies | Camera translation plus bob, previous/current matrix relationship, both callback paths and actual mapped UBO; no fabricated uploaded data |
| `DirectWorldProbeVisibilityTestBase` | Shader visibility decisions under camera/origin/clipmap/budget variation | Reuse immutable guide/atlas preparation and avoid uploads only when an explicit content/version contract permits it | Every draw, branch, normal offset, signed origin, changed atlas and immediate numerical observation |
| SPIR-V inventory/lifecycle tests | Every selected binary loads; reload installs a new generation and failed replacement preserves the old one | Improve immutable asset access only if its cost warrants it | Deliberate create/link/reload/dispose operations remain; a live-program cache would invalidate their purpose |

Source anchors:

- `VanillaGraphicsExpanded.Tests/GPU/LumOnUniformTests.cs`: `Shader_CriticalValueHasLinkedBinding`
  creates a program per row; `HasLinkedUniformOrBlockMember` obtains and parses vertex/fragment
  source on each lookup, including macro alternatives. `ShaderTestHelper.GetProcessedSource` reads
  and preprocesses source again. This is test-side declaration validation, not a remaining runtime
  GLSL dependency in `GpuProgram`.
- `VanillaGraphicsExpanded.Tests/GPU/Fixtures/RuntimeLightingPrograms.cs`: `Initialize` invokes
  `VgeShaderPrograms.RegisterAll`. Its 20 registration entry points include a debug-family expansion;
  they must not be counted as only 20 linked programs. `Loaded` records requested names separately.
- `VanillaGraphicsExpanded.Tests/GPU/Fixtures/DirectWorldProbeVisibilityTestBase.cs` already caches
  variants per test and uses size-aware resources; debug draws skip screen allocations. Its input
  arrays are mutable, so reference equality alone cannot authorize skipping an upload.
- `VanillaGraphicsExpanded.Tests/GPU/Fixtures/RuntimeRenderEvents.cs`: stage filtering, ordering,
  `ToArray` and callback dispatch are distinct from work inside `OnRenderFrame`. Measuring the entire
  dispatch loop does not establish that sorting or interface adapters dominate.
- `VanillaGraphicsExpanded.Tests/GPU/Fixtures/TestUniformRing.cs`: a single mapped page relies on
  current readbacks for synchronization. The separate retirement task precedes removing observations.

## Constraints on reuse and concurrency

Any immutable binary-byte cache must retain per-fixture `Overrides`, missing/corrupt-asset scenarios,
`BeforeRead` notifications and read accounting. It must invalidate when asset contents change;
path-only process-wide caching could hide reload regressions. Parsed source snapshots need keys
covering source/include content and shader options, and mutable AST objects cannot be shared freely.

Grouping uniform checks changes xUnit case granularity. A follow-up must either preserve individual
rows against an immutable linked-interface snapshot or evaluate all rows and aggregate named failures;
stopping at the first failed assertion would hide the remaining checks in that group. Record asserted
row counts independently of the number of xUnit cases before comparing performance.

Live programs and render targets remain owned by their GL context. Engine platform state, shader
imports, PBR material state, samplers and the uniform ring are shared boundaries. No additional
collection parallelism is proposed without explicit isolation and retirement contracts.

The existing follow-up tasks for required versus diagnostic observations, uniform-buffer retirement,
and duplicate GPU observations remain separate. This investigation does not authorize deleting
finite/nonempty checks hidden in diagnostic expressions or reducing temporal/freshness workloads.
Moving lazy compilation from the first frame into construction or warmup merely relocates its cost;
any speedup claim must include complete fixture setup and teardown around the same asserted work.
