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

Owned framebuffer declarations and attachment-owned renderbuffers request allocation through private
owner-bearing RHI operations. Their backend implementations invoke the existing manager handoff;
constructors do not coordinate native allocation or publication directly. Geometry arrays and unlinked
program recipients are declared and completed inside backend realization. Native program-pipeline
initialization remains private machinery for explicit native integration, with no general wrapper factory.
Framebuffer construction failures retire only the new framebuffer, preserving borrowed images. Failed
attachment construction returns newly owned storage to its captured disposal queue.

Resource wrappers retain allocation, growth, streaming and disposal policy. Mutation and retirement
use private owner-bearing RHI execution; wrappers do not select backend implementations or translate
semantic command descriptions with backend helpers. Buffer role is owner policy, separate from storage
metadata. Texture/image/sampler slot publication belongs to RHI resource operations, not wrapper bind
methods. Buffer-texture views use the same retained texture reference as other sampled textures.

Framebuffer configuration admission runs through owner-bearing RHI operations before shared attachment
storage or native selectors change. Static framebuffer owners retain a disposable configuration lease;
the backend registers the immutable declaration in the existing cache and withdraws it on lease disposal
or original-context retirement. Lease cleanup issues no native calls and can run on a worker thread.
Backing-allocation guards remain owner policy and are released on disposal or detach. Detaching a wrapper
or publishing attachments does not discard the still-live native object's independently known output route.
Observed deletion removes that route through the backend's existing retirement publication.
Screen resize preserves unrelated executed knowledge and runs resource callbacks in dependency order;
uniform frame callbacks advance allocation/publication epochs without purging bindings each frame.

## Explicit acquisition and image dependencies

Image and framebuffer wrapper APIs, canonical metadata, initialization records and capability-cache keys
use project-owned `TextureDimension`, `CubeFace`, `TextureFormat` and `FramebufferSlot` values.
Texture shapes and cube-face selections are separate facts. Format capability requests distinguish
texture dimensions from renderbuffer resource kind; the shared cache holds these semantic keys.
Framebuffer selection and targeted query fields also use project-owned types, and routing carries
color indices. Native adapters decode actual external inputs, while the backend's codecs encode
commands and decode query results. Ordinary resources and manager services never call those codecs.
Canonical metadata and initialization retain their existing manager ownership.

Borrowed texture admission, executable adoption, interface acquisition and imported framebuffer
publication enter through private `RHICommandContext` resource operations. The admitted backend invokes
manager acquisition; wrappers and interface preparation do not select native readers. Available aliases
reuse their canonical entries. Converting a reference, reading a published snapshot and validating an
attachment slot never acquire or recover facts.

Imported framebuffer publication holds one metadata operation while discovering descriptors and resolving
backing images. The manager preserves face, layer, view range and observed attachment-mode evidence;
the framebuffer owns its retained image composition. Identity replacement, availability withdrawal and
failure notification belong to that explicit transaction. A completed publication emits one owner event;
a failed publication withdraws old metadata and retained composition while preserving borrowed storage.

Canonical entry recipients deliver registered image projections and dependent notifications. A
manager-owned weak owner/image relationship covers context-local owners and intentional owner replacement;
it contains no canonical metadata or native-ID registry, and skips images already delivered by their
current entry. Local projection uses supplied owner snapshots. Missing selections become unavailable,
and one failed dependent does not starve other images. Storage operations report missing local image
selections after the owner allocation has committed; that dependency error does not retire the successful
allocation. Candidate initialization failure remains separate and clears the failed identity in the backend.

## Command results and queries

The OpenGL RHI backend owns buffer/image, synchronization, query-result, readback, capture and optional
external-object, framebuffer, vertex-array, executable and fixed-function native dispatch.
`OpenGlStateCache` retains selector knowledge, eligible transition suppression and operation guards. Manager-owned readers request native discovery through the context's
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
Deferred program linking retains its separate submission/completion boundary. Pending link records
are held by the backend against the original program wrapper, association and metadata revision;
submission supersedes only that wrapper's previous request, and completion consumes it once. Snapshot assignment precedes
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

Engine-owned executables consume captured logical uniforms through
`RHICommandContext.BindUniformSnapshot`. Sampled engine inputs use one complete
`BindSampledTextures` packet of retained texture/sampler references and semantic dimensions. The
backend validates every supplied slot and backing dependency before binding effects, retains original
resources during execution, and uses the same cache suppression and multi-bind publication as owned
shader tables. Empty inputs clear only their declared texture target and supplied sampler. Program
membership, sampler assignment and engine generation policy remain with their explicit integration.

Pass completeness and its attachment/routing revision reuse belong to backend target realization.
Immutable native framebuffer configuration declarations in the thread-local cache use original context
and native ID together; manager registry keys remain resource kind/ID within their owning manager.
Terminal associations cannot certify routing or block a new allocation while wrapper disposal is deferred.
Framebuffer owners retain image composition and notifications; private RHI operations execute their
native configuration and status checks. Window-surface discovery occurs only at explicit publication.

Explicit engine framebuffer extension uses the restricted OpenGL integration service for attachment
transactions, output routing and blend application. Engine callback mesh upload/deletion and particle
output redirection use that same admitted native boundary, including selector restoration. Cloud adapters
acquire external framebuffer/program facts through the manager and reuse covered snapshots; adapter
publication observes callback outcomes separately from metadata validity and never consumes ambient errors.

Optional bindless owners likewise route handle issuance/residency through private RHI execution and
never issue cleanup against a replacement context.

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

## Recorded command ownership

`RHICommandList` is an optional CPU recording for an explicitly supplied `RHICommandContext`.
Recording uses no current native context, resource validity, metadata acquisition, pass guards or
physical uniform allocation. Immutable descriptions and exact shader/PSO references are retained;
upload bytes, indexed batches and variable slot/transition spans are copied into reusable list storage.
Headers identify typed payload locations, and managed resources remain in typed list-owned tables.
Immediate execution allocates no recording storage.

Close publishes a complete recording and an immutable `Boundary` derived from its pipeline references,
operation kinds and explicit helper slots. Enter that boundary when restoration is required, or submit
inside an existing boundary that admits the whole footprint. Submission cannot widen a declaration or
enter/leave a caller-owned pass. Each list contains complete pass, query, conditional and capture intervals.
Native execution goes through the same immediate context operations in explicit order, without automatic
state isolation, a completion fence or a GPU wait. Immediate submission remains the default.

Lists are exclusive to their originating thread and follow Recording -> Closed -> Executing -> Executed
or Failed. Cancellation is allowed only before execution; only terminal lists can reset. Reset and disposal
release CPU references while keeping reusable page capacity; they never dispose issued result storage.
Resources are resolved again at execution: owner-following references see deliberate replacement;
expired borrowers and fixed shader generations reject rather than retarget. Retention extends no engine
callback borrowing interval and certifies no producer content freshness.

Recorded readback/query requests use their existing ticket types; recorded fences return `RHIFenceTicket`.
They stay pending without allocating a native result until their operation executes. Cancellation creates
nothing. Enqueued staging/fences belong to the tickets independently of list reset. Failure stops once,
reports the offending index and original error, and separately reports cleanup errors. Unexecuted requests
fail or cancel; earlier GPU work remains enqueued. Scope unwind uses existing backend services and only
list-owned intervals. Observing/submitting closed work or polling pending tickets after origin retirement
cancels CPU promises without entering a replacement context.

Captured uniform bytes/revisions publish through the existing allocator in the execution epoch. Recording
and cancellation never advance upload history or clear dirty CPU edits. Missing allocation authority fails
recorded publication; successful execution commits only the captured revision, preserving newer edits.

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

Resource validity and metadata availability do not prove that an object is bound. OpenGlStateCache separately
tracks selectors, image parameters, buffer ranges and framebuffer routing. Controlled known-equal bindings
can be suppressed. Arbitrary external selectors preserve exact execution and withdraw uncertain knowledge
for lazy recovery. Required selector-restoration scopes preserve separate read/draw framebuffer selections and original VAO
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
the focused OpenGL backend execution modules. Allowed OpenTK owners are GL (also named Core by adapters), GL.Ext and GL.Arb. Other auxiliary
owners and legacy fixed-function facilities are outside interception. Generic discovery handles concrete
value-type instantiations from built-in assembly references; arbitrary mods and dynamic callers are outside
that boundary. A registry entry does not extend interception to unsupported callers.

Cloud and depth consumers retain typed borrowers across stable selections and share existing owned material
depth directly. Their content readiness, upload/generation certificates, frame qualification and producer
selection remain separate from resource metadata. Unsupported raw sampler IDs retain conservative validation
and slot invalidation. See [shader bindings](Rendering.ShaderBindingState.md#prepared-resource-submission)
and [cloud ownership](PBR.Clouds.md#borrowed-map-contract) for consumer contracts.

## Backend state ownership

`OpenGlStateCache` is internal to `Rendering/RHI/Backends/OpenGL/State`. It owns the existing thread-local
executed knowledge, independent known-field masks, original-context selection, binding/routing keys,
operation guards and selective restoration. Its Bindings, FixedFunction, Observation and Boundaries
modules separate those responsibilities. It has no resource registry, native allocation API or arbitrary
callback execution API. Manager operation services consult guards without issuing native work.

Admitted managed fixed-state transitions use the stateless `OpenGLStateEffects` modules to issue core
4.5 native effects. These modules hold no knowledge, suppression, metadata or alternative dispatch path.
The cache withholds the affected known bit before dispatch and commits it only after the effect succeeds.
Unknown incoming selectors and selective restoration facts are queried by the cache's native observation
services. Baseline independent blending, clip control, sample shading and fixed-index restart have no
older-context fallback. Existing limits and profile distinctions remain authoritative.

Exact engine fixed-state calls live in backend `State/External/EngineStateCalls` modules. They preserve
the original overload, arguments and native error behavior, issue one native call without suppression or
normalization, and withdraw only affected state fields. Invalid native arguments never become a
managed setter exception. Metadata/resource adapters use their corresponding focused backend modules.
Native calls in these modules are outside rewritten engine call sites, so forwarding cannot recurse.
