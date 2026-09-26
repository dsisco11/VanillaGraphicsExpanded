# Shader declaration and preparation

Fullscreen debug programs are now independently compiled per view; see
[Per-view debug shaders](GPU.DebugViewShaders.md). Earlier debug-category and legacy-fallback
validation below records the preceding implementation.

All production graphics registration now declares owners without loading shader binaries or linking
executables. `GpuShaderPrograms` owns declarations independently of the engine's registry. Consumers
look up declarations there, so a missing shader never triggers the engine's GLSL loading path.

## Graphics ownership

`GpuProgram.EnsureReady` prepares the current immutable settings through the existing SPIR-V loader
and driver executable cache. `TryUse`, `UseScope`, direct `Use`, and `IShaderProgram.Use` establish
readiness before activation. Failed preparation retains a previous executable but does not activate
it with incompatible inputs. Repeated attempts with the same failed inputs do not reread binaries;
changed effective settings or asset invalidation permit another attempt.

`GpuShaderPrograms.Preload` explicitly batches a selected immutable set of declarations. Callers
configure settings before submitting that set. Already-ready, retired and known-failed selections
are excluded from driver submission. Settings edits alone no longer enqueue compilation. The old
recompile queue and debug-family-specific lifecycle owner have been removed; the debug family now
only groups declarations and applies shared settings.

Explicit owner disposal is terminal. Engine shader reload calls the base engine disposal method;
that invalidates GL ownership without retiring the declaration. Subsequent preparation recreates
and re-registers it. Application teardown retires and removes the shared library. Never-linked
owners can be retired without GL deletion calls.

## Preload decisions

- The direct-lighting renderer explicitly preloads its required fixed-input shader.
- An enabled LumOn renderer batches seven invariant passes: velocity, anchors, HZB copy/downsample,
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

## Validation

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
