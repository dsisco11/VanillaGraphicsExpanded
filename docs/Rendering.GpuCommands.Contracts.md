# RHI implementation contracts

Planning companion to [the approved proposal](Rendering.GpuCommands.Proposal.md) and
[implementation plan](Rendering.GpuCommands.todo). These define the implemented execution and ownership
boundaries. Qualification receipts and outstanding acceptance belong to the implementation plan.
No throwing placeholder, old-executor forwarding facade or duplicate owner is part of this contract.

## Authority and ownership

Complete interception of supported calls through the installed OpenTK binding tables is a required
architectural invariant, irrespective of project, engine or other-mod caller assembly. Direct calls
through other libraries or independently cached driver pointers are outside that boundary. Missing
interception/effect propagation is a correctness bug, not justification for routine purging/recapture.
Initially unknown facts, actual context identity changes and uncertain outcomes retain their existing
acquisition/invalidation rules. This is the final contract, not current coverage qualification.

The approved binding-interception proposal (`92c5f18b:docs/Rendering.OpenTKBindingInterception.Proposal.md`) controls
the native-route replacement. External callbacks always forward original native dispatch once and
preserve arguments, return/output values and native errors. They never suppress, veto, cancel, retry,
throw into the caller or defer an observer exception to it. Adapt old managed observation guards into
non-vetoing effect/interruption bookkeeping; contain failures and withdraw only uncertain affected facts.
Scalar state delivery is generated from compiler-checked withdrawal methods and optional classifiers.
`InterceptOpenGL.Withdraw` names a static void method receiving the selected cache and exact native
arguments. The cache domain owns affected-field selection, including argument-dependent targets;
withdrawal performs no native execution or acquisition. The generator owns delivery safety without
knowing cache knowledge categories. Native acceptance rules remain handwritten. Proven rejection retains
old facts, uncertainty withdraws only affected facts, and publication/recovery checks the originating lifetime. Context selection
uses explicit nonthrowing absence, independently of capability discovery. Numeric uniform writes and
ignored locations do not interrupt metadata certification or refresh descriptors.
Managed RHI validation remains separate. Backend-owned execution bypasses callbacks and retains its
single reconciliation/publication owner. The cleanup unit precedes the native replacement; only the
latter qualifies universal supported-caller delivery, observation-only forwarding and remaining handlers.

The proposal controls semantics. Existing [resource](Rendering.GpuResources.md) and
[binding](Rendering.ShaderBindingState.md#prepared-resource-submission) contracts control retained
metadata, ownership and publication behavior. The creation/publication proposal at
`3f307365:docs/Rendering.CommandPublication.Proposal.md` controls private initialization and operation
composition; `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md` controls the unchanged texture value.
The new public creation boundary supersedes recipient-bearing public creation, not private handoff.
Older DSA alternatives and measurement requirements in those documents do not override this proposal's
4.5 minimum or its explicit exemption from performance measurement.

| Type/service | Final contract |
| --- | --- |
| `RHICommandContext` | Context/thread-affine immediate factories and semantic operations, pass sequencing, graphics/compute selection, binding publication, boundary admission and synchronous list submission. Entirely replaces `GraphicsCommandContext`. Reuses specialized helpers; no duplicate submission state. |
| `IRHIBackend` | Internal backend-neutral execution contract consumed by the context. Focused operation-family interface partials; one OpenGL implementation, no version strategies or alternate executor. Manager-owned readers use backend query services. |
| `RHICapabilities` | Immutable semantic projection of existing cached context limits, format/sample capabilities and optional features; no second discovery system. OpenGL admission checks 4.5 and required entry points once. |
| `RHICreationResult<TResource>` | Discriminated ready owned resource or failure diagnostics, never both. No default-success, nullable metadata, provisional identity or publicly supplied recipient. Preserve required exceptions; no universal silent-error detection. |
| `RHICommand` | Immutable opcode and typed-payload location. Managed references live in list-owned tables; headers own no execution, metadata or deletion authority. |
| `RHICommandList` | Exclusive list-local recording, copied/frozen arguments, intended context identity, closing, terminal state and reset. Native execution only through `RHICommandContext.Submit`. |
| `OpenGlStateCache` | Existing executed knowledge, eligible suppression, selective restoration and effect forwarding. Rename only after native execution leaves `StateCache`; no old-name alias. |
| `RHIResourceManager` | Existing associations, complete canonical snapshots, availability/revisions, dependency validation, operations and retirement. No new registry, handle generation field, registration identity, recipient membership or revision authority in `GpuResource<TMetadata>`. |
| `RHIResourceManagerSystem` / `RHIResourceDisposalQueue` | Publish the single manager and retain cleanup for its originating lifetime; no replacement execution or registry authority. |
| `RHIUniformRingSystem` / `RHIUniformRingFrameController` | Expose the current `GpuUniformRingBuffer` allocator and coordinate publication epochs through the existing begin/end engine callbacks. Storage ownership and allocation records stay unchanged. |

`IRHIBackend` methods mirror semantic operation inputs below, not OpenTK overload names. Its internal
creation orchestration can accept unpublished typed recipients and explicit operation state. Native
calls and exact external forwarding have separate OpenGL modules; neither calls an old cache executor.
The typed snapshot container may retain manager-written local snapshot presence and uncertain-effect evidence
for context-local completion and explicit recovery. It cannot certify tracked usability: original registration,
canonical availability, revisions and all transitions remain manager-owned. It performs no acquisition or notification itself.

The context does not publish metadata itself. The backend consults the cache, executes and reconciles;
existing manager services commit snapshots and notifications exactly once.

### Resource completion

The manager separates complete snapshot assignment from meaningful change delivery through the internal
RHIResourceChange/ResourceChangeKind contract described in [resource publication](Rendering.GpuResources.md#snapshot-and-change-publication).
Equal canonical values cause no event or revision increment; missing recipients still receive snapshots.
Structural equality includes represented values and original backing identity, with matching hashing;
default collections differ from known empty values. Established Storage/Executable replacement remains
observable even with equal descriptors/interfaces. MetadataRevision advances once for a published
metadata, storage, executable, availability or terminal transition; StorageRevision tracks replacement.
Operation exclusion alone does not constitute published unavailability.

Canonical facts, original dependencies and every affected snapshot/projection precede callbacks.
Successful operations are finalized before notification and remain protected by their original exclusion.
All families use one internal callback, including empty-schema lifecycle transitions. Owner-level
framebuffer completion remains explicit; origin suppression prevents its duplicate metadata reaction.
Weak owner/image fallback covers local completion and deliberate owner replacement without retargeting
borrowers/views. Independent child uncertainty cannot be recovered by a source-only recovery.

Delivery rechecks original identity, revision and availability after reentry, attempts remaining valid
recipients after consumer failure, and cannot roll back or retry committed native work. Failure unwind
preserves its original error; external observations contain notification failures. Retirement closes
usability immediately and delivers one terminal transition for the original lifetime. Worker disposal
uses the existing context-affine cleanup handoff; teardown never enters a replacement context.
No additional queries, public event bus, registry or generation token participates in classification.

Resource dependency lookup uses a manager-owned Graffs mutable directed graph over original registration
references. It is derived adjacency data, not another metadata or lifetime authority. Relationship changes
update only their incident edges; fanout uses stable direct-neighborhood snapshots rather than registry
enumeration. A weak unresolved-dependent subset preserves exceptional deletion handling without granting
discovery or adopting reused names. Retirement retains edges required for deferred native deletion;
final deletion and context teardown release obsolete lookup retention.

### Internal resource execution

The context routes private resource work through the internal `IRHIResourceExecution` contract inherited
by `IRHIBackend`. Its creation, acquisition/observation and retirement partials are not public consumer
operations. Initialization records, nullable metadata and native identities remain inside this handoff.
Existing owner allocation/configuration records also cross this private handoff. Image and framebuffer
canonical metadata, initialization records, wrappers and capability-cache keys use project-owned texture dimensions, cube faces, formats and framebuffer slots end to end. Direct
query selectors use metadata fields and program-block kinds. Routing carries color indices
(minus one disables an output) and an optional read-color index.
No OpenTK enum occurs in RHICommandContext or IRHIResourceExecution signatures or the migrated buffer,
shader/program and image/framebuffer payload schemas, including collection keys. Buffer facts use
BufferUsage/StorageAccess; stage facts use ShaderStage. Program resources contain ProgramResourceKind,
ShaderValueType, nullable typed location/extent/block fields and ShaderStageMask, without native property
bags or raw native type identifiers. Authored ShaderResourceType values carry no native token identity.
Native adapters decode actual external arguments; backend execution encodes semantic
inputs and decodes native query results. The codecs live inside the backend. Ordinary resource wrappers, manager
acquisition services and capability caches do not call native translation helpers.
Query-free image description/encoding projections translate published canonical facts only; they never
acquire or recover metadata. Native window discovery and optional bindless handle issuance/residency
are private owner services. The manager receives the admitted context explicitly for initialization; it never retrieves or stores
an OpenGL backend. Typed context methods invoke the contract; no service-locator property or second
dispatch registry is exposed. Public factories are not called again from private initialization.

Manager-owned readers admit discovery only for recorded pre-installation or supported EXT implicit-creation
provenance, and explicit recovery of an unavailable original entry. Missing covered entries and context-local
tools do not grant discovery. Backend query implementations execute those authorized reads and explicit
driver-produced outputs. Published snapshot access
never calls these services. Cached limits come from `RHICapabilities`; there is no separate discovery path.
Retirement reaches one backend kind dispatcher only after existing owner/queue original-context and
association admission. Candidate cleanup uses the same route, preserving original errors and identities.

Private import operations also admit borrowed textures, executable adoption, interface acquisition and
selected framebuffer images. Imported framebuffer publication keeps its operation guard through maintained descriptor/backing-image
composition; it does not rediscover a known framebuffer. Image validation reads supplied facts only. Supplied imported-image projection enters the private RHI
gate without native admission, associates available canonical facts or projects supplied local facts,
and never acquires or recovers a tracked backing. Unknown-fact acquisition retains native admission. Manager entry delivery
is authoritative for registered images; weak owner/image relationships cover local tools and intentional
owner replacement and skip already-delivered entry recipients. Dependency notification failure does not
convert a successfully committed replacement allocation into a failed candidate.

Imported geometry admission validates supplied layout/streams and original allocation ranges through
this contract without scanning external VAO attributes. The backend realizes a private array from those
declarations. A transient `RHIGeometryContext` supplies originating namespace and selected-array revision;
the manager checks an import receipt without querying or recertifying native layout. External configuration
changes expire the original receipt. Integration adapters retain engine ownership/layout and borrowing
policy, not native execution authority. No current external producer supplies a direct-array reuse certificate.
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

## Resource references and factories

Consumer values retain one existing typed resource, with private construction supplied by its wrapper.
They expose absence/usability and published facts where needed, never public underlying-resource,
disposal, detach, resize, mapping or mutation APIs. Creation/conversion never queries, registers, acquires
guards or transfers ownership. Metadata access never performs recovery. Resolve original associations
through the manager on use; equality alone cannot establish current validity or suppress a binding.

| Reference | Existing resource / canonical metadata | Factory result / description |
| --- | --- | --- |
| `GpuTextureRef` (unchanged) | `GpuTexture` including `BorrowedTexture`, and `GpuTextureView`; `GpuTextureMetadata`. Buffer textures use that same typed base and receive a wrapper-owned conversion during B. | `CreateTexture(RHITextureDescription, RHITextureInitialData?)` → `RHICreationResult<GpuTexture>`; dimension-specific factories return the concrete existing owner where required. `CreateBufferTexture(RHIBufferTextureDescription)` → `RHICreationResult<GpuBufferTexture>`; `CreateTextureView(RHITextureViewDescription)` → `RHICreationResult<GpuTextureView>`. |
| `GpuBufferRef` | `GpuBufferObject` and VBO/EBO/UBO/SSBO/atomic/indirect/pack/unpack subclasses; `GpuBufferMetadata` | `CreateBuffer(RHIBufferDescription, RHIBufferInitialData?)` → `RHICreationResult<GpuBufferObject>` with the concrete owner selected by role. Typed `CreateVertexBuffer`, `CreateIndexBuffer`, `CreateUniformBuffer`, `CreateShaderStorageBuffer`, `CreateAtomicCounterBuffer`, `CreateIndirectBuffer`, `CreatePixelPackBuffer`, `CreatePixelUnpackBuffer` return the respective existing owner through the same transaction. No parallel generic owner. |
| `GpuRenderbufferRef` | `GpuRenderbuffer`; `GpuRenderbufferMetadata` | `CreateRenderbuffer(RHIRenderbufferDescription)` → `GpuRenderbuffer`. |
| `GpuFramebufferRef` | `GpuFramebuffer` including dynamic/static or external wraps; `GpuFramebufferMetadata` | `CreateFramebuffer(RHIFramebufferDescription)` → existing owned framebuffer, for backend realization or explicit OpenGL integration only. Portable passes use attachments. |
| `GpuSamplerRef` | `GpuSampler`; `GpuSamplerMetadata` | `CreateSampler(RHISamplerDescription)` → `GpuSampler`. |
| `GpuVertexArrayRef` | `GpuVao`; `GpuVertexArrayMetadata` | `CreateVertexArray(RHIVertexArrayDescription)` → `GpuVao`; ordinary draws supply geometry, backend realizes vertex input. |
| `GpuQueryRef` | `GpuQuery`; `GpuQueryMetadata` | `CreateQuery(RHIQueryDescription)` → `GpuQuery`; description declares query kind before its use. |
| `GpuSyncRef` | `GpuFence`; `GpuSyncMetadata` | No general deferred factory: immediate `InsertFence()` yields an owned `GpuFence`; recorded markers yield a completion ticket. |
| `GpuTransformFeedbackRef` | `GpuTransformFeedback`; `GpuTransformFeedbackMetadata` | `CreateTransformFeedback(RHITransformFeedbackDescription)` → `GpuTransformFeedback`. |
| `GpuSemaphoreRef` | `GpuSemaphore`; `GpuSemaphoreMetadata` | `CreateSemaphore(RHISemaphoreDescription)` → `GpuSemaphore`; import is explicit, capability checked. |
| `GpuMemoryObjectRef` | `GpuMemoryObject`; `GpuMemoryObjectMetadata` | `CreateMemoryObject(RHIMemoryObjectDescription)` → `GpuMemoryObject`; import ownership is explicit. |
| Private native executable refs only | `GpuShaderModule` / `GpuShaderMetadata`, `GpuProgramObject` / `GpuProgramMetadata`, `GpuProgramPipeline` / `GpuProgramPipelineMetadata` | Private backend creation/retirement inside shader realization and external integration; no portable native-stage/program factory. Public shader factory returns `RHIShader`. Compute asset policy composes a shader and managed PSO rather than adding another native program wrapper. |

Every `Create*` result abbreviated to an owner `T` in this table means `RHICreationResult<T>`,
not a second bare-owner/exception-only creation pattern. Fence insertion retains its distinct sync contract.
Semaphore generation returns an owned reservation that is disposable but not consumable by wait/signal.
Import realization uses one approved targeted lifecycle outcome check before imported success is exposed;
neither empty metadata nor a nonzero handle is proof of an imported payload.

`GpuImageMetadata` remains the manager's selected attachment projection, not another registered kind or
public factory result. Empty metadata schemas remain empty; no full VAO, sampler or transform-feedback
configuration mirror is introduced. Refs for these kinds are introduced with their owning family, not
as unused public scaffolding. Wrapper-owned implicit conversions follow the texture value's pattern.
`GpuBufferView` supplies its wrapper-owned texture conversion through the existing internal typed-base
constructor; it does not change `GpuTextureRef` or create another reference type.

Owner-following refs observe intentional replacement of their retained owning wrapper at execution.
Borrowers and native views retain original associations/dependencies. No ref captures another token or
metadata snapshot. Manager-certified usability includes native-view backing validity and context-local
certificates. Retention neither prevents explicit retirement nor extends an engine borrow interval.
Shader/pipeline refs have the fixed-generation contract below instead of owner-following replacement.

All factories are synchronous/context-affine except explicit pending shader creation. Manager creation
freezes legal inputs, privately allocates/configures, constructs facts and completes identity/snapshot
handoff before exposing a resource. Wrappers retain allocation/growth policy and use internal unpublished
declaration construction to avoid constructor/factory recursion. Failure cleans only new owned candidates
through original-context retirement; borrowed backing allocations survive. Intentional owner replacement
commits ready storage under existing replacement rules; it does not promise rollback after old retirement.

Borrow/adopt/recover native names belong to the OpenGL integration boundary. They accept explicit native
identity and ownership intent there and reuse covered available facts. Discovery requires recorded
pre-installation or supported EXT implicit-creation provenance; failed discovery requires explicit
original-entry recovery. They are not portable factories or automatic recovery on every command.

## Descriptor and enum inputs

Keep `RHI` on the namespace and major abstraction types. Enums use descriptive domain names without
an `RHI` or `E` prefix; native enum types remain confined to backend and explicit integration boundaries.

Descriptors use project types and immutable/frozen collections. Constructors and recorders validate
supplied shape without a current context; cached limits, optional support and resource facts are checked
at execution. Legal optional setters track assignment automatically, preserving omitted versus explicit
zero/false. Public RHI descriptions lower into existing internal `Gpu*Initialization` records; those
records and canonical structs are not repurposed as portable descriptors.
Required allocation inputs cannot be discovered by querying a newly created object. Resolve omissions
using the archived per-kind initialization defaults and existing wrapper policy, not C# struct defaults;
assignment flags are request intent only and never enter published canonical metadata.

| Domain | Named value contracts and required fields |
| --- | --- |
| Buffer | `RHIBufferDescription`: byte capacity, intended role, mutable/immutable storage, usage and storage-access flags, label. `RHIBufferRange`: ref, native-width checked byte offset/length. Whole-buffer mode is explicit, never inferred from zero or current capacity. |
| Texture | `RHITextureDescription`: dimension, extent, array layers, cube faces, format, mip count, sample count/fixed locations, mutable/immutable storage and base/max mip intentions. `RHITextureSubresource`: view-relative mip, face/layer or layered range. `RHITextureRegion`: subresource, origin, extent. |
| Backing relationships | `RHIBufferTextureDescription`: buffer ref, format, whole/ranged selection. `RHITextureViewDescription`: texture ref, compatible target/format, base/count mips and layers. Renderbuffer-backed extension textures preserve their explicit association contract inside the optional backend/integration family. |
| Renderbuffer/sampler | `RHIRenderbufferDescription`: extent, format, samples and optional coverage samples. `RHISamplerDescription`: filters/mip policy, address modes, LOD range/bias, compare state, border value and explicitly optional anisotropy. Retain wrapper sampling policy. |
| Content | `RHIBufferInitialData`: destination offset and bytes. `RHITextureInitialData`: subresource regions, pixel encoding, row/slice pitches and bytes. `RHITextureTransfer`: source/destination ranges and layout for GPU buffer-image transfers. Contents and readiness certificates never enter canonical metadata. |
| Vertex input | `RHIVertexLayout`: attributes (location, binding, component format/interpretation, relative offset), stream strides/divisors. `RHIVertexStream`: buffer range, binding, stride/divisor. `RHIIndexStream`: range and index format. `RHIGeometryInputs`: frozen streams and optional index stream/borrow interval. `RHIVertexArrayDescription` supplies the same configuration for explicit creation. |
| Draw/dispatch | `RHIDraw`: first vertex/count, instance count/base instance. `RHIDrawIndexed`: first index/count, base vertex, instance count/base instance. `RHIIndirectDraw`: argument range, count/stride and optional count-buffer range for supported optional operations. `RHIDispatch`: group counts. `RHIIndirectDispatch`: argument buffer range. |
| Targets/pass | `RHIAttachment`: texture ref/subresource or renderbuffer ref. `RHIRenderPassDescription`: frozen color/depth-stencil attachments, output routing, optional render area, typed clear values and independent load/store actions. Explicit `RHISurfaceRef` represents an integration-owned surface, never bare zero. `RHIFramebufferDescription` supplies configuration/attachmentless defaults for integration and target service. |
| Pipeline | `RHIGraphicsPipelineDescription`: exact shader ref, vertex layout, primitive assembly, depth/stencil, rasterizer, per-color blending, sampling/output state, target format/sample signature, dynamic-field mask and label. `RHIComputePipelineDescription`: exact compute shader ref and label. `RHIDynamicState`: viewport/scissor, blend constants, stencil references and other declared dynamic values. |
| Bindings/uniforms | `RHIResourceBindings`: exact prepared-interface identity, frozen active slot inputs with typed texture/image/sampler/buffer ranges, ordinary values and logical uniform snapshots. `RHIImageBinding`: ref, mip, layer/layered, access and format. `RHIUniformSnapshot`: immutable bytes, captured revision, logical publication owner/lifetime; no physical ring offset. |
| Synchronization/results | `ResourceAccess` flags express supported producer/consumer access categories. `RHIBarrierDescription` contains the explicit ordered access intent. `RHIReadbackDescription` names source range/region and layout. `RHIQueryDescription` selects timestamps/statistics/occlusion kind. Wait timeout/poll policy is explicit; external semaphore descriptions retain buffer/image transitions. |
| Auxiliary resources | `RHITransformFeedbackDescription`: retained output buffer ranges; begin/end/pause/resume select primitive kind. `RHIMemoryObjectDescription` and `RHISemaphoreDescription`: supported parameter/import intent and explicit external-handle ownership. Platform handles use a typed import payload, not a resource ID or opaque GL enum. |
| Boundary | `RHIBoundaryDescription`: immutable permitted graphics/compute pipeline refs, explicit state-category footprint and existing conditional-rendering/reentry declarations. A closed list supplies an equivalent declaration from immutable descriptions/opcodes only. |

Project-owned enum domains are `TextureFormat`, `TextureDimension`, `RHIPixelEncoding`,
`BufferRole`, `ResourceStorageMode`, `BufferUsage`, `StorageAccess`, `ImageAccess`,
`IndexFormat` (unsigned 8/16/32), `VertexInterpretation` (float/normalized/integer/double),
`PrimitiveTopology`, shader stage/code format, comparison/stencil/blend/rasterizer modes,
sampler filters/address modes, attachment load/store actions, query kinds, external handle kinds,
`ResourceAccess`, dynamic fields and state-category flags. Reuse existing project-owned values where already
equivalent; replace OpenTK-valued fields and their callers directly. `TextureFormat`/pixel/vertex encodings
cover the formats already supported by wrappers, `TextureFormatHelper` and shader validators, including
integer, floating, depth/stencil and compressed distinctions; no cast-compatible GL token values or
catch-all native-token member. Backend translation uses explicit enum members and rejects unrepresented
inputs. Exact external signatures retain their full OpenTK overload coverage separately.

## Executable and pipeline contracts

`RHIShaderDescription` freezes graphics-or-compute kind, stage descriptions, authored binding contract
and optional validated binary-cache input. Each stage identifies `ShaderStage`, explicit GLSL text
or SPIR-V bytes, entry point and typed specialization values. Asset loading, preprocessing, permutation
selection and binary cache acceptance policy stay above backend execution. No silent translation.

`CreateShader(description) -> RHICreationResult<RHIShader>` completes one executable bundle.
`BeginCreateShader(description) -> RHIPendingShaderCreation` freezes inputs and captures original
context/associations; `TryComplete()` returns Pending, Ready with the same creation result, or Failed
diagnostics. `Cancel()` is terminal and retires only the candidate. Synchronous completion may wait;
this is deferred link completion, not asynchronous GPU submission. Completion/cleanup never runs GL
in a replacement context. Intermediate native stages are cleaned after link where no longer needed.

`RHIShader` owns its existing native executable, immutable prepared interface and fixed generation.
`RHIShaderRef` exposes neither native resources nor lifecycle/configuration. External mutation,
executable revision change or context retirement invalidates the fixed realization. Pipeline factories
return `RHICreationResult<RHIGraphicsPipeline>` / `RHICreationResult<RHIComputePipeline>`; they consume
the ready shader interface and never relink/reflect independently. Realizations may be managed-only
application data, with no invented native registry entries. Their consumer refs retain exact identity.

`IRHIContextLifetime` admits fixed objects against their originating context and thread without exposing
a concrete backend. Graphics pipelines retain opaque `IRHIGraphicsPipelineRealization` data; the OpenGL
implementation owns relevance and selective restoration coverage. Managed PSOs have no native registry
entries. Pending shader requests retain an `IRHIShaderCreation` operation, whose implementation owns
unpublished native stages and programs. Query/readback tickets use the internal execution contract for
observation and mapping, preserving original cleanup and completion ownership.

Backend construction and native lifetime notifications belong to the OpenGL composition boundary.
Restricted engine shader/stage integration exposes named operations and never returns a backend. Ordinary
context and owner-authorized operations dispatch through `IRHIBackend`; there is no context backend
escape property. Stable pinned upload memory is consumed immediately, and staged uploads carry a typed
buffer reference rather than rediscovering an allocation by numeric ID.

`RHICommandContext.Composition` alone selects concrete execution and CPU-only exclusion authority.
`RHIShaderBinary.NativeFormat` is an internal opaque executable-cache token: the backend validates/encodes
it, while `DriverProgramCache` persists it unchanged with its bytes. It is not a public format selector
or a shader/PSO policy input. `RHIExternalHandle` instead declares semantic platform-handle kind and
ownership explicitly; retaining it never closes or duplicates the operating-system handle.

Portable payload isolation includes nested fields/properties, container arguments and inherited storage,
not just method signatures. Runtime restoration has a separate ownership boundary: `RHIBoundaryScope`
privately retains the existing `EngineBoundaryScope`, whose cache/snapshot state belongs to execution.
That implementation token is neither a portable pass description nor a public context parameter/result.
It is not permission to put native fields in descriptions, initialization or canonical snapshots.

PSO equality covers all behavior fields and exact shader generation, excludes labels, framebuffer identity,
ordinary values and current resource instances. Renderer owners reuse realizations; no global PSO cache
or reference-counted sharing. Pipeline disposal never disposes its shader; retention does not block
explicit shader retirement. Reload builds shader and required PSOs, installs a compatible set between
submissions and preserves the installed set on candidate failure. Stale candidates cannot install.
Prepared tables/history belong to the exact installed interface; never retarget recorded slot indices.

## Operation signatures and dispositions

The following are semantic signature contracts, not compilable forwarding stubs. `I` means immediate
only, `B` means both context execution and list recording. Owner-bearing operations are internal routes
called by owning abstractions; a consumer ref does not grant resize/delete authority. The context's
factories cover the resource table above; no public `Destroy(ref)` operation exists.

| Operations | Inputs → result | Disposition |
| --- | --- | --- |
| `Create*`, `CreateShader`, pipeline factories | descriptions + optional initial content → typed ready owned creation result | I |
| `ExportShaderBinary` | fixed shader ref + explicit byte budget → optional opaque executable-cache payload | I; cache policy/persistence remains with the shader owner |
| `AllocateBufferStorage`, `AllocateTextureStorage`, `AllocateRenderbufferStorage`, `ConfigureSampler`, `ConfigureVertexArray`, `ConfigureFramebuffer`, backing/view configuration and `SetMipRange` | owner authority + frozen legal description → coherent completion or original error | I |
| `GenerateMipmaps` | texture write input and view-relative range → completion | I initially |
| `UploadBuffer`, `UploadTexture` | destination ref/range or region + bytes/layout → completion | B |
| `CopyBuffer`, `CopyTexture`, `TransferBufferToTexture`, `TransferTextureToBuffer`, framebuffer copy/blit | explicit source/destination ranges/regions, formats and allowed filtering → completion | B |
| `ClearBuffer`, `ClearTexture` | destination range/region + typed clear encoding/value → completion | B |
| `EnterBoundary` / scope disposal | `RHIBoundaryDescription` → admitted scope; restoration on exit/unwind | I; lists provide declaration at Submit |
| `BeginRenderPass`, `EndRenderPass` | attachment/surface pass description → complete pass scope | B; no split passes |
| `SetPipeline` | graphics or compute pipeline ref → selected immutable realization | B |
| `BindUniformSnapshot`, `BindSampledTextures` | captured logical uniform version at a slot, or complete retained sampled texture/sampler slot set with semantic dimensions → validated publication without owning an engine executable | B |
| `SetDynamicState`, `BindResources` | declared dynamic values, complete interface-qualified binding set → validated publication | B |
| `Draw`, `DrawIndexed`, `DrawIndexedBatch`, `DrawIndirect`, `DrawIndexedIndirect` | geometry inputs + explicit draw/argument description → ordered native dispatch; noninstanced indexed batches consume a stable supplied span immediately and recording copies it | B |
| `Dispatch`, `DispatchIndirect` | group counts or argument range → ordered compute dispatch | B |
| `Barrier`, `WaitGpu`, `WaitSemaphore`, `SignalSemaphore` | access intent, sync/semaphore refs and explicit resource transitions → ordered native work | B |
| `InsertFence` / `RecordFence` | context: → owned `GpuFence`; list: → `RHIFenceTicket` without native identity | I / recorded marker |
| `BeginQuery`, `EndQuery`, `WriteTimestamp`, `RequestQueryResult` | precreated query ref/kind → ordered operation; result request → `RHIQueryTicket` | B |
| `EnqueueReadback` | readback description → `RHIReadbackTicket` owning staging/completion | B |
| `Poll`, `WaitCpu`, `MapReadyReadback` | sync/result owner and explicit timeout/mapping policy → pending/ready/failure/cancellation or scoped ready mapping | I |
| `Flush`, `WaitIdle` | explicit current-context submission or complete-context CPU wait → completion | I; no implicit call from ordinary submission |
| Transform-feedback begin/end/pause/resume | feedback ref + primitive/output description → ordered capture state | B; context-affine setup I |
| External import and residency commitment | owner authority + typed import or range commitment → completion/unsupported diagnostics | I |
| `Submit` | closed list → `RHISubmissionResult` (Executed or Failed with command index/original error) | I |

No recording of client pointers/mapped spans, general resource creation or arbitrary callbacks. Upload
bytes remain stable until immediate return; recording copies them. GPU transfer sources/staging retain
their actual completion lifetime. Readback tickets own staging independently of the list, map only when
ready, and support explicit polling/waiting. Result states are Pending/Ready/Failed/Cancelled; readiness
does not transfer resource disposal authority. A fence marker creates nothing if cancelled pre-execution.
Existing query kinds/conditional rendering and supported indirect variants retain their native semantics;
optional variants are rejected before dispatch when unavailable, never emulated by reading GPU contents.

## Submission and failure rules

1. Validate context/thread, resource identity/usability, known limits and usage/reentry guards before
   mutation. Content-dependent index/indirect values remain the producer's responsibility; validate only
   supplied descriptors and accesses derivable from explicit parameters, with no validation readback.
2. Eligible known-equal binding/state changes may be suppressed. Allocation, upload, draw, link and
   synchronization never suppress merely because metadata matches. Stable refs still revalidate identity.
3. Capture original associations and restoration facts, withdraw affected metadata through existing
   manager operations and explicitly compose internal steps. No ambient collector or success-on-dispose.
4. Execute once; reconcile cache knowledge and commit complete facts once through the manager. Use
   supplied/default/derived facts, retaining only the approved sample/resolved-format, shader output,
   external acquisition/recovery and deletion-outcome reads. No `GetError` drain, object-validity probes,
   requested/actual cache or universal command-success query.
5. On interruption stop, preserve the original error, unwind scopes, withdraw uncertain knowledge and
   retain earlier outcomes. No alternate-path retry, implicit repair or native rollback. Retire original
   entries/context only, never rediscover a queued delete by reused numeric ID.

Pass target realization keys use original attachment associations, view-relative subresources, proven
selection mode and routing/configuration, excluding load/store/clear intentions. The context/backend
target service owns its framebuffer realizations, manager retains metadata/dependencies. Validate
completeness before pass effects; failed misses clean candidates. Replacement/invalidation prevents stale
reuse; teardown retires only owned targets. Explicit engine framebuffer/surface inputs remain borrowed.

Boundary admission occurs before mutation using the full permitted pipeline set and state footprint.
Immediate callers declare it; closed lists derive it from immutable inputs only. Existing boundaries
cannot widen. Reuse existing selective restoration, feedback/usage guards and helper state; no blanket
capture or implicit list isolation.

`RHIResourceBindings` uniform inputs capture logical bytes/revision/lifetime. Physical allocator selection,
upload and `UniformPublication.Prepare/Commit/Abort` occur at execution in the current open epoch.
Commit only the captured successful revision, preserving newer CPU edits. Cancellation allocates no ring
storage; failed binding aborts only a new candidate. Ordinary successful-upload history also advances
at execution, never recording. Shader table/generation compatibility is checked before publication.

## List lifecycle and storage

`Recording -> Closed -> Executing -> Executed | Failed`; `Recording | Closed -> Cancelled`.
Only terminal states permit `Reset()` to empty Recording. Invalid state transitions reject without
native work. No replay, execution-time concurrent cancellation, nested lists or cross-list scheduler.
`Close()` rejects unbalanced passes and freezes payloads; exclusive ownership transfer publishes all
recorded memory. Initial recording/submission is on the originating thread but recording needs no current
native context, metadata, manager/cache access, pass guard or allocator. Lists carry intended context
identity, not an ambient context lookup. Resource-dependent validation happens at execution.

Typed reusable pages hold payloads; managed reference tables and copied byte storage have list ownership.
No closures, unmanaged managed-reference unions or required per-command heap objects. Immediate calls
do not allocate command storage. Submit executes in explicit caller order and reports CPU dispatch
completion only, with no automatic fence/Finish. Reset releases recording memory, not GPU-used staging.
The implementation exposes closed-list declarations through `RHICommandList.Boundary`; explicit sampled
slot dimensions and uniform slots supplement pipeline-derived coverage. `Submit` admits the full footprint
before commands, leaves an existing caller pass intact on rejection, and introduces no implicit boundary.
`RHISubmissionResult` separately carries `CleanupError`, preserving `Error` as the original exception.

Failed lists report the failing index and original error; unexecuted requests fail/cancel while already
enqueued tickets retain completion ownership. Context retirement cancels CPU work without GL into a
new context. No worker scheduling, concurrent execution or performance measurement gate is introduced.

## Steady-state source ownership

Native engine integration retains explicit framebuffer attachment/routing/blend policy and callback mesh
ownership. Restricted integration requests admit the original context and invoke the existing backend;
they return no backend object. Fullscreen/particle adapters expose geometry or producer certificates,
while backend operations own native selector/restoration scopes. Cloud external acquisition reuses
available canonical manager facts and queries unknown facts through backend services. Reported callback
outcomes certify content; neither ambient GL errors nor object-validity queries provide a certificate.
Pass helpers under the OpenGL backend serve only context pass execution and specialized validation.

| Directory/domain | One responsibility per file/module; dependency direction |
| --- | --- |
| `Rendering/RHI/` | Thin `RHICommandContext` composition and capability/result contracts; callers depend on semantic inputs. |
| `Rendering/RHI/Descriptions/` | Project-owned immutable descriptors/enums; no backend/manager/native queries. |
| `Rendering/RHI/Shaders/`, `Rendering/RHI/Pipelines/` | Fixed executable/PSO ownership and consumer refs; native realization through backend services. Asset policy remains above. |
| `Rendering/RHI/Commands/` | Header stream, typed storage, lifecycle and result tickets in separate files; recorder depends on immutable inputs, never execution knowledge. |
| `Rendering/RHI/Backends/OpenGL/` | Backend composition, focused resource/transfer/shader/vertex/pass/sync modules, exact forwarding modules and `OpenGlStateCache` knowledge/restoration. No giant dispatcher or old-version strategy. |
| Existing resource/manager/metadata/initialization domains | Existing owners, canonical data and manager-owned readers/publication. Backend query service executes native reads; manager decides acquisition. No parallel RHI resource hierarchy. |
| Existing pass/uniform/integration helpers | Specialized policy/validation/allocator state behind context, with backend execution dependencies; orchestration above context. |

These contracts are implemented with their complete migration units. An unimplemented signature in this
planning document is not permission to add a production placeholder, shimming facade or duplicate owner.

## Optional resource diagnostics

Owner label updates enter through the private RHI diagnostics contract using `GpuResource`, never
native object selectors or IDs. The backend admits the original allocation without `Is*` queries;
labels neither acquire/publish metadata nor certify command success. Managed names remain available,
borrowed ownership is unchanged, and native label/notification calls are DEBUG-only and best-effort.
Native label/error/group implementations and engine label integration live beneath backend diagnostics.
Explicit diagnostic assertions and groups have semantic RHI entry points; no automatic command-success
error drain is added. Failed engine shader compilation retains its bounded integration diagnostic report.
