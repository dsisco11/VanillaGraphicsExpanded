# Shader declaration and preparation

Fullscreen debug programs are now independently compiled per view; see
[Per-view debug shaders](GPU.DebugViewShaders.md). Earlier debug-category and legacy-fallback
validation below records the preceding implementation.

All production graphics registration now declares owners without loading shader binaries or linking
executables. `GpuShaderPrograms` owns declarations independently of the engine's registry. Consumers
look up declarations there, so a missing shader never triggers the engine's GLSL loading path.

## Graphics ownership

`GpuProgram.EnsureReady` prepares the current immutable settings through the existing SPIR-V loader
and driver executable cache. `TryUse`, `UseScope`, direct `Use`, and internal `IGpuProgram` workflows establish
readiness before activation. Failed preparation retains a previous executable but does not activate
it with incompatible inputs. Repeated attempts with the same failed inputs do not reread binaries;
changed effective settings or asset invalidation permit another attempt.

`GpuShaderPrograms.Preload` explicitly batches a selected immutable set of declarations. Callers
configure settings before submitting that set. Already-ready, retired and known-failed selections
are excluded from driver submission. Settings edits alone no longer enqueue compilation. The old
recompile queue and debug-family-specific lifecycle owner have been removed; the debug family now
only groups declarations and applies shared settings.

Explicit owner disposal is terminal. VGE never enters the engine registry or receives engine-base
disposal. The client rendering lifetime synchronously invalidates graphics asset epochs and
feature-owned compute generations before native reload, withdraws publications, and reopens demand
preparation after native refresh or failure. Declarations, settings and CPU inputs survive reload;
known failures retry only when settings/assets change or explicitly requested. Native reload's
Boolean is independent of lazy VGE readiness. Closed client libraries cannot be recreated by late
lookup callbacks. Never-linked owners can retire without native deletion.

The root owns feature/registry/shared-resource shutdown before the GPU manager drain. World leave
advances callback admission without changing native context generation. Program resources and queued
deletes carry their originating context registration; dead-context names are abandoned rather than
deleted in another context. See [shader lifetime](GpuProgram.md#reload-and-retirement).

## Preload decisions

- The direct-lighting renderer explicitly preloads its required fixed-input shader.
- An enabled LumOn renderer batches five invariant passes: velocity, anchors,
  SH9 projection, atlas filtering and upsampling.
- PIS, tracing, temporal processing, gathering and PBR composition collect their current configuration
  and resource-dependent inputs before first activation. They avoid compiling an intermediate default
  selection. Further preloading of these passes requires the owning system to know those inputs.
- Debug views, line overlays and probe markers prepare only when requested.

These boundaries preserve explicit batching without forcing every declaration to compile at startup.
Enabling a feature or selecting a view can still cause a first-use stall. This change does not make
individual driver links faster or establish a new startup performance result.

## Compute ownership

`GpuComputePipeline.DeclareFromAssets` captures immutable inputs without loading assets or issuing GL
commands. `EnsureReady`, ordinary activation and dispatch prepare the executable and transfer its
interface and handle together. Failed attempts remain suppressed until an explicit retry; disposed
pipelines cannot revive. `DispatchBound` still requires an already-prepared, bound pipeline.

`TryCreateFromAssets`, `TryLoadFromSpirv`, and low-level `TryCreate` remain explicit eager preparation
APIs for feature owners and fixtures that require a completed pipeline immediately. Existing compute
systems already invoke these at their feature-use boundaries rather than through global startup
registration. The world-probe trace owner now declares its three required programs and explicitly
preloads them through its existing linking batch.

## Retained compute reload

The `GpuComputeProgram` wrapper retains desired inputs and CPU blocks. Asset-backed eager and
deferred creation paths retain an immutable rebuild factory/source. On reload a validated candidate
`GpuComputePipeline`, interface and revision install together, then the old native owner retires.
Failed candidates retain the previous allocation but cannot dispatch it for the new asset epoch.
Raw pipelines without a rebuild description are explicitly caller-lifetime objects. Compute owners
remain feature-owned and do not require graphics registry membership. Shared default workflows and
concrete facade dispatch are documented in [GpuProgram](GpuProgram.md#shared-workflows-and-compatibility).

## Historical demand-loading validation

The following receipts predate independent shader ownership and describe that earlier implementation.
Current migration qualification is recorded in [the ownership checklist](Rendering.ShaderOwnership.todo).

Final Release validation passed **53/53** focused tests in 35 seconds, including declaration-only
registration, selective activation and preload, graphics interface activation/disposal, terminal
retirement, engine reload, failed retries, compute first dispatch, executable caching, batching,
debug fallback and seven actual PBR/lighting runtime cases. Evidence:
`artifacts/ShaderDemand/demand-all-release.trx` and `artifacts-demand-all-release.log`.

An earlier Debug integration run passed 15/15 shared-preparation and runtime cases:
`artifacts/ShaderDemand/demand-all-runtime-debug.trx`. The final retirement changes are covered by
the final Release run. The 249-variant reload inventory theory was not rerun; focused graphics
lifecycle cases were included. No new performance profile or in-game visual check was performed.

Independent review passed after correcting terminal disposal versus engine reload, excluding retired
owners before batch submission, replacing retired family declarations, and preparing PBR composition
before framebuffer mutation. No game process was launched or stopped.

Historical
19-program eager registration and cold/warm selection measurements remain in
[GPU.ParallelShaderCompilation.md](GPU.ParallelShaderCompilation.md); they predate this generalized
registration policy and must not be presented as current startup measurements.
