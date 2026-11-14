# Virtual shadow map qualification protocol

This protocol accompanies the [implementation contracts](Rendering.VirtualShadowMaps.Contracts.md) and
[proposal](Rendering.VirtualShadowMaps.Proposal.md#qualification-gates-and-review-status).
It defines repeatable acceptance evidence for the [implementation plan](Rendering.VirtualShadowMaps.todo).
VSM scenes, counters, fault injection and runtime capture described here are requirements to implement;
they are not existing measurements. Surface qualification precedes full volumetric fog qualification.

## Evidence boundaries and tool routes

Run all build/test tools through delegated agents. Preserve complete command, working directory,
environment overrides, source revision/dirty diff, start/end timestamps, exit code, selected tests,
skips and stdout/stderr with each receipt. Do not infer live rendering quality or performance from
source inspection, CPU fixtures, synthetic depth or headless GPU checks.

| Boundary | Canonical route from repository root | Required receipt |
| --- | --- | --- |
| Mod and shader build | `dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Debug --tl:off -v:minimal` (`Build (dotnet)` task) | Build log, warnings/errors; default shader producer enabled; `artifacts/spirv/Debug/build-receipt.json` and output digest index |
| Focused existing tests | `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj --filter <explicit expression>` | TRX plus exact selected test names/counts and category boundary; focused checks do not imply a full regression pass |
| All correctness, including GPU | `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj --settings VanillaGraphicsExpanded.Tests/AllTests.runsettings` (`Test (All Correctness)`) | TRX and real GL vendor/renderer/version/limits; Diagnostic excluded; skipped GPU tests do not establish GPU coverage |
| Shader build contract fixtures | `ShaderBuildTool/Tests/ValidateBuildContract.ps1 -Configuration Debug` | Maintained fixture integration results; not a substitute for the shader-enabled mod build |
| Strict shader content verification | Mod build with `-p:SpirvVerifyContents=true` | Content-rehashed success receipt and `spirv-digests.json`; exact source/compiler/tool/config identities |
| Package | `dotnet run --project CakeBuild/CakeBuild.csproj` (`Package`, default Release) | Cake JSON validation/clean/publish/shader logs and ZIP identity; never substitute a Debug DLL for the packaged Release output |
| Package identity verification | Inspect resulting ZIP against published DLLs, shader catalog and generated runtime digest index | Exact member set, sizes and SHA-256; verify catalog/interface/assets agree, no obsolete shaders/private cache entries; a successful ZIP operation alone is insufficient |

The repository's `.agents.md` calls these build/test routes generically `dotnet: build/test`;
actual task labels are in [.vscode/tasks.json](../.vscode/tasks.json). Default tests exclude
`GpuIntegration` and `Diagnostic`; use the explicit all-correctness settings when GPU coverage is needed.
See [incremental shader builds](ShaderBuild.Incremental.md) for tool pinning, cache and strict-verification
semantics. Reuse matching successful receipts; do not turn every documentation change into a package run.
No single standalone package identity verifier is assumed: preserve the concrete inventory/hash procedure
and its results with the eventual package receipt.

Retain VSM evidence under `artifacts/virtual-shadow-maps/`, with unique capture IDs. Preserve the existing
chunk receipts and native-boundary receipt linked from the contracts. Build outputs under `artifacts/spirv`
are mutable: acceptance captures must retain copies or content identities, not depend on a later overwritten
file. Keep required evidence available when handing off a checkout, including ignored artifact files.

## Hardware and acceptance profiles

The available development reference on 2026-11-09 is Windows, Intel Core i9-12900K (16 cores, 24 threads),
NVIDIA GeForce RTX 4090, Windows driver 32.0.16.1088, approximately 63.7 GiB physical system memory.
This inventory is not a live OpenGL capability receipt or a minimum supported hardware claim. Query actual
GL limits from the rendering context for every GPU run; do not interpret WMI's truncated AdapterRAM as VRAM.
Record OS build, power policy, display/resolution, GPU clocks/temperature when available and competing load.

Use these named capture configurations:

| Profile | Configuration and purpose |
| --- | --- |
| Reference-Baseline | Available reference GPU, 1920x1080, fixed render scale 1, VSync/frame cap disabled for timing, Balanced requested settings, default 256 MiB / 2 ms / eight shadowed locals; optional acceleration paths disabled |
| Reference-HighDemand | Same scene/config identity at 2560x1440 and higher light/receiver demand; explicit overload and recovery, not a substitute for baseline |
| Capacity-Limited | Same context with deliberately restricted tiles/array layers/sampler slots/storage/operation limits plus low-memory and zero-local settings; verifies effective capacity/failure rules, not slower-hardware performance |
| Baseline-Context | Reference GPU with an actual OpenGL 4.5 context, the established GL_ARB_gl_spirv ingestion support, and optional VSM acceleration disabled. Repeat correctness through packaged production SPIR-V with captured context/extensions and limits. A newer context or GLSL-only fixture does not prove this minimum-context production route; capability masking establishes branch independence only. |

Capability masking on a newer GPU validates branch independence, not another GPU's speed or full driver
compatibility. Additional supported device/driver targets and hardware-specific acceptance limits are
agreed with the user during runtime qualification; this protocol does not presume access to another GPU.
State the actual tested hardware coverage and do not generalize its performance to untested devices.

Before comparing captures, freeze a profile containing scene/save hash, script/event sequence, camera
path, field of view, engine/mod/dependency identities, shader hashes, resolution, terrain view distance,
displacement/tessellation/material settings, light counts/intensities and all effective shadow limits.
Record the actual rendered/loaded chunk envelope per sample. The VSM budget target is not a measured limit.
Agree CPU/GPU percentile limits, minimum per-source ready duty cycle, maximum withdrawal streak and acceptable
detail with the user after pilot measurements and before final pass/fail runs. Keep failed runs; do not
lower detail or exclude unavailable frames silently to meet the target. Required coverage/invalidation
correctness has zero tolerated missing occluders in current certificates or stale samples presented as
current, independent of timing goals. Explicit temporal reuse is measured separately against its age,
motion and receiving-coverage limits.

## Repeatable combined workloads

Create a versioned fixed save/fixture and deterministic event/camera manifest. Record native world/dimension
and seed; a seed alone is insufficient after edits. Use fixed simulation time steps for correctness replay
and recorded event timestamps for performance replay. Restore the identical initial snapshot before each run.
The scene contains a low-angle ridge casting offscreen shadows, a sealed cavity and opening corridor,
cutout foliage, displaced terrain, water over opaque geometry, multiple transparent layers, animated
entities and a moving chunk. Placement is specified in integer block/chunk coordinates in the manifest.

Use this initial deterministic fixture recipe. Coordinates are block offsets from a recorded ground
origin on a native chunk boundary in dimension zero, with room for the complete construction inside the
world's vertical bounds. Retain the chosen absolute origin and actual loaded envelope. Resolve material/
entity archetypes to actual game IDs and record those IDs with the save; do not substitute different
opacity or displacement behavior between repetitions. Building the executable fixture/save belongs to
runtime qualification; this recipe fixes its intended geometry and event schedule now.
Axes use Y up; azimuth zero points toward +X and increases toward +Z. The direction toward a celestial
source is `(cos(elevation) cos(azimuth), sin(elevation), cos(elevation) sin(azimuth))`, matching the ridge
on the +X side. A separate horizon-crossing replay sweeps elevation from -3° to +3° for the boundary cases;
it does not replace the combined above-horizon sun/moon workload.

| Fixture component | Reproducible placement or motion |
| --- | --- |
| Terrain and offscreen ridge | Ground plane over x/z [-128,128]; ridge x [96,103], z [-64,64], height 48 blocks; no caster horizon excluding it |
| Cavity and corridor | Closed shell x [40,55], y [0,15], z [-8,8], one-block walls; passage at x=40, y [1,3], z [-1,1], initially filled |
| Receiver materials | Displaced ground x [-24,-9], z [-8,8]; cutout foliage x [-8,-1], z [-8,8]; water x [0,15], z [-8,8] at y=1 over opaque ground; transparent sheets at x=20 and x=24, y [1,8], z [-8,8] |
| Camera and held light | For normalized cycle t=(frame mod 1800)/1800, camera position (40 sin(2πt),5,-24+12 cos(2πt)), looking toward (16,3,0); held attachment follows a recorded one-second animation loop |
| Moving locals | Six emitters with phase offsets i/6 on circles: (16+12 cos(2π(t+i/6)),4,12 sin(2π(t+i/6))); one additional stationary source at (48,4,0) isolates scene edits. With the held source this gives eight locals before overload. Record actual payloads/radii, ensure visible contribution |
| Celestial motion | Sun azimuth 0° to 6°, elevation 0.1° to 6.1° over each 1800-frame cycle; moon azimuth 180° to 186°, elevation 6.1° to 0.1°. Record independent nonzero direct payloads for the harness and production-calendar observations separately |
| Caster motion | Animated entity centered at (16,2,0), one-second pose loop; moving chunk follows x=32+8 sin(2πt), y=8, z=16 with recorded transform/bounds |

Within each 1,800-frame measured cycle: open the passage at frame 300, close at 600, add a caster at
(32,4,0) at 750 and remove at 900, change a material's shadow eligibility at 1050 and restore at 1200.
At 1200 also introduce four more contributing local emitters; at 1350 halve memory and work allowances;
at 1500 restore settings and remove the extra emitters. Maintain an unmodified control replay with the
same light/camera motion for attribution. Streaming replay translates the complete camera path by one
chunk every 120 frames across a saved loaded boundary; record actual arrivals and pending intervals.
Origin/dimension and context-reset cases use explicit checkpoints at frames 600/1200 in separate replays
so their discontinuities do not silently contaminate steady-state statistics. Log actual application frame
for every asynchronous edit/upload; compare runs only when the event/availability histories are compatible.

Every timed sequence keeps both celestial directions moving and at least one carried plus several moving
point sources contributing. Include a deterministic harness setting where sun and moon both contribute
even if a particular game calendar setting would make one negligible. Preserve calibration in production.

| Sequence | Fixed events and required observations |
| --- | --- |
| Cold fill | First 300 frames from cleared shadow caches, no discarded warm-up frames in this segment; reveal offscreen occluders and new receiver depths sharing pages |
| Sustained motion | At least 1,800 frames: camera crosses chunk boundaries; both celestial directions rotate through low elevations and candidate-coverage boundaries; held source moves with animated hand and moving sources cross their 2 m coverage boxes repeatedly |
| Edits and moving casters | At fixed manifest frames open/close the corridor, fill an empty chunk, introduce/remove an offscreen caster, deform/animate geometry without range/transform replacement and change cutout eligibility; leave one light stationary to isolate scene invalidation |
| Streaming and origins | Follow a recorded route through load/unload boundaries, change dimension, move to negative/large coordinates and shift render origin; retain interior missing/pending cells as unresolved |
| Pressure and recovery | Increase eligible local sources above eight; shrink memory/work limits, exhaust individual storage/operation limits, then restore; record whole-light withdrawal, minimum detail and consecutive recovery history |
| Reconfiguration | Disable/re-enable, change page-size policy where supported, reload shaders and recreate context/resources; publish no stale result and preserve in-flight/native ownership |

Run at least three repetitions per timed configuration. Separate cold fill from steady-state after a fixed
300-frame warm-up; report every transition window rather than pooling it away. Retain at least 1,800 measured
frames per sustained sequence/repetition. Faster hardware does not shorten the event list. Timestamp events
and report both frames and seconds so a frame-driven stress schedule can be distinguished from real-time motion.
Stationary cache-hit scenes are supplemental. Combine motion, edits and streaming in a final mixed run;
isolated tests identify causes but cannot substitute for the combined moving-light workload.

## Measurement boundaries

Use monotonic CPU timings and nonblocking GPU timestamp/query results tagged with original frame/work IDs.
Collect delayed results when available; never wait in the render loop for a current query. Record missing
query samples, timeout/disjoint/context-reset conditions and capture overhead. A missing result is not zero.
Keep raw per-frame values, per-run p50/p95/p99 and max, plus variation across repetitions. Compute nearest-rank
percentiles on sorted valid samples and report sample counts. Do not sum stage percentiles into a total.

| Interval/counter | Required attribution |
| --- | --- |
| Demand/preparation | Extra surface demand draws, entity preparation and source capture work attributable to shadows |
| Publication | World/geometry/change capture, freeze and upload; CPU lock/wait and bytes, GPU consumers separately |
| Associations | Changed-region invalidation, traversal, continuation, dedup and replacement publication; cells/slabs/false positives and cache age |
| Expansion/submission | Candidate visits, intersections, counting/emission repeats, temporary pairs, total groups/slots including zero commands, CPU submit cost |
| Clear/depth | Selective clear separately, actual caster depth draws, page instances and post-tessellation/geometry-stage amplification |
| Sampling | Consumer GPU cost measured with paired same-scene lighting runs and/or scoped passes; distinguish full pass time from attributed shadow delta |
| Total | Full frame CPU/GPU and the union of shadow intervals; no double-counting nested stages; disclose when attribution cannot isolate shared work |
| Memory | Live depth/metadata, associations, commands, scratch, building and retired generations; peak and effective capacity; driver overhead reported separately if measurable |
| Availability | Per contributing source/consumer: ready frames / eligible demand frames, selected detail, withdrawal streak, recovery latency, failure reasons and unexpected sample misses |

Report denominators for **all contributing eligible sources**, including sources rejected by the scheduler;
also report admitted-only duty cycle separately. Otherwise continually rejecting an expensive light could
produce a misleading 100% readiness metric. Record sun, moon and player's carried light individually.
Record coarse/detail reduction frequency, candidate output versus reference required cells and invalidation
backlog. A cheap frame with routinely missing shadows cannot pass performance acceptance.

## Correctness and fault matrix

The CPU exhaustive reference belongs to test tooling, not production selection. Enumerate every chunk box
in small bounded domains and determine conservative swept-volume intersection independently of the GPU slab
algorithm. Use exact/rational or outward interval arithmetic where feasible; ambiguous boundary cases remain
included. Do not compare two copies of the same slab implementation and call the result an independent oracle.
CPU reference uncertainty may increase false positives; no reference-required cell may be missing on GPU.

| Family | Required cases and evidence |
| --- | --- |
| Directional traversal | All six signed sectors and overlaps; full cap interior, negative coordinates, zero components, exact planes/edges/corners, near-horizontal directions, initial/final slabs, receiver/domain boundary contact; no elevation-based horizon |
| Point traversal | Source inside receiver, overlapping/coincident boxes, motion across faces/corners, finite segments versus infinite lines, changing influence, negative coordinates and exact boundary ties |
| Bounded continuation | Pause within a broad slab, between sectors and during dedup; resume every cursor with changed projection/domain/pruning generations; force all capacities/probe limits to reject incomplete results |
| Classification | Fresh/stale/absent Empty/connectivity, occlusion setting disabled, sealed cavity/pillar/bent passage, source inside chunk, empty-to-filled and passage opening with unchanged light; proof of opacity agreement or bounds-only behavior |
| Coverage/cache | New receiver at another depth in the same page, enlargement to enclosure, shared surface/fog page, new filter/bias footprints, motion within candidate coverage invalidating depths, expired candidate interval |
| Scene/native lifetime | Load/edit/upload/removal/dimension reset races, stale/reused native range, failed upload, offscreen entity and moving chunk, material/pose changes without transform changes, lost/deferred change publication |
| Raster/sampling | Ordinary/SSBO terrain, tessellation displacement, cutouts/foliage, object instancing, source-near crossings, deep directional span, cube/clipmap/page seams, unchanged protected tiles after selective clear, multiple array layers, contended nearest-depth atomics, all filter taps |
| Temporal | Current/permitted-stale/unavailable, strict zero reuse, repeated edits without expiry reset, age and motion boundaries, separate current light and rendered projection, new receiver coverage, insufficient replacement space, hard source/world/resource withdrawal |
| Virtual views | Packaged SPIR-V uses compact object/view inputs and texture-buffer projection records; final-deformation clipping, invalid selectors, region boundaries and limits, material discard before atomics, RHI raster target and charged in-flight bytes |
| Readiness | Demand overflow, hidden versus contributing transparent layers, early/late consumers, independent sun/moon failures, unsupported consumer bindings, immediate withdrawal, sustained recovery without stale fades |
| Lifecycle | Explicit capacity recreation, disable, source/world removal, context and shader replacement, in-flight retirement, aggregate limits over multiple layers/groups; unknown inputs never equal an empty complete set |

Run real GPU variants with draw parameters, indirect counts, direct vertex/TES layer output and bindless
sampling disabled. Preserve the established SPIR-V ingestion capability and load packaged production artifacts.
Test reduced hardware limits without altering actual GL limits illegally: restrict the effective policy
and verify rejection, then separately run actual boundary-sized resources under reported device limits.
Test shader metadata transport and selective clear with sentinel tiles. Engine lifecycle integration
must exercise the actual native adapters; synthetic publications alone do not establish their safety.

## User-run visual and fog acceptance

The user runs the game; automation must not launch it for visual acceptance. Supply the versioned scene,
settings, observation sequence and matching package identity. Capture screenshots/video and observations
for simultaneous solar/lunar contributions, held/moving lights, cutouts/foliage/animated entities/water,
transparent layers, offscreen occluders, near-source/contact bias, low angles, streaming edges, page/cube/
clipmap seams, detail changes and whole-light withdrawal/recovery. Record approval or unresolved observations
per scenario. A headless image match cannot stand in for this acceptance.

After surface acceptance, repeat the combined sequence with volume sampling and fog enabled. Record separate
fog demand/readiness, filter footprints, projection/page/readiness history rejection and GPU cost. Test a fog
receiver at a different depth sharing a surface page and force replacement exhaustion: preserve complete
surface results and defer fog expansion. Fog-only demand cannot evict required surface pages or reduce their
readiness. Compare against matching accepted surface captures and obtain separate fog visual acceptance.

The implementation plan retains current baseline results and unresolved verification ownership. Passing
the build routes above establishes only those exercised boundaries; no measured VSM claim exists yet.

## Controlled frame-gate correctness receipts

The 2026-11-12 captures below qualify controlled frame preparation, current surface demand, bounded
arbitration and GPU whole-light readiness. They preceded production surface integration and do not
establish live appearance or moving-light frame-time performance.

- [Strict build and GPU regression capture](../artifacts/virtual-shadow-maps/frame-gate/20261112-030157-clamp-correction/):
  Debug content verification passed with 20 warnings, none on changed production paths. All 25 affected
  checks passed, including the six native source/gate cases, directional near-depth clamping and view-mode
  packing. The actual context was OpenGL 4.5 on NVIDIA RTX 4090 with SPIR-V ingestion and optional
  acceleration masked. The full correctness run recorded 6,219 passes, three shader-inventory snapshot
  failures and 11 unrelated skips; all 247 shadow tests passed without skips.
- [Inventory corrections and tooling capture](../artifacts/virtual-shadow-maps/frame-gate/20261112-032012-catalog-corrections/):
  six targeted catalog, declaration and binding checks passed, resolving all three full-run failures.
  Shader-build contract fixtures passed 156/156 and generator tests passed 158/158, without skips.
  Correctness evidence therefore combines the retained full run with these targeted corrections;
  it is not a fresh single all-green full-suite invocation. The retained skip classification identifies
  six atmosphere checks, three tokenizer/source-map reproductions and two probe-tracing cases.
- [Release package identity](../artifacts/virtual-shadow-maps/frame-gate/20261112-032338-package/package-identity.json):
  canonical Cake validation, clean, publish, shader production and ZIP creation passed. Exact member,
  size and SHA-256 comparison verified 1,188 ZIP files, 795 SPIR-V binaries and 795 associated interfaces,
  including producer/runtime/archive binary-set equality, digest-index coverage, owned source provenance
  and compiler/tool identities. The ZIP SHA-256 is
  `432DF45EF1870B7E8894C51E329C1ADE149DEC967CAD463E6A15FE986EE1FC17`.
  An initial verifier misclassified hashed variant filenames as shader-stage suffixes; the retained
  corrected verifier derives stages from the source paths, including `.tcsh` and `.tesh`, and reports
  zero issues without rebuilding or changing package bytes.

Each capture retains commands, timestamps, environment overrides, complete logs, source snapshots/hashes
and applicable TRX, catalog, interface and digest evidence. For these frame-gate captures, production
hashes match across the strict build, full regression run and Release package. Required handoff evidence includes these ignored artifact
directories and the retained ZIP, rather than only the mutable build-output directories.

## Translated surface-coordinate correctness receipts

The [composed result ledger](../artifacts/virtual-shadow-maps/surface-consumers/20261113T035812Z-translated-final/composed-test-results.json)
recorded 440 passing distinct cases and four unresolved native forward attenuation fixtures at that
checkpoint. The four fixtures attempted to submit installed native programs through the owned RHI
shader path, which rejected their standalone numeric uniforms before drawing. Native family
compilation, linking, interface and binding checks passed. The later native-owner executions below
supersede that checkpoint's draw-coverage gap.

- [Retained focused and direct/liquid executions](../artifacts/virtual-shadow-maps/surface-consumers/20261113T031653Z-translated-final/verification-summary.json)
  supply the still-applicable passing results from the original selections. These runs also retain
  the original failures; they are not presented as all-green executions.
- [Affected numerical and liquid regressions](../artifacts/virtual-shadow-maps/surface-consumers/20261113T034533Z-translated-final/affected.trx)
  passed all 19 owned forward numerical cases and both liquid rendering cases after camera/source
  fixture reconciliation. The independent radiance oracle and appearance assertions were preserved.
- [Final native source-to-consumer execution](../artifacts/virtual-shadow-maps/surface-consumers/20261113T035812Z-translated-final/final-two.trx)
  passed both ordinary and carried-source cases without skips. These exercise the native frame gate,
  simultaneous celestial and local visibility, independent source fallback, retained moving-light
  mappings, emission preservation and the production liquid boundary source.
- [Final build and identity receipt](../artifacts/virtual-shadow-maps/surface-consumers/20261113T035812Z-translated-final/verification-summary.json)
  records strict Debug content verification: 249 warnings, zero errors, 795 production and 962 test
  shader variants current. Captures retain commands, source and binary identities, shader receipts,
  digest indices and TRX results; completed executions recorded no source drift.

The domain regressions include large positive/negative origins, integer page limits, cube seams,
camera/chunk rebasing with retained depth, original carried-light position capture, tight distant
receiving bounds and high/low origin cancellation at an FP32 rounding boundary. GPU image assertions
use reductions; compact status, addressing and numerical observations remain separate from bulk images.

This is composed evidence across receipts, not a fresh full-suite or single-driver pass. Earlier full
selections reported NVIDIA 610.88; the final test process reports RTX 4090, OpenGL 4.5.0 NVIDIA 621.24,
GLSL 4.50, 96 SSBO bindings and 16 compute SSBO blocks. Intermediate metadata captured in a separate
context is labelled as such. A context-unavailable attempt with 114 skipped GPU cases is retained
separately and supplies no GPU qualification. Those captures established no new Release package,
user-run visual acceptance or frame-time/performance qualification.

## Relative compute and native surface correctness receipts

The [2026-11-14 UTC capture](../artifacts/virtual-shadow-maps/surface-consumers/20261114T000431Z-phase10-final/verification-summary.json)
records strict Debug builds, focused GPU correctness and a canonical Release package. Its
[composed result ledger](../artifacts/virtual-shadow-maps/surface-consumers/20261114T000431Z-phase10-final/composed-test-results.json)
contains 428 distinct passes, zero remaining failures and zero skips, including all 267 shadow cases.
This is a composition of passing executions, not a single full-suite run: the original broad run had
397 passes and 31 failures; a corrected-group run passed all 50 cases, overlapping 19 earlier passes.

- Coverage containment, accumulated demand, candidate traversal, selection and raster addressing use
  integer chunk/page identities with bounded relative FP32 arithmetic. The GPU regressions exercise
  large signed coordinates, conservative boundary enclosure, point-source candidate completeness,
  retained mappings and exact integer-clock expiry/rollback behavior. All 34 production shadow SPIR-V
  modules, including variants, lack the Float64 capability in both Debug and Release outputs.
- The four native-forward attenuation cases execute installed patched native programs through their
  native shader and mesh owners inside an admitted RHI boundary. Lit, occluded and zero-solar draws
  isolate the source's direct contribution while proving independent illumination remains. Native
  frame-gate, ordinary/carried-source and early receiver-capture regressions also pass.
- Strict Debug mod verification reported zero warnings/errors and 795 current production shader
  variants/interfaces. Test verification reported 249 warnings, zero errors and 962 fixture variants.
  Test corrections preserved behavioral oracles, removed an obsolete feature-specific UBO byte check
  and restored incoming sampler state rather than assuming a global default.
- The canonical Cake Release route passed clean publish, shader production and packaging. Its
  [package identity receipt](../artifacts/virtual-shadow-maps/surface-consumers/20261114T000431Z-phase10-final/package-identity.json)
  verifies all 1,191 archive members against Release outputs, all 795 shader/interface pairs, 39 tool
  identities and 358 consumed owned-source observations with zero identity, provenance or obsolete-member
  issues. The retained ZIP is 8,512,721 bytes, SHA-256
  `B40C5D4FC8B6149C97DEEEBC78D726E0C62FC97CB37D26575859B7DD87CE4B38`.
  Release logs contain 25 unique warnings, including existing Cake dependency advisories. Source and
  index were unchanged during the captured package run.

The tested Debug and packaged Release shaders have matching consumed-source observations; Debug
debug-information flags produce different shader bytes. This receipt does not establish Release GPU
execution or byte identity between Debug and Release assemblies. The GPU process reported RTX 4090,
OpenGL 4.5 NVIDIA 621.24 and GLSL 4.50. No live appearance or frame-time/performance acceptance follows
from these headless receipts. This checkpoint preceded the final frame-owner event coverage and
comment-only source reconciliation below.

## Frame-owner lifecycle correctness receipts

The [final lifecycle/native execution](../artifacts/virtual-shadow-maps/surface-consumers/20261114T004638Z-lifecycle-final/final-lifecycle-native.trx)
passed all 16 selected cases with zero skips after the final test-source cleanup. This includes the
four frame-lifecycle cases, four native-forward attenuation cases and native frame-gate/receiver
regressions. The new cases deliver the actual registered `LeaveWorld` event, retire and restore the
render-context registration, and invoke native-reload admission/completion through an attached
`ClientRenderingLifetime`. They retain surface input sets to prove synchronous withdrawal, exercise
real page/gate/source retirement, and admit fresh generations after teardown or fence completion.

These are integration checks with a controlled frame source and real GPU owners. Context replacement
advances the registry generation on the same physical context handle; it does not destroy and recreate
the driver context. Native terrain/entity adapter behavior retains its separate qualification receipts.
The reload test additionally holds a fence pending and verifies that replacement waits for retirement.

The final strict Debug mod rebuild reported 20 warnings and zero errors; the test rebuild reported
249 warnings and zero errors. All 795 production shader/interface pairs and 962 fixture variants were
current, and all 34 Debug shadow modules lack Float64. The production-source reconciliation changed
comments only. Independent test review found no actionable issue after the region-only cleanup.

The [final composed ledger](../artifacts/virtual-shadow-maps/surface-consumers/20261114T004638Z-lifecycle-final/composed-test-results.json)
contains 431 distinct passes, zero remaining failures and zero skips: the preceding 428-case ledger
plus the final 16-case execution, with 13 overlapping cases and three new lifecycle cases. Provenance
is retained per case; this is composed correctness evidence rather than a fresh full-suite execution.

The final canonical Cake Release package also passed. Its
[identity receipt](../artifacts/virtual-shadow-maps/surface-consumers/20261114T004638Z-lifecycle-final/package-identity.json)
verifies 1,191 members, 795 shader/interface pairs, 39 tool identities and 358 consumed owned-source
observations with zero issues. The retained ZIP is 8,512,691 bytes, SHA-256
`9F523C284CF351497EB4609A0B984ED5B90CB00E5A5A2619F5AF4D14A9C04DA1`.
The [verification summary](../artifacts/virtual-shadow-maps/surface-consumers/20261114T004638Z-lifecycle-final/verification-summary.json)
retains final source/index reconciliation and shader capability checks. Post-result edits to this
record and the task list change evidence prose only. Existing staged changes are preserved.
These receipts do not establish Release GPU execution, live appearance or measured performance.

## Liquid boundary celestial extraction correction

The water-boundary capture branch in `pbr_liquid.fsh` now calls a per-source helper explicitly for sun
and moon, alongside the previously extracted liquid surface evaluation. Source review against the
retained loop confirms the same irradiance guard, phase calculation, source-specific shadow lookup
and sun-then-moon accumulation. The boundary phase evaluation does not consume angular radius.

The [fresh liquid/native regression execution](../artifacts/virtual-shadow-maps/surface-consumers/20261114T012123Z-liquid-celestial-correction/liquid-regressions.trx)
passed 69 cases with zero failures or skips, including production boundary-capture radiance and native
frame-gate shadow attenuation, refraction, traversal, shader contracts, transparency and reflection.
Independent lunar-only and nonzero-anisotropy boundary GPU assertions were not added; unchanged math
for those cases was reviewed by source equivalence. Independent coverage review found no actionable
issue for this extraction.

Strict incremental Debug mod and fixture builds passed with zero warnings/errors. Both producers
rebuilt 32 affected variants, retaining 795 current production and 962 fixture variants overall.
The [updated composed ledger](../artifacts/virtual-shadow-maps/surface-consumers/20261114T012123Z-liquid-celestial-correction/composed-test-results.json)
remains at 431 distinct passes: the 69 fresh results supersede matching prior entries. It is composed
evidence with per-case provenance, not a fresh full-suite execution.

The correction's canonical Release package passed with 1,191 members, 795 shader/interface pairs,
39 tool identities and 358 consumed owned-source observations, with zero identity issues. The
[package identity receipt](../artifacts/virtual-shadow-maps/surface-consumers/20261114T012123Z-liquid-celestial-correction/package-identity.json)
and retained ZIP identify 8,579,288 bytes, SHA-256
`B0ECE1EB90DD521AF8A383DB346C68A778D150E281BA1155F99EC62C24995317`.
All 34 shadow modules in each configuration lack Float64. The
[verification summary](../artifacts/virtual-shadow-maps/surface-consumers/20261114T012123Z-liquid-celestial-correction/verification-summary.json)
retains build, regression, package and source/index reconciliation. These checks do not establish
Release GPU execution, live appearance or performance acceptance.

## Uniform input ownership verification

Concrete uniform blocks own their packing and size declarations; renderers and programs supply named
values or operations. The [ownership correction receipt](../artifacts/ubo-encapsulation/20261114T042603Z-verification/verification-summary.json)
records 586 passes and one independently reproduced pre-existing cloud DDA failure across 587 cases.
The later [semantic-input correction receipt](../artifacts/ubo-semantic-inputs/20261114T053059Z-verification/verification-summary.json)
records 430 focused passes with no failures or skips. These selections overlap other surface evidence;
their counts are not additive. The cloud failure remains a separate limitation, outside the AST task.

## Native surface AST transaction verification

TinyAst 0.12.0 is used by the mod, shader build tool, packaging tool and shader-preparation fixture.
The [published dependency gate](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/dependency-probe-summary.json)
passes the retained adjacent replacement/insertion reproduction in both queue orders, through node and
query APIs, with trivia variants and the captured installed header. [Package provenance](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/package-provenance.json)
matches the public archive, cached dependency and actually loaded probe DLL. The historical 0.11.3
failure record remains unchanged. Compatibility cleanup, float clamp bounds and material declarations
now share the normal AST patch transaction; surviving leading extensions remain before declarations.

The [final native/capture/recovery run](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/final-native-regressions.trx)
passes all 15 cases, including six controlled captures, four native forward draws and five recovery
cases. The [main-project composed ledger](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/composed-test-results.json)
contains 177 passes, zero remaining failures and three unchanged upstream repro skips: two import
source-map cases and the distinct inner-body leading-trivia case. It retains each case's execution
provenance and the earlier failed attempts; it is not a fresh 180-case passing run. Separately,
[11 shader-build contract families](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/shader-build-contracts.trx)
pass all 113 cases with no skips. The counts above overlap earlier qualification and are not combined.

Strict Debug mod and test builds pass with 20 and 249 warnings respectively and zero errors.
The [canonical Release package identity](../artifacts/tinyast-adjacent-edit-update/20261114T055936Z-qualification/package-identity.json)
verifies 1,191 members, 795 shader/interface pairs, 39 tool identities and 358 owned-source observations
with no issues. The retained ZIP is 8,592,444 bytes, SHA-256
`0DEA978883EC2059B7FEC7212BDAFF916B45AF5D629462413F1939ADF793E9F2`.
All 34 Release shadow modules lack Float64. These receipts establish the exercised component and
headless native boundaries; live appearance and performance qualification remain open.
