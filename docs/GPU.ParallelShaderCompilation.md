# Production parallel shader linking

Production graphics registration, grouped configuration recompiles,
world-probe compute setup, and the feedback mark/compact pair now submit independent programs
through a render-thread `ShaderLinkBatch`. The default window is eight candidates. The public
loading APIs remain synchronous: callers receive only completed, contract-validated programs.
The LumOn debug family declares its members at startup and links each only when selected.

## Submission and publication

The batch captures immutable shader settings and uses the existing binary reader and shared
digest index. A driver executable cache hit keeps its existing synchronous validation. A miss
or rejected entry specializes stages without querying compilation status, then submits linking.
Both ordinary `TryCreate` paths and pending candidates use `ShaderProgramLink` for driver
submission and final link status.

`GL_ARB_parallel_shader_compile` is preferred; `GL_KHR_parallel_shader_compile` is also
accepted. Without either extension, ordinary loading remains in use. The code leaves the
context's compiler-thread hint unchanged. It introduces no worker-thread GL calls.

At a consumer dependency, the batch polls only the extension completion property. Once ready,
it checks stage and link status, preserving driver diagnostics, before the existing owner prepares
interfaces and publishes the executable. A cancelled wait or a 60-second completion deadline
leaves the previous installed executable in place. The deadline bounds completion polling; it
cannot interrupt a driver call that itself blocks.

Completed graphics stages transfer to the engine-compatible owning slots; compute stages detach
and are deleted after linking. Disposal deletes abandoned candidates. Contract/interface setup,
binary extraction and callbacks happen after completion. A newer settings snapshot prevents
publication of a superseded graphics candidate.

Refill occurs after the requested candidate completes. An unexpected dependency, a request outside
the submitted window, or a nested batch first establishes completion of outstanding work before
submitting more. Completed candidates can remain available to their planned consumers, but they
no longer contribute unfinished driver work. Cancellation applies equally to fallback contexts.

## Production boundaries

- `VgeShaderPrograms` batches the ordinary graphics owners and publishes successful programs.
- `LumOnDebugShaderProgramFamily` retains settings declarations for all family variants. Selection
  prepares only the requested executable, using the existing loader and executable cache.
- `ShaderRecompileQueue` coalesces changed owners by API and asset domain, captures their final
  settings on the render thread, and leaves changes raised during publication for another callback.
- `WorldProbeTraceBatch` groups the three compute programs required by its constructor.
- Feedback mark/compact creation is grouped only when both programs are missing, adding no batch
  work to the usual frame path.

Engine IL inspection confirmed that a full `ShaderRegistry.ReloadShaders` replaces shader assets,
disposes and clears all existing registered programs, reloads built-in programs, and only later
invokes the mod callback. The existing queued mod registration is therefore the safe batch boundary.
Preserving an old executable applies to mod-owned replacements and configuration updates; the
engine's full reload already destroys the old generation. No extra engine reflection hook is added.

## Debug programs loaded on demand

Startup now declares all 11 debug-family members without reading their SPIR-V assets, linking
executables or registering them with the engine. Ordinary production registration prepares 19
programs instead of 30. Family lookup returns a settings owner; the renderer applies current
composite, visibility and world-probe settings before requesting a completed executable.

The renderer selects its category program first. If preparation fails, it prepares the legacy
dispatcher through the same family, with the same current settings. Neither lookup asks the engine
to load a missing name as GLSL. Repeated selections reuse the installed executable. Configuration
changes update declarations, including previously selected inactive programs, without linking them.

Failed replacements preserve the installed owner but do not draw it with incompatible settings.
The same failed inputs are suppressed on later frames; changed effective inputs or asset reload
allow another attempt. Reload retains pending settings and defers recreation until selection.
Selection after engine teardown also restores engine registration, even before the queued family
reload callback. Application disposal releases both linked members and never-used declarations.

Existing executable caching remains in use. Cold linking work for a debug program moves to its
first selection; it is avoided entirely only if that program is never requested. This change does
not claim faster individual links or increased steady-state FPS. Preparing initial settings for
non-debug rendering remains a separate task.

Focused Release validation passed 28/28 checks, including actual renderer fallback draws,
pending/inactive settings, reload, engine teardown before queued registration, failed replacement
retention, disposal, production registration and existing batching behavior. Evidence:
`artifacts/LazyDebug/lazy-debug-release-complete.trx`. A separate implementation review passed
after correcting registration restoration when selection follows engine teardown.
Debug validation passed 27/27 focused checks using isolated `bin/LazyDebug` output;
evidence: `artifacts/LazyDebug/lazy-debug-debug.trx`. The final expanded Release startup
measurement separately passed 1/1. No game process was launched or stopped.

An opt-in Release measurement used one empty private executable cache followed by one warm-cache
generation. Both direct and legacy programs rendered radiance 1 as RGB 0.5; switching back required
zero asset reads. Evidence: `artifacts/LazyDebug/lazy-debug-startup-profile.trx`.

| Operation | Empty executable cache, ms | Warm executable cache, ms |
| --- | ---: | ---: |
| Production startup, 19 programs | 991.375 | 23.400 |
| First direct-program selection | 14.716 | 0.973 |
| Direct draw and readback | 16.420 | 5.023 |
| First legacy-dispatcher selection | 917.630 | 6.852 |
| Legacy draw and readback | 1.258 | 311.406 |
| Switch back to direct | 0.006 | 0.005 |
| Repeated direct draw and readback | 0.302 | 0.169 |

The warm legacy draw demonstrates that executable loading can defer substantial driver work until
first use. These are single-process observations, not a matched before/after speedup estimate;
driver-internal caches were not cleared. The earlier 30-program figures below are historical.
Other debug modes and in-game stalls remain unmeasured. Run the opt-in measurement with
`VGE_DEBUG_DEMAND_PROFILE=1`; ordinary correctness runs do not repeat this workload.

## Validation and measurement

Before demand loading, final Release validation passed 28/28 focused checks. Coverage includes graphics draws and compute
dispatch/readback, bounded refill, unexpected and nested dependencies, supported and forced
synchronous loading, cancellation, cache hits/rejected entries, superseded settings, 30-program
registration, 15-owner configuration coalescing, and failed debug-family replacement retention.
Evidence: `artifacts/ParallelLink/parallel-link-final-verified.trx`.

Final Debug validation also passed 28/28, using isolated
`artifacts/ParallelLink/DebugOutput/` because another process locked the normal mod DLL output.
No process was stopped. Evidence: `artifacts/ParallelLink/parallel-link-debug-final.trx` and
`artifacts/parallel-link-debug-isolated.log`. The ordinary output failure was MSB3027/MSB3021,
not a compilation or test failure.

The separate implementation review passed after correcting out-of-window bounds, cancellation on
fallback drivers, duplicated linking operations, and debug-family failure reporting. The subsequent
family retention fix keeps a working member in both its lookup map and the registry when a new
candidate fails. No game process was launched. The KHR-only hardware path and a genuinely
unsupported driver were not available; fallback policy is exercised explicitly in tests.

The matched Release production workload registered 30 programs, changed 15 owners through one
queued callback, and re-registered 30 programs. The executable cache was disabled for both policies.
One ABBA block, in milliseconds:

| Policy | Registration | Configuration | Re-registration | HZB draw/readback after registration |
| --- | ---: | ---: | ---: | ---: |
| Synchronous A1 | 2424.857 | 1535.393 | 1927.169 | 5.035 |
| Batched B1 | 2042.134 | 1777.992 | 2052.505 | 0.419 |
| Batched B2 | 2015.439 | 1532.430 | 2022.588 | 0.334 |
| Synchronous A2 | 1947.808 | 1823.410 | 2023.396 | 0.296 |

For B1/B2 registration, submission took 2036.009/2011.902 ms and completion observation took
0.430/0.063 ms. Submission includes asset reading, input preparation, specialization and link calls;
it is not pure driver CPU time. Both batches consumed all 30 candidates with a peak window of eight.
Configuration and re-registration completion observations were also below one millisecond.
Synchronous scopes have no separate batch counters, so their zero counters do not indicate zero work.

Results are mixed and **do not establish a reliable speedup**. Earlier samples differed, and the
first-use outlier illustrates warm-up/order effects. Driver-internal caches were not cleared. HZB
copy with a numeric pixel assertion measures one production shader's draw/readback after registration
and re-registration; its observation after configuration is a repeated use, not first use of the
15 changed programs. Other shaders' first-use costs and in-game frame stalls remain unmeasured.
Re-registration exercises the real mod entry point, not the engine's entire reload operation.
Evidence: `artifacts/ParallelLink/parallel-link-production-firstuse.trx`.

Routine production checks run one batched correctness arm. Set `VGE_SHADER_LINK_PROFILE=1` to
opt into ABBA profiling, avoiding four full registration workloads in every ordinary test run.

## Cause analysis: driver calls and eager debug registration

A focused Release profile split the previously broad submission measurement. On the local NVIDIA
RTX 4090, driver 591.86, GL 4.3 context, the corrected 30-program registration measured:

| Operation | Calls | Milliseconds |
| --- | ---: | ---: |
| Total registration | 1 | 2329.488 |
| Batch submission, inclusive | 30 programs | 2030.268 |
| `GL.LinkProgram` | 30 | 1906.869 |
| `GL.SpecializeShader` | 60 | 98.985 |
| Stage asset capture and copying | 30 programs | 13.097 |
| `GL.ShaderBinary` | 60 | 0.224 |
| Completion polling and validation | 30 programs | 0.734 |

Link calls consume approximately 94% of submission time. They occupy the calling thread before
the next program can be submitted; postponing status queries cannot overlap that portion of the
work. This identifies the API boundary where time is spent, not the driver's internal optimizer
or whether it uses worker threads internally. The compiler-thread hint was already `UINT_MAX`,
which requests the implementation-specific maximum; querying it does not reveal actual worker count.

The 11 LumOn debug programs consumed 1411.750 ms, approximately 74% of all link time. The legacy
`lumon_debug` dispatcher alone took 1064.334 ms (56%). Other large links were
`lumon_probe_atlas_gather` (110.728 ms), `lumon_debug_worldprobe` (105.612 ms),
`lumon_upsample` (89.436 ms), and `lumon_debug_gbuffer` (87.728 ms).
At the time of this profile, `LumOnDebugShaderProgramFamily.Register` linked every member eagerly. The renderer normally selects
a category-specific program and uses the legacy dispatcher as a fallback. Its cold linking cost
is therefore paid even when debug rendering is disabled or the dispatcher is never selected.

Source inspection also confirms avoidable intermediate settings in some startup paths:
`DirectVisibility` defaults to false, while both gather passes request true before drawing.
`GpuProgram.UpdateSettings` consequently schedules a replacement when those effective inputs change.
Preparing the required initial settings before the first link would avoid that intermediate
generation. The number and timing of such replacements in a live game were not measured here.

Priorities supported by this evidence are to avoid eagerly linking unused debug programs, especially
the legacy dispatcher, and to prepare initial runtime settings before linking. Executable-cache hits
can avoid this linking path; these particular timing comparisons deliberately disabled that cache.
Increasing the batch window, pooling small bookkeeping allocations, or optimizing the 13 ms of
asset capture would not address the dominant measured cost.

Evidence: `artifacts/ParallelLink/link-cost-profile-batch.trx` (passed). The initial
`link-cost-profile.trx` has valid synchronous startup counters but contaminated batch counters
that included preceding configuration/re-registration work; do not compare those totals.
Temporary profiling instrumentation was removed after measurement.

A separate real 30-program cache check confirmed the practical distinction: an empty private
executable cache took **2422.839 ms** (zero cached owners); the next registration took
**39.601 ms**, with **30 verified cached owners**, valid GL program handles and no GL errors.
The stored executable payload totaled 802,436 bytes. This was one cold-then-warm sequence within
one process, not a cross-process or all-program first-draw benchmark. It demonstrates that the
existing executable cache bypasses the expensive linking work for unchanged inputs. Evidence:
`artifacts/ParallelLink/link-cache-profile.trx` (passed). The temporary test was removed.

The extension permits overlapping driver work but does not guarantee a speedup on a given driver.
Submission, dependency completion, interface preparation, cache I/O and first use are distinct costs;
lower startup wall time would not imply cheaper individual links or higher steady-state FPS.

References: [ARB parallel compilation](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_parallel_shader_compile.txt),
[ARB SPIR-V interaction](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_gl_spirv.txt).
