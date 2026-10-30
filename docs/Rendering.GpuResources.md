# GPU resource metadata and lifetime

`RHIResourceManager` owns canonical metadata for the originating rendering context. Its concurrent
registry uses `GpuResourceHandle`, a resource-kind/native-width-ID value, as the key. Each entry holds
one typed metadata snapshot, availability and revisions, shared usage guards, and weak wrapper recipients.
Resource registrations distinguish reserved names from realized objects independently of availability and
terminal retirement. For buffers, Gen reserves; legal realization publishes capacity zero, StaticDraw usage,
mutable mode and no storage flags on the original entry. Other kinds retain their specific factory and
realization semantics. Registration grants no native ownership. Metadata operations and native queries run on the context thread;
concurrent containers support identity lookup and recipient removal across threads.
`RHIResourceManagerSystem` publishes this single service and captures its original
`RHIResourceDisposalQueue`. `RHIResourceManagerModSystem` connects that lifetime to engine callbacks;
it does not introduce a second registry. `RHIUniformRingSystem` exposes the current allocator, while
`RHIUniformRingFrameController` and its begin/end renderers advance and close publication epochs.
`RHIUniformRingModSystem` owns their engine registration. The storage/allocation contract remains
`GpuUniformRingBuffer`; `GpuBindingRegistry` remains the project's authored shader-slot assignments.
Tracked command inputs, operation admission, deferred link submission and executable transfer validate
the originating context and thread before dispatch or metadata withdrawal. Reusing an available snapshot
does not bypass that check. Explicit operation steps recheck affinity after callbacks.

## Wrappers and initialization

`GpuResource` supplies identity, ownership, disposal and deletion scheduling. `GpuResource<TMetadata>`
stores a snapshot, exposes it read-only to derived wrappers, and assigns complete snapshots through
`AssignMetadataSnapshot`. Registration identity, canonical availability, revisions and recipient membership belong to the manager.
The snapshot container also retains manager-written local presence and unfinished-effect evidence for
context-local completion/recovery; these passive facts never override a tracked entry or initiate acquisition.
Resource properties read the supplied snapshot; allocation policy, upload strategy and selected
subresources remain with their existing wrappers and helpers.

Resource-specific initialization records expose legal inputs and automatically record assigned fields,
including explicit zero and false values. Manager creation methods validate those requests and use
the OpenGL backend through `RHICommandContext` for buffers, images, samplers, queries, fences, transform
feedback, semaphores, memory objects, framebuffers, vertex arrays and executable objects. Private
initialization receives the admitted context explicitly and dispatches through `IRHIResourceExecution`,
without retrieving the concrete backend or reentering a public factory. A nullable metadata result reports detected failure;
identity and a complete initial snapshot are handed to the wrapper only after successful initialization.
Semaphore generation instead hands off an owned reservation: its empty snapshot becomes available only
after payload realization is established, and wait/signal reject an unimported reservation.
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

## Snapshot and change publication

Snapshot assignment and meaningful change delivery are separate manager responsibilities.
GpuResourceMetadataEntry<T> compares a candidate with its last committed facts and prior published
availability. Temporary operation exclusion withholds usability during dispatch; a successful equal
completion does not produce an unavailable/available pair. Initial facts are distinguished from lifecycle
revision changes by HasCommittedMetadata. Newly associated or missing recipients still receive the
complete snapshot when republication is equal.

Canonical record equality compares values. GpuTextureMetadata images compare by mip/face,
GpuFramebufferMetadata attachments by slot and original backing registration, and GpuProgramMetadata
resources by interface/index and unit assignments by location. Immutable allocation and enumeration
order are irrelevant; hashing follows the same rules. Default/unavailable collections remain distinct
from known empty collections. Backing associations held outside those structs participate separately
in classification using their original registration and storage certificate.

The internal RHIResourceChange carries ResourceChangeKind reasons, resulting availability, existing
metadata/storage revisions and the initiating owner. Metadata means represented facts or original
backing selection changed; Storage and Executable preserve established replacement effects even when
descriptors or interfaces compare equal. Availability covers published withdrawal/recovery; Retired is
terminal for the original lifetime. MetadataRevision advances once per actual published transition,
including availability and retirement. StorageRevision advances only for established or conservatively
recovered storage replacement. Generic metadata-effect flags alone do not prove change. Uncertain
storage/executable effects survive explicit recovery; content writes and GPU completion are separate.

RHIResourceManager.ChangeDelivery batches only one synchronous completion and its dependency chain:
canonical facts, revisions, original backing associations, wrapper snapshots and all selected-image
projections are committed before consumer callbacks. MetadataOperation finalizes its committed result
before delivery while retaining its authorized exclusion through callbacks. OnResourceChanged is the
single internal resource callback; wrappers without derived work need no handler. There is no public
event bus, asynchronous notification service or second context-local registry.

All fourteen registered kinds use that contract: Buffer, Texture, Renderbuffer, Framebuffer, Shader,
Program, VertexArray, TransformFeedback, Sampler, ProgramPipeline, Query, Sync, Semaphore and MemoryObject.
GpuImageMetadata is a projection. Empty schemas report realization, availability and retirement without
mirroring backend configuration, query readiness, synchronization state or shader compilation results.
VAO import and realization receipts use the existing backend configuration epoch independently of the
canonical metadata revision, so equal empty snapshots cannot hide actual vertex configuration changes.

Buffer/renderbuffer-backed textures and native views retain original dependencies. Source withdrawal
withholds dependent usability; source recovery restores only source-caused uncertainty and never an
independent child failure or invalidated fixed view storage certificate. Detaching a backing removes
its obsolete association. OwnerImages preserves weak local-owner/replacement relationships, projects
all affected images before callbacks and skips recipients already served by their canonical entry.
A failed projection does not prevent attempts for other valid recipients. Local completion uses the
existing manager-written snapshot/effect evidence, without granting it tracked usability authority.

Framebuffer attachment helpers and program preparation react through the same callback. Explicit
framebuffer resize/rebuild/import events remain owner completions even for identical selections.
The initiating owner is excluded from duplicate metadata-driven completion, while aliases still
receive their resource changes. Dependency fanout to an owner is coalesced within one completed chain;
explicit owner completions are not suppressed by this coalescing.

Callbacks may inspect committed facts; managed same-resource reentry remains guarded. Before each
remaining delivery, original association, revision and availability are checked so an intercepted
reentrant mutation or retirement cannot be overwritten by an obsolete result. Other valid recipients
are attempted after consumer failure. Successful-completion callback errors are reported after commit,
without undoing allocation, retrying native work or treating it as failed initialization. Failure paths
preserve the original operation exception; external observation contains its own exceptions.

Retirement immediately closes usability and captures weak recipients before membership is cleared.
Publish, withdrawal and terminal capture share entry synchronization; terminal delivery is idempotent
across scheduled disposal, native deletion and context teardown. Worker disposal captures and queues
through the existing originating cleanup lifecycle; context-affine callbacks do not run on that worker.
Context retirement withdraws the affected registrations before callbacks. Original lifetime checks
prevent notification or native cleanup from targeting a recycled name or replacement context.

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
attachment slot never acquire or recover facts. Imported images with supplied canonical metadata use
the same RHI entry but project through the manager without native admission. Tracked images associate
only with available original canonical facts; supplied snapshots cannot recover or override an unavailable
entry. Missing covered creation entries are tracking defects. Initial discovery requires either an identity
explicitly recorded before observation activation or the narrow EXT named implicit-creation cause described
below. Failed discovery and other unavailable original facts require explicit recovery.
For external-memory-backed texture allocation, a known-mutable texture receives one immutable-storage
flag read to establish whether the opaque imported payload was accepted. Allocation dimensions, format
and levels still come from the intercepted command.

Imported framebuffer publication composes maintained descriptors and backing images under one metadata
operation. It does not discover a known framebuffer or reread its backing storage. The manager preserves face, layer, view range and observed attachment-mode evidence;
the framebuffer owns its retained image composition. Identity replacement, availability withdrawal and
failure notification belong to that explicit transaction. A completed publication emits one owner event;
a failed publication withdraws old metadata and retained composition while preserving borrowed storage.
Repeated known attachment definitions retain dependent revisions when all image selections, original
backing registrations and attachmentless defaults are unchanged. Actual changes, unavailability and
recovery still invalidate dependents. Explicit imported framebuffer publication remains an owner event
even when the selected images are identical, so resize/rebuild publication is not lost.
Each maintained attachment descriptor retains its original backing registration, so a recycled native
name cannot silently select a different texture or renderbuffer. When a bound framebuffer's backing is
deleted, its automatically detached slots are removed from the descriptor; an unbound framebuffer retains
its original selection and image projection requires that original backing to remain available.

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
facts; only recorded pre-installation or supported EXT implicit-creation discovery and explicit
original-entry recovery use manager-owned readers.

Driver-produced shader compilation/link results and interfaces retain their completion checks. Allocation
uses cached target/format sample capabilities, with a single sample-count read when those capabilities
cannot supply the result. Unsized image formats may require a resolved-format read. These exceptions do
not authorize full descriptor scans after controlled allocation. Native readers use core named queries; cube-face descriptors retain their required bound selection
and restoration; optional renderbuffer-backed association queries retain their required selectors.
Buffer readers use core named queries only for a pre-install identity recorded before observation
activation, consumed once, or explicit recovery of an existing unavailable original allocation. Missing
covered entries and reserved names do not authorize discovery. Buffer allocation observations publish
supplied storage facts after classifying rejection; content updates preserve descriptor revisions. Uncertain initialization uses the existing pending entry
effect; a later definite bind cannot fabricate empty defaults after an intervening potentially accepted
allocation. Explicit recovery remains separate from ordinary association.
Memory objects retain their empty schema across imports; opaque payload validity is not inferred from
an import handle. An unresolved imported-buffer storage outcome stays unavailable without a query.
The one manager is established before observation and later attaches renderer events to that same
service. Reload retains its identities; context retirement establishes a replacement service. Context-local
RHI initialization retains supplied snapshots without a no-manager discovery fallback.

An operation captures its original identity, withdraws affected availability, constructs a complete result
and commits once. Participating internal steps receive the operation explicitly. Unrelated same-resource
reentry is rejected before dispatch. Cleanup does not recover or retry mutations; recovery is explicit.
Deferred program linking retains its separate submission/completion boundary. Pending link records
are held by the backend against the original program wrapper, association and metadata revision;
submission supersedes only that wrapper's previous request, and completion consumes it once. Canonical facts,
availability and wrapper snapshots are committed before dependent notification. Selected framebuffer images project backing metadata without
another canonical entry or reader when facts are available.

Execution exclusion is separate from canonical publication. The manager's `MetadataOperation<T>`
retains its original resource/entry, accumulated effects, availability and exactly-once commit policy.
It holds only `IRHIResourceOperation`, a private RHI exclusion with original identity, interruption and
observed-deletion state. `IRHIResourceOperationExecution` is implemented by the existing backend cache;
the thin context composition root selects that same authority without constructing or admitting a native
backend. Registry-only deletion, reentry checks and exceptional unwind therefore require no GPU queries
or fresh native admission. Disposing the exclusion releases its originating cache bookkeeping, never
publishes metadata or performs recovery. Registered generations isolate operations; admitted local tools
also distinguish physical contexts. Owner context checks run before availability withdrawal and again
before participating dispatch/commit. Physical discovery is confined to `RenderContextRegistry`.

## Maintained image facts

Native texture, renderbuffer and framebuffer generation reserves original identities. Typed creation and
legal realization establish explicit native defaults. Texture targets are fixed at realization; image
collections start empty. Renderbuffers start with zero extent/samples and Rgba format; framebuffers
start with no attachments, zero attachmentless extent/samples and fixed sample locations enabled.
Framebuffer zero remains the window surface. Backend factories use saved originals and publish through
the private manager handoff once, without external callback registration.

Image definitions retain supplied dimensions, sized formats and storage mode. Immutable mips and mutable
mip generation derive per-level geometry; array layers remain constant while 3D depth shrinks. Content
updates and regeneration of existing immutable images do not renew descriptor/storage revisions. Base/max
setters publish converted inputs. Native observers use cached capabilities without discovering missing
format capabilities. Narrow allocation outcomes include an unsized resolved format, the approved
sample-count fallback, and the immutable-storage flag for an opaque external-memory allocation. The
last case does not authorize a descriptor scan.

Texture clears dispatch the supplied pixel encoding and value over the exact requested region.
They change contents, not allocation metadata, and emit no resource-change notification.

Native views own separate registrations and retain original source/storage dependencies regardless of
which wrapper later borrows them. Cumulative view ranges supply feedback/overlap coordinates, while
binding coordinates remain relative to the view. Source replacement, withdrawal and retirement do not
retarget old views. Buffer and renderbuffer texture associations resolve available original backing
entries without publication-triggered acquisition; whole-buffer mode retains its size-zero convention.

Framebuffer observations publish individual attachment/subresource/default changes, including cube faces,
layers and packed depth/stencil slots. Read and draw routes stay in the existing execution cache.
Image projection and covered borrowing consume these maintained descriptors without native rediscovery.
Explicit pre-install discovery and original-entry recovery remain separate operations.
Before interception starts, the engine integration records framebuffer and texture identities exposed by
the client's framebuffer references. Other pre-existing objects require their own established provenance;
an unrecorded texture name does not acquire an empty descriptor merely because it is bound.
An unknown identity encountered through a supported EXT named implicit-creation operation is recorded
as unavailable with a distinct initial-discovery cause. The observer neither guesses fresh defaults nor
queries descriptors. Explicit adoption may discover its required metadata once, identifying the actual
texture target rather than trusting the EXT command's target. Later consumers reuse the canonical result;
failed discovery requires explicit recovery and is not rearmed by repeated EXT calls. Tracked generated
reservations continue to realize and update from supplied facts without descriptor reads.
For an unknown EXT buffer texture, a validated association request retains its range mode, native-width
offset/size and original backing in that same unavailable entry. Initial discovery verifies a matching
nonzero native buffer association, establishing that the object is a buffer texture before publishing these facts.
Detached or unproven associations remain unavailable. It does not infer whole-buffer mode from a queried
range or narrow native-width values through integer getters; an unobserved association remains unavailable.
A later uncertain association discards those unpublished facts so discovery cannot certify an earlier backing.

## Shader executable ownership

Native CreateShader/Core/ARB results publish the supplied fixed stage without querying it. Fresh
programs publish a complete empty unlinked interface. Pipeline Gen retains an unavailable reservation,
while Create and specified binding/configuration/validation/getter boundaries realize its empty schema.
Already-live factory outputs never replace the original allocation. Native creation and owned RHI
creation have separate once-only publication paths; backend factories use saved-original dispatch.

Linking, combined shader-program creation and binary installation obtain explicit driver-produced
executable/interface output. External parallel linking may remain unavailable until a caller obtains
link-status or positive completion output; managed pending requests retain their own original identity
and completion boundary. Unit/block assignments patch supplied values without reflection or getters.
Numeric/ignored writes and proven rejected assignments preserve existing facts. Unavailable snapshots
are never repaired by assignments or preparation; adopted covered programs reuse maintained facts.
Initial discovery requires recorded pre-install provenance. Context-local shader candidates supply their
initial and completed snapshots; absence of a manager does not authorize raw-ID reflection.

Shader asset selection, digest validation, permutations and binary-cache policy live in
`ShaderCompilation`. Frozen `RHIShaderDescription` inputs specify stage code, entry points,
specializations and authored bindings. The backend creates stages/programs through the manager, links
or installs a selected binary, publishes one canonical executable snapshot and prepares its interface.
SPIR-V input admission checks the selected entry-point name and execution model before native ingestion;
this declaration check does not reflect resource bindings or replace native compiler diagnostics.
Canonical shader metadata and initialization use `ShaderStage`. Program resource snapshots use
`ProgramResourceKind`, `ShaderValueType`, nullable typed location/extent/block fields and `ShaderStageMask`;
missing fields remain unavailable. Native reflection selectors and tokens are decoded inside the backend.
Prepared tables consume these facts and supplied limits without queries or context admission.
External authored assignments resolve against canonical facts through a private RHI operation, skip
unchanged values and publish through the existing observation path; ordinary layout reads use snapshots.
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
contains capacity, semantic `BufferUsage`, immutable-storage status and `StorageAccess` flags; content operations never
publish another storage snapshot. Partial initial bytes use direct subdata when permitted, otherwise a
temporary staging copy. Native deferred destruction retains that staging storage until the copy completes.
No alternate execution path is retried after failure.

`GpuBufferRef` privately retains the typed resource and exposes only published metadata and managed
identity. It grants no lifecycle or storage-replacement authority. Backend use validates original context
and manager association before examining binding suppression. Generated native callbacks forward external
buffer calls exactly once, preserving original arguments and native errors. Cache-owned post-call effects in
`Rendering/RHI/Backends/OpenGL/State/Observation/Native/Buffers` maintain established facts or withdraw
uncertain knowledge; they never apply managed omission or suppression rules. Managed backend execution
uses saved-original driver calls and publishes through its existing operation owner.

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

Cache-owned native image observations reconcile texture, buffer-texture and renderbuffer effects;
sampler and image deletion observations share the existing retirement authority. Generated native
callbacks preserve arguments and errors, dispatch once and invoke the existing publication services.
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

Raw query generation reserves the original name; legal begin/indexed-begin/counter operations and named
creation establish its empty metadata without discovery. Core and ARB aliases share that registration.
The cache owns target/activity and issued-result revisions, separately from empty canonical descriptors.
An external reissue invalidates a managed result ticket without revising descriptor metadata. Missing
cached optional support or stream limits leave uncertain realization unavailable; they do not authorize
target/status discovery. Explicit positive caller `IsQuery` results can establish an observed reservation.
Fences register their nonzero pointer-width result once; creation does not read signal status. Covered
query/fence/semaphore association and empty-schema recovery never invoke a descriptor reader.

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

Semaphore generation retains a reserved name on the same manager entry. An unimported owner can be
disposed, but cannot wait or signal. Opaque native imports preserve identity without claiming success
or querying payloads. The RHI import boundary makes one targeted `IsSemaphore` outcome check before
publishing the empty realized snapshot; failed import cleans the candidate against its original identity.
An explicit external positive `IsSemaphore` result also establishes realization. FD ownership transfers
only on native success; Win32 handles/names remain caller-owned. Repeated setup does not create another
registration or descriptor revision. Managed factories/imports/deletion use saved originals so external
observation and private publication cannot run twice.

Sparse buffer allocation uses explicit sparse storage intent and cached page alignment. It rejects
mapping and initial bytes before commitment; commitment changes residency, not allocation metadata.
Typed sparse-image commitment supports explicitly configured native integration storage without inventing
an allocation descriptor or querying unrepresented sparse page facts on each call. Exact native error
behavior remains authoritative for those integration facts.

`GpuSyncRef`, `GpuQueryRef`, `GpuTransformFeedbackRef`, `GpuSemaphoreRef` and `GpuMemoryObjectRef`
retain existing owners through wrapper-owned conversions. The manager certifies published usability;
references expose no disposal, handle-transfer or native execution methods. Native auxiliary deletion
observations preserve per-name outcomes after original dispatch and use shared retirement publication. Managed deletion uses the admitted backend after original-context queue admission.

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

The manager admits engine geometry from its original array/buffer identities and supplied layout,
stream ranges and index declaration. Admission validates those declarations without scanning native
attribute configuration or repairing the engine array. The backend creates a private VAO from the
retained declaration when drawing. The import receipt retains original association, metadata revision,
context and selected-array configuration epoch; external changes expire it without descriptor queries.
Unrelated arrays do not expire the receipt. Borrow lifetime and engine layout policy remain with the
integration adapter. No current external producer supplies a certificate permitting direct native-array
reuse, so imports conservatively use private realization.

Vertex arrays, transform-feedback objects and samplers keep empty canonical schemas. Their generated
names remain reservations until specified use; core Create operations realize immediately. Sampler
parameter/getter/IsSampler use initializes a known generated sampler without parameter discovery.
Vertex configuration classifies supplied argument rejection before revising original certificates and
retains established element-buffer associations. Feedback output changes revise their target object;
selection and activity remain executed knowledge. External Begin/Resume can require a targeted active/
paused outcome read because linked capture-output configuration is deliberately not mirrored. End/Pause
reuse established history. Neither an empty schema nor an external wrapper authorizes descriptor reads.

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
can be suppressed. Exact external calls preserve execution and native error behavior. Established
object-local draw routes, pixel-store fields, represented fixed state and established bindings propagate
from legal arguments. Generic/indexed buffer aliases and selected-VAO element bindings share the existing
selector authority. Unknown execution selectors retain targeted restoration reads; missing covered resource
entries never authorize descriptor discovery. Unspecified conversion outcomes withdraw affected certification.
Required selector-restoration scopes preserve separate read/draw framebuffer selections and original VAO
element-buffer associations. Geometry retains authored fields and original configuration revisions; the canonical VAO schema contains no full layout mirror.
Restoration scopes retain their original physical context lifetime. Disposing an old scope in another
admitted context neither restores its numeric names there nor discards that context's known selectors.
Context admission abandons the previous lifetime's scope-stack bookkeeping.

Deletion removes the original entry and invalidates its recipients. Reusing the numeric ID creates a new
entry; old wrappers and queued work retain their original association and cannot affect the replacement.
Owner retirement withdraws usability before deferred deletion. Queue admission retains original context
and registration identity; immediate, local-tool and queued retirement all reach the same internal context
operation and backend kind dispatcher. Borrowed disposal only removes its recipient;
it never deletes external storage. Context retirement clears registrations and abandons work that cannot
run in its originating context. Weak recipients are pruned during acquisition, publication and removal.

## External coverage and consumers

Selected native entries in both `OpenTK.Graphics.OpenGL.GL` and `OpenTK.Graphics.OpenGL4.GL`
binding tables have generated ABI-compatible callbacks. Supported Core/Ext/Arb operations reach the
same cache-owned post-call observations regardless of caller assembly, generic element type, delegate
or dynamically generated caller. OpenTK retains its managed argument marshalling. The callback forwards
the original native operation once, then contains observation failures without changing native outputs
or errors. Other auxiliary owners, legacy fixed-function facilities, other graphics libraries and
independently cached driver pointers remain outside this boundary. Registry membership alone cannot
repair missing observation, and routine purges must not compensate for a coverage defect.

Cloud and depth consumers retain typed borrowers across stable selections and share existing owned material
depth directly. Their content readiness, upload/generation certificates, frame qualification and producer
selection remain separate from resource metadata. Raw sampler IDs require allocation-identity admission;
numeric equality cannot substitute for a retained original association. See [shader bindings](Rendering.ShaderBindingState.md#prepared-resource-submission)
and [cloud ownership](PBR.Clouds.md#borrowed-map-contract) for consumer contracts.

## Backend state ownership

`OpenGlStateCache` is internal to `Rendering/RHI/Backends/OpenGL/State`. It owns the existing thread-local
executed knowledge, independent known-field masks, original-context selection, binding/routing keys,
operation guards and selective restoration. Its Bindings, FixedFunction, Observation and Boundaries
modules separate those responsibilities. It has no resource registry, native allocation API or arbitrary
callback execution API. Manager operation services consult guards without issuing native work.

Ordinary owners and managed geometry enter through `RHICommandContext`. Registration, published fact
access, dependencies and queued lifetime handling remain with the existing manager; that collaboration
is not native execution. Geometry preparation and client reload/shutdown use the context's preparation
gate without constructing a backend or querying native state.

Engine fullscreen geometry is an explicit native-owner adapter: it owns the engine mesh, validates its
declared streams and supplies `RHIGeometryInputs`. Its restricted upload/deletion callbacks admit the
original context through backend integration, including deferred mesh cleanup. They expose no native
draw executor to renderers. Upload selector scopes restore known state on failure as well as success;
the adapter performs no post-upload purge. Engine liquid geometry borrows engine storage through the
same authored-input contract. Actual context retirement abandons the existing restoration owner before
client cleanup; it neither restores into a replacement context nor creates another state cache.

Admitted managed fixed-state transitions use the stateless `OpenGLStateEffects` modules to issue core
4.5 native effects. These modules hold no knowledge, suppression, metadata or alternative dispatch path.
The cache withholds the affected known bit before dispatch and commits it only after the effect succeeds.
Managed viewport, scissor, depth-range and scissor-enable effects address viewport zero explicitly, preserving
other viewport indices during both execution and restoration. Exact external global calls retain their native
all-viewport effects; the cache represents their viewport-zero result.
Unknown incoming selectors and selective restoration facts are queried by the cache's native observation
services. Baseline independent blending, clip control, sample shading and fixed-index restart have no
older-context fallback. Existing limits and profile distinctions remain authoritative.

External fixed-state and resource effects are reconciled by cache-owned methods under
`State/Observation/Native`. `InterceptOpenGL` attributes select native symbols; ABI-identical aliases
share one handler. Generated callbacks dispatch before observation, without equality suppression or
managed admission vetoes. Legal represented effects update only affected fields; proven rejections
preserve prior facts, while uncertain effects withdraw only affected knowledge. Context checks prevent
completion from changing a foreign cache. Initial capability discovery occurs explicitly outside native
callbacks; observations without an admitted capability snapshot leave the context's facts unknown
without draining native errors.

Backend execution uses generated `OpenGLDriverCalls` overloads to invoke saved original pointers,
bypassing external observation while retaining its own admission, suppression and manager publication.
Shared effect logic never dispatches an external operation a second time. Native callbacks expose no
begin/complete receipt; controlled operations own actual execution and cleanup synchronously.

Pixel-store observations retain legal conversion effects in independent field knowledge; complete
layout acquisition reads only missing fields. Draw-route observations retain established object
identity and publish legal complete routes using cached surface facts; read routing has no mirrored
state. Immutable framebuffer declarations constrain managed configuration, while executed routing
knowledge follows external changes and controls restoration and suppression. Routine frame advancement,
resize callbacks, attachment publication and wrapper detach do not purge executed knowledge.

## Resource diagnostics

Resource wrappers retain their managed diagnostic names and submit optional labels through
`RHICommandContext.TryLabelResource`. The private execution contract receives the existing owner,
not a native object kind or numeric identity. The backend checks the originating context and live
manager association before translating the owner identity. Labeling does not acquire metadata,
change bindings, establish creation success or transfer ownership. It performs no native validity
probe and does not consume pending errors. Label failure remains best-effort.

Native labels and debug notification control compile out of Release. The OpenGL diagnostic
implementation lives in `Rendering/RHI/Backends/OpenGL/Diagnostics`, split into labels, groups,
notification control and explicit error assertions. Pre-admission capability error checks retain the
engine binding already loaded by the host; admitted owner labels use OpenGL4. Engine shader/framebuffer/mesh label helpers live
in its integration directory and consume only the engine's current allocation identities. They retain
borrowed ownership and allocation-time naming policy. Portable renderers do not pass native selectors.

Explicit renderer diagnostic assertions and optional groups enter through the RHI diagnostic methods.
These assertions preserve the existing requested checks; ordinary resource commands do not invoke an
automatic error drain. Failed patched engine compilation retains one bounded error report through
`OpenGLRHIContext.ReportFailedEngineCompilation` before its existing single-program recovery. Pending
errors are diagnostic information, never a success certificate. Shader-layout lookup uses supplied
metadata and contains no native error scope.
