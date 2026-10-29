# Pipeline state and rendering authority

`RHICommandContext` is the graphics/compute execution boundary. Renderers choose work and ordering;
immutable RHI pipeline descriptions supply intended state. The OpenGL backend validates and executes
operations using one `OpenGlStateCache` and the existing `RHIResourceManager`.

## Shader ownership and boundary lifetime

`RHIShader` owns a completed executable and prepared interface. Graphics/compute pipelines retain its
exact generation plus immutable descriptions and opaque backend realizations. They do not own another
program or reflect/link again. Shader retirement invalidates dependent pipeline use; pipeline disposal
does not dispose its shader. Asset reload builds candidates before installing a replacement set.

`RHICommandContext.EnterBoundary` admits the complete permitted pipeline set and helper-state footprint
before mutation. Existing selective capture/restoration retains engine state and original associations.
Boundaries do not acquire shader ownership, widen after mutation or turn raw program IDs into VGE owners.
Recorded lists supply an immutable `Boundary`; Submit admits that footprint without implicit isolation.
An existing caller-owned pass cannot be closed as cleanup for rejected submission.

## Draw dependencies

Graphics descriptions supply shader, vertex layout, topology, target format/sample signature, static
state and dynamic-field declarations. Supplied values are validated even when dormant. Realization
derives the groups that can affect emitted primitives and attachments; application establishes those
groups while preserving irrelevant incoming native fields. Dynamic inputs must be declared and supplied.
Owner/resource identities, current ranges and feedback constraints are checked before effects.

Geometry inputs retain typed streams/index ranges and immutable layout. The backend realizes vertex input
and executes direct/indexed/batch/instanced/indirect draws. Engine adapters retain ownership, layout and
borrow-expiry policy without native drawing. Index/indirect contents remain the producer's responsibility;
validation never reads them back or introduces GPU synchronization to inspect values.

## Sparse boundary authority and clear helpers

Coverage is field-specific for depth/stencil, rasterizer, blending per output, sampling, output
interpretation, primitive assembly and dynamics. Clear helpers declare the masks/scissor/routing they
can change; they do not silently broaden a boundary. Incoming unknown covered values are observed before
mutation. Restoration attempts independent fields even after failure and withdraws only uncertain facts.

Color clears select typed float/signed/unsigned operations. Depth and stencil load/store actions remain
independent. Backend pass helpers preserve masks and selectors, check completeness before effects and
retain original target lifetime. Attachment descriptions use view-relative subresources; feedback checks
follow shared-storage dependencies. Load/store/clear intent is not part of framebuffer realization identity.
No renderer needs an FBO merely to describe a portable attachment-based pass.

## State categories and native effects

`OpenGlStateCache` lives under `Rendering/RHI/Backends/OpenGL/State`, split into Bindings, FixedFunction,
Observation and Boundaries. Value structs and independent known masks distinguish known false/zero from
unknown. Indexed blending and global aliases share one owner; global writes update all affected outputs.
Texture, sampler, image, buffer-range and framebuffer-routing keys retain complete selections.

Cache transition methods own admission, eligible equality checks, knowledge withdrawal and successful
commit. Stateless `OpenGLStateEffects` modules issue managed core 4.5 fixed-state calls. They own no
knowledge, metadata or secondary dispatcher. Cache observation queries resolve only unknown required
selectors/restoration facts. Metadata acquisition is manager-owned and executes through private RHI
backend query services. Immediate operations allocate no recording storage.

`EPipelineState` describes selective knowledge invalidation independently of pipeline descriptor masks.
Invalidation does not execute native commands. Retirement notifications update only affected selectors
and prevent restoration of deleted/reused identities. Resource metadata validity does not prove binding.
Manager guard checks and context lifetime remain independent of suppression and shader-local history.

OpenGL 4.5 admission makes independent blending, clip control, sample shading, fixed-index restart,
core DSA and multi-bind baseline. Limits and actual profile distinctions still apply. Optional features
beyond that baseline require explicit support; there is no older-context fallback or retry after failure.
User clip-distance enables are disabled for managed draws and restored to their engine values; authored
custom clipping is unsupported. Modern line/polygon smoothing and programmable point origin remain
profile-aware native state, separate from excluded legacy point smoothing/stipple.

## Engine integration

Compiler-checked exact adapters cover GL/Core, Ext and Arb. Native forwarding is outside rewritten
engine call sites and cannot recurse. Fixed-state engine calls preserve original overloads, arguments,
errors and exactly-once execution; they never use managed duplicate suppression or normalization.
Affected fields become unknown rather than asserting success after native rejection.
Managed core effects and exact external calls share knowledge/publication services, not public executors.
See [engine integration](Rendering.EngineStateSwitching.md).

`GlPipelineDesc` is an internal partial-intent value for bounded integration/restoration; it is not a
complete graphics pipeline, executable selector or parallel submission API. Ordinary graphics/compute
consumers use RHI descriptions, retained bindings and the unified context. No old context/cache alias
or arbitrary callback executor remains.

## Standalone numeric input migration inventory

Ordinary numeric values retain declared upload locations and logical desired state. Resource inputs use
fixed binding contracts and typed references. Generated tables freeze interface identity and inputs;
uniform snapshots retain logical bytes/revisions rather than physical ring offsets. Execution uses
the current publication epoch and commits only successfully uploaded versions. Cancellation never clears
dirty CPU state. See [shader binding state](Rendering.ShaderBindingState.md).

## Verification boundary

Pipeline description, categorized state, boundary, pass/feedback, geometry, shader/uniform and RHI
recording tests cover native output, rejection, suppression, original identity and restoration.
Diagnostic counters are DEBUG-only. Headless correctness does not establish installed-game appearance,
runtime activation or frame-time performance. Qualification receipts belong to implementation records.
