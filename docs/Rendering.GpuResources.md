# GPU resource metadata and lifetime

`GpuResourceManager` owns canonical metadata for the originating rendering context. Its concurrent
registry uses `GpuResourceHandle`, a resource-kind/native-width-ID value, as the key. Each entry holds
one typed metadata snapshot, availability and revisions, shared usage guards, and weak wrapper recipients.
Registration grants no native ownership. Metadata operations and native queries run on the context thread;
concurrent containers support identity lookup and recipient removal across threads.
Tracked command inputs, operation admission, deferred link submission and executable transfer validate
the originating context and thread before dispatch or metadata withdrawal. Reusing an available snapshot
does not bypass that check. Explicit operation steps recheck affinity after callbacks.

## Wrappers and initialization

`GpuResource` supplies identity, ownership, disposal and deletion scheduling. `GpuResource<TMetadata>`
stores a snapshot, exposes it read-only to derived wrappers, and assigns complete snapshots through
`HandleMetadataChanged`. Tracking, availability, revisions and recipient membership belong to the manager.
Resource properties read the supplied snapshot; allocation policy, upload strategy and selected
subresources remain with their existing wrappers and helpers.

Resource-specific initialization records expose legal inputs and automatically record assigned fields,
including explicit zero and false values. Manager creation methods validate those requests and use
the OpenGL backend through `RHICommandContext` for buffers, images, samplers, queries, fences, transform
feedback, semaphores, memory objects, framebuffers, vertex arrays and executable objects. Private
initialization receives the admitted context explicitly and dispatches through `IRHIResourceExecution`,
without retrieving the concrete backend or reentering a public factory. A nullable metadata result reports detected failure;
identity and a complete initial snapshot are handed to the wrapper only after successful initialization.
Partial allocation cleanup preserves the original exception and the originating deletion context.

## Command results and queries

The OpenGL RHI backend owns buffer/image, synchronization, query-result, readback, capture and optional
external-object, framebuffer, vertex-array and executable native dispatch; StateCache retains selector
knowledge and operation guards. Manager-owned readers request native discovery through the context's
internal execution contract. Controlled operations publish inputs,
guaranteed defaults and derived facts. Existing failure handling remains authoritative: reported failure
does not publish success, and interruption withdraws affected knowledge. No universal post-command query,
error drain or separate requested/actual metadata cache is used. Available covered aliases reuse canonical
facts; external unknown state and explicit recovery use manager-owned readers.

Driver-produced shader compilation/link results and interfaces retain their completion checks. Allocation
uses cached target/format sample capabilities, with a single sample-count read when those capabilities
cannot supply the result. Unsized image formats may require a resolved-format read. These exceptions do
not authorize full descriptor scans after controlled allocation. Native readers use core named queries; cube-face descriptors retain their required bound selection
and restoration; optional renderbuffer-backed association queries retain their required selectors.
Buffer readers use core named queries, and are invoked only for explicit unknown external acquisition or recovery.

An operation captures its original identity, withdraws affected availability, constructs a complete result
and commits once. Participating internal steps receive the operation explicitly. Unrelated same-resource
reentry is rejected before dispatch. Cleanup does not recover or retry mutations; recovery is explicit.
Deferred program linking retains its separate submission/completion boundary. Snapshot assignment precedes
availability and dependent notification. Selected framebuffer images project backing metadata without
another canonical entry or reader when facts are available.

## Shader executable ownership

Shader asset selection, digest validation, permutations and binary-cache policy live in
`ShaderCompilation`. Frozen `RHIShaderDescription` inputs specify stage code, entry points,
specializations and authored bindings. The backend creates stages/programs through the manager, links
or installs a selected binary, publishes one canonical executable snapshot and prepares its interface.
SPIR-V input admission checks the selected entry-point name and execution model before native ingestion;
this declaration check does not reflect resource bindings or replace native compiler diagnostics.
Temporary stages are retired after completion. Pending creation exposes outcomes through
`IRHIShaderCreation`; the OpenGL implementation privately retains native candidate stages and programs.
Cancellation retires only those candidate allocations against their original context.

`RHIShader` owns one ready `GpuProgramObject` and immutable prepared interface. Its consumer ref retains
that exact generation and rejects native executable changes or retirement without querying or repairing
metadata. `ComputeShaderAsset` owns a compatible RHI shader and lightweight compute PSO set; it has no
native registration. Asset replacement builds a fresh set before transferring ownership, preserving the
installed set on failure. Program and stage wrappers own identity, canonical metadata and native lifetime;
compilation and linking belong to backend execution rather than wrapper methods.
Graphics and compute realizations retain the backend-neutral `IRHIContextLifetime` admission contract.
Graphics relevance and restoration coverage live in opaque backend-owned realization data; semantic
pipeline objects retain immutable descriptions, fixed shader references and independent lifetime state.
OpenGL composition and restricted engine shader integration live in `OpenGLRHIContext`; no semantic
context property exposes the concrete backend.

Compute dispatch guards both its selected realization and native executable against retirement and
rejects recursive submission or pipeline-selection changes. Managed retirement unbinds a selected owned
program before deletion, preventing subsequent scopes from capturing a deleted restoration target.
Exact external deletion preserves OpenGL's deferred current-program behavior.

Graphics description depth/stencil comparisons, stencil updates, blend equations/factors and raster
conventions use project-owned RHI enums. OpenGL translation selects named enum members explicitly;
semantic values do not share native token numbering. The state cache retains native observation and
restoration values. Blend validation and state relevance share operand-dependency rules, including
MIN/MAX factor independence and dynamic constant requirements.

`RHIVertexLayout` copies and sorts `RHIVertexAttribute` declarations and compares their complete packing
structurally. Component storage and shader interpretation use project-owned enums; no buffer identity
belongs to the layout. Duplicate locations, incompatible component interpretations and inconsistent
strides or divisors on a shared binding reject without a graphics context. Native configuration and
external attribute checks translate storage through explicit backend mappings.

Engine terrain replacement uses an explicit OpenGL integration handoff that preserves borrowed engine
vertex/fragment stages, validates original identities before installation and retires the old program.
Post-install cleanup failure is reported separately from candidate failure, so installed topology remains
recognized. This native ownership operation is not exposed through the portable shader description.

## Buffer execution

`RHICommandContext` admits desktop OpenGL 4.5 and required core buffer and image entry points for its original
context/thread. `RHICapabilities` projects the existing capability snapshot without a second discovery
service. `RHIBufferDescription` uses project-owned role, storage, usage and access values and distinguishes
omission from explicit zero. Creation returns `RHICreationResult<T>` containing an already initialized
existing owner, or diagnostics; the manager retains private identity/snapshot handoff and candidate cleanup.

`GpuBufferUploader` validates client payloads and routes allocation and uploads into that semantic path.
Core named allocation, upload, copy, clear, map and flush preserve selectors. Canonical `GpuBufferMetadata`
contains capacity, usage, immutable-storage status and storage-access flags; content operations never
publish another storage snapshot. Partial initial bytes use direct subdata when permitted, otherwise a
temporary staging copy. Native deferred destruction retains that staging storage until the copy completes.
No alternate execution path is retried after failure.

`GpuBufferRef` privately retains the typed resource and exposes only published metadata and managed
identity. It grants no lifecycle or storage-replacement authority. Backend use validates original context
and manager association before examining binding suppression. Exact engine buffer overloads execute in
`Rendering/RHI/Backends/OpenGL/Buffers/External`, preserving original arguments and native errors; they
withdraw uncertain metadata/bindings rather than applying managed omission or suppression rules.

Context-local buffers retain physical context identity without registering a second lifetime system.
Local tools must dispose their owners before destroying that context; foreign-context disposal rejects
without surrendering ownership. Registered resources retain the existing deferred retirement service.

## Image execution

Texture, renderbuffer, buffer-texture, native-view and sampler factories accept project-owned descriptions
and return typed ready owners. Omitted initialization inputs retain guaranteed defaults; assigned zero
and false remain explicit. Initial pixels are separate region/encoding/pitch payloads consumed before
handoff. No controlled allocation reads back its complete descriptor.

Immutable allocation, uploads, copies, clears and sampler configuration use core named commands.
Mutable image definitions retain bound semantics, with backend-owned active-unit, target and pixel-buffer
restoration. Cube faces and mip dimensions derive from the allocation request; array layers remain
constant across mip levels. Multisample counts use the existing target/format cache, with the approved
single-value read only when that cache cannot supply a result. Optional coverage-sample storage is
capability checked before dispatch and reads only its resulting sample count.

GPU buffer/image transfers carry explicit byte offsets and row/slice pitches. Pixel layouts and pack/unpack
selectors use the existing cache and restore on unwind in their original context. Content operations do
not publish allocation revisions. Mip generation is immediate: mutable image geometry commits once when
it changes; regeneration of existing images preserves storage revisions.

Buffer textures retain the original backing dependency and explicit whole/range intent. Native views
retain source storage provenance and expose view-relative mips/layers. Failed creation retires only its
candidate texture, never its backing. External borrowing lives at the native integration boundary and
reuses available manager facts; readers execute only for unknown external facts or explicit recovery.
Queued uploads retain their selected destination storage certificate and revision, so replacement cannot
receive stale staged bytes. PBO payloads retain typed buffer references through consumption; pinned
client payloads and PBO transfers use semantic RHI upload/transfer operations. These payload certificates are separate from consumer references and content
readiness. Physical-context tools use the same creation/retirement path without another registry.

Exact image adapters live in backend texture, buffer-texture and renderbuffer external modules; sampler
and image deletion overloads share the backend lifetime boundary. Each external call preserves its
original signature and native errors, executes once, and uses the existing observation/publication service.
External selectors never use managed duplicate suppression; uncertain binding facts recover lazily.

## Synchronization, results and auxiliary execution

Explicit RHI access barriers order visibility without inferring hazards or adding implicit fences.
Fence insertion creates the existing native-width owner through private manager handoff; polling and
CPU/GPU waits validate its original association. Pending or failed waits do not release staging or
uniform storage for reuse. CPU submission is not GPU completion.

Queries declare their category before core named realization. Timestamp, elapsed, occlusion and primitive
queries use the baseline; pipeline statistics require their cached optional capability. Result tickets
retain one issued execution revision, reject later reissue and distinguish Pending, Ready, Failed and
Cancelled. These are request states, not canonical metadata or another resource registry. CPU result
reads preserve external query-buffer binding and retrieve unsigned 64-bit values. Conditional rendering
consumes issued occlusion queries without CPU result readback.

Readback descriptions select a buffer interval or view-relative image region and explicit pixel pitches.
The request privately owns staging plus its completion fence. Mapping requires readiness and is exclusive;
its scoped read-only bytes remain tied to the original context. Synchronous callers use
`RHIReadbackTicket.Wait()` with the shared `DefaultWaitTimeout`; callers can also supply an explicit
timeout or poll without blocking. Cancellation retires native storage
without putting in-flight ranges into a reusable CPU pool. Registered-context retirement cancels pending
requests and abandons native cleanup rather than issuing it into a replacement namespace. Tickets use
the internal backend-neutral execution contract for observation and mapping. Existing queues
may retain and map their own fenced output storage through the same backend instead of adding a copy.

Transform-feedback creation configures named output ranges from frozen authored descriptions. Capture
retains the object selection and output lifetime guards across begin/end, pause/resume and scope unwind;
per-method scopes never restore a binding during active capture. Canonical capture metadata stays empty.
Optional memory/semaphore descriptions make platform-handle kind and ownership explicit: successful FD
imports transfer their descriptor to GL, while Win32 handles remain caller-owned. The pinned OpenTK
package exposes these optional entry points only through GL.Ext, confined to backend modules. No payload
state is invented in canonical metadata. Native external-producer import qualification remains separate
from object creation and capability-rejection tests.

Sparse buffer allocation uses explicit sparse storage intent and cached page alignment. It rejects
mapping and initial bytes before commitment; commitment changes residency, not allocation metadata.
Typed sparse-image commitment supports explicitly configured native integration storage without inventing
an allocation descriptor or querying unrepresented sparse page facts on each call. Exact native error
behavior remains authoritative for those integration facts.

`GpuSyncRef`, `GpuQueryRef`, `GpuTransformFeedbackRef`, `GpuSemaphoreRef` and `GpuMemoryObjectRef`
retain existing owners through wrapper-owned conversions. The manager certifies published usability;
references expose no disposal, handle-transfer or native execution methods. Exact auxiliary deletion
adapters preserve their original loader/signatures and per-name outcomes before shared retirement
publication. Managed deletion uses the admitted backend after original-context queue admission.

## Imported geometry certification

The manager authorizes cold external vertex-array configuration discovery through the RHI context.
The backend performs native checks, while the completed receipt retains the original array association
and metadata revision. Context-local receipts use the selected array's observed configuration revision,
so unrelated array configuration does not expire their proof. An engine array with explicitly draw-time
index selection may remain detached; after certifying its attributes and detached state, the backend
realizes a private array from the declared streams and original index allocation. A different nonzero
index attachment rejects certification. Native engine arrays are never repaired as a side effect.
`IsEngineGeometryConfigurationCurrent` checks the receipt against current context/cache knowledge without
native configuration reads. Engine adapters retain layout, ownership and borrow-expiry policy.

## Binding knowledge and retirement

`GpuTextureRef` is a readonly consumer value holding one private `GpuResource<GpuTextureMetadata>`
reference. Implicit operators on `GpuTexture`, `GpuBufferView` and `GpuTextureView` supply it; borrowers use the texture
base conversion. It exposes identity and published metadata without public disposal, mutation or
underlying-resource access. Null wrappers produce an empty value. Equality compares managed wrappers;
intentional owner replacement is visible through retained references. The value adds no registration,
native allocation, captured lifetime token or metadata cache.

The manager checks usability and native-view source dependencies using existing associations and local
storage certificates. Metadata access performs no recovery. Explicit recovery remains with the producer;
unavailable facts reject active submission. Shader compatibility history examines current identity and
metadata revisions even when the reference value compares equal. Texture-reference retention does not
acquire a pass guard or prevent explicit owner disposal.

Resource validity and metadata availability do not prove that an object is bound. StateCache separately
tracks selectors, image parameters, buffer ranges and framebuffer routing. Controlled known-equal bindings
can be suppressed. Arbitrary external selectors preserve exact execution and withdraw uncertain knowledge
for lazy recovery. Fallback scopes preserve separate read/draw framebuffer selections and original VAO
element-buffer associations. Vertex helpers retain authored fields and resolve only required unknown
facts through the manager; the canonical VAO schema contains no full layout mirror.

Deletion removes the original entry and invalidates its recipients. Reusing the numeric ID creates a new
entry; old wrappers and queued work retain their original association and cannot affect the replacement.
Owner retirement withdraws usability before deferred deletion. Queue admission retains original context
and registration identity; immediate, local-tool and queued retirement all reach the same internal context
operation and backend kind dispatcher. Borrowed disposal only removes its recipient;
it never deletes external storage. Context retirement clears registrations and abandons work that cannot
run in its originating context. Weak recipients are pruned during acquisition, publication and removal.

## External coverage and consumers

Compiler-checked adapters redirect supported calls discovered in the built-in engine assemblies through
backend resource modules or the remaining StateCache operation families. Allowed OpenTK owners are GL (also named Core by adapters), GL.Ext and GL.Arb. Other auxiliary
owners and legacy fixed-function facilities are outside interception. Generic discovery handles concrete
value-type instantiations from built-in assembly references; arbitrary mods and dynamic callers are outside
that boundary. A registry entry does not extend interception to unsupported callers.

Cloud and depth consumers retain typed borrowers across stable selections and share existing owned material
depth directly. Their content readiness, upload/generation certificates, frame qualification and producer
selection remain separate from resource metadata. Unsupported raw sampler IDs retain conservative validation
and slot invalidation. See [shader bindings](Rendering.ShaderBindingState.md#prepared-resource-submission)
and [cloud ownership](PBR.Clouds.md#borrowed-map-contract) for consumer contracts.
