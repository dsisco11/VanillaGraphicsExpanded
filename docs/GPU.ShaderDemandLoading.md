# Shader declaration and preparation

Fullscreen debug programs are independently compiled per view; see
[Per-view debug shaders](GPU.DebugViewShaders.md).

All production graphics registration now declares owners without loading shader binaries or linking
executables. `GpuShaderPrograms` owns declarations independently of the engine's registry. Consumers
look up declarations there, so a missing shader never triggers the engine's GLSL loading path.

## Graphics ownership

`GpuProgram.EnsureReady` prepares the current immutable settings through the existing SPIR-V loader
and driver executable cache. Internal `IGpuProgram` preparation completes readiness before a renderer constructs its RHI pipeline
and captures bindings. Pipeline selection and execution belong to `RHICommandContext`. Failed preparation retains a previous executable but does not activate
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

`ComputeShaderAsset` retains immutable asset/rebuild inputs and composes a ready RHI shader and compute
pipeline. Declaration does not load assets or execute GL. `EnsureReady` prepares the selected executable
and interface before input capture; explicit eager factories remain available to feature owners and
fixtures. Disposed owners cannot revive, and failed requests retry only on changed inputs or explicit
retry. Renderers select the resulting pipeline, bind the captured table and dispatch through RHI.
Work uploads, clears, barriers and readbacks remain explicit ordered operations.

## Retained compute reload

The `GpuComputeProgram` wrapper retains desired inputs and CPU blocks. Asset-backed eager and
deferred creation paths retain an immutable rebuild factory/source. On reload a validated candidate
`ComputeShaderAsset`, interface and revision install together, then the old native owner retires.
Failed candidates retain the previous allocation but cannot dispatch it for the new asset epoch.
Raw pipelines without a rebuild description are explicitly caller-lifetime objects. Compute owners
remain feature-owned and do not require graphics registry membership. Shared default workflows and
concrete facade dispatch are documented in [GpuProgram](GpuProgram.md#shared-workflows-and-compatibility).
