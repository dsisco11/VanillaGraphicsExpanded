# GPU resource metadata implementation inventory

Planning companion to [the approved proposal](Rendering.GpuResourceHandles.Proposal.md) and
[implementation checklist](Rendering.GpuResourceHandles.todo). This records the source inventory and
chosen implementation mechanics. The implementation checklist distinguishes the implemented resource
and command foundation, persistent-consumer integration and integrated qualification from the separately
requested texture-reference, RHI-design and multi-bind work. Qualification limits remain in the checklist.
Legacy fixed-function/compatibility facilities are excluded from API coverage, including matrix stacks,
display lists, server/client attribute stacks, client default resets, legacy client arrays, alpha testing,
point smoothing, separate polygon face modes, line/polygon stipple and automatic mip generation. Modern retained operations remain required regardless of introduction date. The separate
RHI command contract retains the same exclusion boundary.

## Publication mechanics authority

The approved command-publication contracts (`3f307365:docs/Rendering.CommandPublication.Proposal.md`) govern the
implementation. Controlled initialization/mutations use inputs, defaults and derived values, not
full reader refresh. Native readers remain for external acquisition/recovery and listed output
exceptions. The source inventories below describe current responsibility boundaries. Ownership,
original associations, context safety, dependency semantics and notification
ordering remain required. No universal command-success detector or parallel requested/actual cache is added.

## Wrapper and ownership inventory

Paths below are relative to `VanillaGraphicsExpanded/Rendering` unless stated otherwise. All wrappers
inherit `GpuResource`'s local disposal, `Detach`/`ReleaseHandle`, captured manager/context and default
ownership unless a row says otherwise. All native IDs are integers except `GpuFence`'s pointer-sized
sync. Registration must occur when a native object exists, not merely when a CPU declaration is created.
Resource wrappers retain growth/sampling policy, attachment selection and upload helper strategy.
`GpuTextureRef` is the consumer-facing value for owned, borrowed and native-view texture inputs.
It retains the typed resource wrapper without exposing ownership or mutation methods. Conversion
operators live on `GpuTexture`, `GpuBufferView` and `GpuTextureView`; manager-certified published metadata supplies
binding and attachment validation without native discovery during reference construction or access.
Buffer/image/renderbuffer/sampler and auxiliary resource creation uses RHICommandContext descriptions and typed ready-owner
results, privately lowering to manager initialization records. The OpenGL backend executes these native
operations; the manager publishes complete snapshots. Wrappers retain growth, upload and sampling policy.
Framebuffer, vertex-array and executable families use the same internal RHI execution boundary and manager publication authority.
Wrapper slot-binding APIs are absent; complete RHI tables or typed sampled-slot packets publish resources.
Engine slot discovery and program assignments remain explicit native integration. Pending native links and
pass completeness/revision reuse belong to the backend. Private owner operations also execute optional
bindless residency and explicit window-surface discovery without wrapper-owned native calls.

Buffer and image owner creation goes through RHI before the private manager handoff; the backend uses the manager's
`CreateBuffer`, `CreateTexture`, `CreateRenderbuffer`, `CreateBufferTexture` and `CreateTextureView`
entry points. Their initialization records distinguish omitted inputs from explicit zero/false.
Private allocation returns a complete snapshot before identity/recipient handoff. Controlled changes
publish arguments, native defaults and derived image geometry; wrappers retain upload and sampling policy.
Unsized formats acquire only the resolved format. Sample requests use cached target/format capabilities;
renderbuffer and multisample-texture allocation without those capabilities reads only its resulting sample count.
Known source metadata supplies view geometry and backing ranges without object rediscovery. Buffer
realization uses core `CreateBuffers`; reserved-name realization is retained only for exact external calls.

Vertex arrays and program pipelines use private RHI creation and empty manager initialization.
Queries declare their category in GpuQueryInitialization and use core CreateQueries; transform feedback
uses core CreateTransformFeedbacks. Fences and optional external memory/semaphore objects also execute
through the RHI backend with the same private manager handoff. Their canonical schemas stay empty,
without creation-time descriptor or status queries. Exact external generated names retain their distinct
unrealized semantics; managed named creation is realized. Semaphore generation is a reservation even
for managed owners; confirmed import supplies its realized empty snapshot. Fence handles remain native-width.
Semaphore/memory-object capability checks precede allocation; imports, handle ownership, waits,
sampling policy and configuration remain separate wrapper operations.

| Kind | Existing wrappers / acquisition | Metadata and disposition |
| --- | --- | --- |
| Texture | `GpuTexture`: `Texture2D`, `Texture3D`, `DynamicTexture2D` (including `DepthTexture`, `StencilTexture`, `DepthStencilTexture`), `DynamicTexture3D`, `DynamicTextureCube`, `PmjJitterTexture`, `PBR/Clouds/CloudBlueNoiseTexture` | Private RHI creation allocates through the backend and publishes established facts. Target, dimensions, internal format, allocated mips, immutable-layered flag and storage generation participate in validation/dependencies. Replacement registers the new object; in-place mutation updates canonical metadata and entry revision. Filter mode is an owner policy, not a complete snapshot of arbitrary GL sampler parameters. |
| Texture | `BorrowedTexture(int)` | Reuses available covered canonical facts; unknown or unavailable objects use native target/image discovery. Non-owning; transfer rejected; dispose is local only. Same live native object must share the owner's canonical entry. |
| Texture | `GpuBufferView`, derived `GpuBufferTexture` | Own the texture name, not the buffer. Buffer ID, format, offset and size describe the view; track texture association changes. Whole-buffer views currently use size zero as a sentinel, not a cached capacity; preserve that convention. |
| Texture | `GpuTextureView` | Owns a distinct texture object sharing immutable storage, with source reference/ID/generation, target, format, base/count levels and layers. It gets its own canonical texture entry, distinct from its source. Preserve current source-generation validation. |
| Buffer | `GpuBufferObject`: `GpuVbo`, `GpuEbo`, `GpuUniformBuffer`, `GpuShaderStorageBuffer`, `GpuAtomicCounterBuffer`, `GpuIndirectBuffer`, `GpuPixelPackBuffer`, `GpuPixelUnpackBuffer` | Core CreateBuffers only; capacity/usage/immutable/access flags are storage metadata. `Role` is the wrapper's intended semantic binding policy. EBO index type/count use semantic IndexFormat and authored content extent, not discoverable GL facts. Pack/unpack methods return mapping pointers to their callers; they do not retain a mapping snapshot for notification to refresh. |
| Framebuffer | `GpuFramebuffer`: `DynamicGpuFramebuffer`, `StaticGpuFramebuffer` | Generated owners and external non-owning wraps. Attachment dictionary, wrapped extent, surface/pass metadata and dirty publication. External attachment changes refresh actual attachment descriptions, preserving ownership of existing image wrappers. Window-surface format/samples remain on the explicit surface publication path; default framebuffer zero is not a native object handle. Static attachment locks stay in force. |
| Texture / Renderbuffer | `GpuFramebufferAttachment` | Constructors immediately allocate owned backing storage, or factories retain a typed/raw external image; base `OwnsResource=false`, but owned backing storage is released through its existing disposal queue/finalizer mechanism. Associate with the backing entry and receive an image metadata projection. Selected mip/layer/cube face remain local view choices. External dimensions/format/samples must refresh. `Detach` rejected. |
| Renderbuffer | `RHICommandContext.CreateRenderbuffer` / `OpenGLRenderbufferIntegration.Borrow` | Creation owns; explicit native integration borrows. Storage format, width, height and actual sample count refresh from native storage. Only owning wrappers may transfer their handle; Wrap borrowers reject Detach and ReleaseHandle without affecting the external owner. |
| VertexArray | Private backend `CreateGeometryVertexArray` | Wrapper retains ID/debug name, not a complete attribute mirror. `GpuVertexAttribBinding` is a value configuration helper holding VAO/index/buffer/offset/stride/divisor. Preserve authored geometry descriptions; make stale helper reuse detectable by owning-wrapper identity and manager-owned revision, or resolve current facts before fallback use. No invented global VAO layout cache. |
| Program | Private backend `CreateProgramOwner` / external `Adopt` | The backend creates and completes the native owner; `RHIShader` retains that owner and its immutable prepared interface. `ShaderCompilation/ComputeShaderAsset` owns asset policy and a compatible RHI shader/compute PSO set without native re-registration. `Shaders/GpuProgram` and `GpuComputeProgram` retain feature settings/inputs above these objects. External executable changes invalidate fixed RHI references. |
| Shader | `GpuShaderModule` | Adopt/compile/load shader, fixed shader type and authored binding contract. Shader compilation data is not a mutable storage descriptor; program link preparation remains responsible for reflected interfaces. Fixed shader type becomes canonical metadata; no mutable storage refresh. |
| Query | `GpuQuery` | Core named target-specific creation and raw Gen/Begin/Counter aliases maintain original identity. Empty schema; target/activity and result generations belong to execution knowledge. Results are explicit outputs. |
| Sync | `GpuFence` | Observed FenceSync maintains pointer-shaped identity without status discovery; signal/wait results remain explicit outputs. |
| TransformFeedback | `GpuTransformFeedback` | Core named object; no retained binding/configuration snapshot in wrapper. OpenGlStateCache binding state remains separate. |
| Sampler | `GpuSampler` | Generated name; setters do not retain a complete parameter snapshot. No mutable wrapper metadata to refresh. |
| ProgramPipeline | `GpuProgramPipeline` private recipient | Core named initialization through the private manager/RHI handoff; stage/program calls and validation query native state, no retained stage mirror or general wrapper allocation factory. |
| Semaphore / MemoryObject | `GpuSemaphore`, `GpuMemoryObject` | Capability-gated EXT creation/import with empty schemas. Memory creation realizes immediately; semaphore Gen reserves, and confirmed import realizes the same identity. The RHI semaphore import makes one targeted lifecycle outcome read; native import observers do not probe. Handle ownership remains unchanged. |

The `new IsValid` properties in concrete wrappers must also consume shared validity; changing only the
base property would leave bypasses. Debug labels and authored policies remain wrapper-local.

The native `EngineBoundaryResources` footprint belongs to backend boundary admission/restoration and
has no managed consumer. `OpenGLPassTarget` is a private framebuffer realization retaining its original
configuration lease and nonowned image projections. Private geometry-array creation completes an
unpublished `GpuVao` through the manager before cache membership. Engine geometry import recipients
remain nonowning members of the explicit OpenGL integration boundary: their empty-array reader uses
engine realization proof, while buffers reuse maintained manager facts or explicitly recorded pre-install discovery. None supplies
an ordinary wrapper with native execution authority.

## Lifecycle and existing enforcement

`GpuResource.Dispose` checks retirement, clears the local ID, invokes hooks and schedules owned deletion.
`Detach` performs the same existing guard check, clears local state and transfers the ID without deletion.
`GpuResourceManager` queues kind/ID/context and retains the original registration when present. Both
immediate and queued deletion pass through the private RHI retirement contract to the owning backend
for every represented kind. Drain rejects a registration
replaced under the same numeric ID. Exact GL/Core, EXT and ARB deletion adapters preserve native signatures
and per-object outcomes through the same notification route.

Existing enforcement sites: `GpuResource.RequireMutableStorage`/retirement; texture allocation and
dynamic resize; renderbuffer allocation; framebuffer Configuration/PassMetadata/SurfaceMetadata/
Publication; `StaticGpuFramebuffer` frozen attachment references; `RenderPassTargets` pass references;
`RenderPassSequence` lifetime references; `GpuProgramLifetime` and compute scopes. Alias-wide counters
reside in the manager entry with the existing checks and releases. Declaration-only state remains local
until realization. Adapters do not duplicate those mutation guards.

`GpuResourceManager` drains managed cleanup before native deletion, defers live foreign contexts and
abandons dead contexts. `RenderContextRegistry` identifies handle plus registration generation and emits
`Retiring`; absence of registration is not authority for persistent external tracking. Preserve the
context-free declaration path and existing shutdown exception policy.

Graphics pipelines retain opaque backend realization data; only the OpenGL implementation owns native
relevance and restoration coverage. Fixed objects use a backend-neutral originating-context contract.
Pending shader requests expose outcomes while their backend operation owns native candidates. Engine
geometry adapters reuse manager certification; context-local freshness belongs to the selected array,
not unrelated VAO edits. Explicit draw-time index selection can retain a detached engine array and use
a private backend realization without editing engine-owned configuration.

## Metadata command matrix

Interception covers selected native entries in both OpenTK GL tables, restricted to the
GL/Core, Ext and Arb owners. Core methods are exposed directly on GL. All other auxiliary owners are excluded, including Amd, Ati, Ibm, Intel,
Mesa, Oes, Ovr, Sgis, Sun, Apple and NV. This is an owner boundary, not a filter on feature origins,
command suffixes, extension capability checks or direct VGE resource operations.
Unmapped calls execute unchanged and do not provide continuing metadata or lifecycle observation.

The retained baseline API inventory includes excluded owners as raw inspection data; filter by
managed declaring type when deriving active coverage. Retain every applicable allowed-owner overload.

The controlling coverage matrix is [Rendering.GpuResourceHandles.ApiCoverage.md](Rendering.GpuResourceHandles.ApiCoverage.md). It derives all applicable command families and variants from the OpenGL API, independently of existing callers. The wrapper inventory above maps represented fields to those families. Current call-site receipts below are integration examples only and cannot justify omitting an API operation.

## Concrete implementation decisions

These mechanics govern the implementation; the checklist records which contracts are implemented.

The registry primitives live in `GpuResourceManager.Registry.cs`, typed publication in
`GpuResourceManager.MetadataPublication.cs`, and image projection in `GpuResourceManager.ImageMetadata.cs`.
`GpuResourceRegistration` owns identity/revisions/guards; `GpuResourceMetadataEntry<TMetadata>` owns
canonical typed storage and weak recipients. `GpuResource<TMetadata>` supplies the assignment-only
notification. Schemas and the native-reader boundary live under `Rendering/Metadata/`.
Acquisition accepts a manager-executed typed reader that validates native kind and complete facts.
Per-kind readers acquire unknown external descriptors and justified outputs; controlled creation and
updates publish established facts. Common wrappers use the registry; persistent cloud/depth consumer
retention uses these common associations. The current responsibility boundary is described in
[GPU resource metadata and lifetime](Rendering.GpuResources.md).
Failed refresh preserves pending storage effects until recovery, so recovery also advances the storage
revision required by existing source-storage dependencies. This is an implementation detail of the
documented unavailable-metadata and revision contracts, not another identity mechanism.

### Canonical metadata schemas and field migration

Each struct is a value snapshot. Variable-length members are immutable after publication. Lifecycle,
availability and revisions belong to the manager entry, not native metadata. Native IDs/ranges use
native-width representation; narrower existing accessors must reject values they cannot represent.

| Kind / canonical struct | Native fields and wrapper migration | Local or derived state | API families |
| --- | --- | --- | --- |
| Texture / `GpuTextureMetadata` | Target; per-level/per-face dimensions, format, samples and allocated state; immutable storage/level count; base/max levels. Optional buffer association (ID/format/offset/range/whole-buffer mode), texture-view range (base/count levels/layers) and renderbuffer association. `GpuTexture` and all subclasses, `GpuBufferView`/`GpuBufferTexture` and `GpuTextureView` use this struct; their native descriptor fields become accessors. | FilterMode, labels, streaming and uploads remain local. The managed view source/reference/ID certificate and captured source storage revision remain dependency state: arbitrary external views do not expose a queryable source name. `StorageGeneration` reads the manager-owned storage revision; source validation asks the manager to check its original source registration dependency. | T1–T4, I1, L1 |
| Buffer / `GpuBufferMetadata` | Byte capacity, semantic `BufferUsage`, immutable-storage status and `StorageAccess` flags; `sizeBytes/usage` are accessors in `GpuBufferObject`, inherited by VBO/EBO/UBO/SSBO/atomic/indirect/pixel pack/unpack wrappers. | Intended semantic buffer role, requested allocation usage, EBO index type/count, queued uploads, mapping scopes, growth policy and labels remain local. No content or mapping-ownership mirror. | B1, I1, L1 |
| Renderbuffer / `GpuRenderbufferMetadata` | Width/height/format and actual samples; `storage/width/height/samples` become accessors. | Ownership and labels remain local. | R1, L1 |
| Framebuffer / `GpuFramebufferMetadata` | Immutable attachment descriptors keyed by slot: kind/ID, mip, layer/face, layered state, multiview base/count and proven ordinary/multiview mode (unknown when native queries cannot establish it); attachmentless default dimensions/layers/samples where applicable. Image facts resolve through backing entries. | Retained image wrappers/ownership, frozen locks, dirty/publication state, viewport fallback extent and surface metadata stay local. The wrapper dictionary reconciles from descriptors rather than independently defining native facts. Selected range/replay policy remains on the image relationship. Pass extent/format are derived; routing stays in existing OpenGlStateCache routing cache. | F1, backing T/R changes, L1 |
| VertexArray / `GpuVertexArrayMetadata` | Empty struct: `GpuVao` has no retained native layout fields. Entry revision invalidates represented helper assumptions; no full attribute mirror. | `GpuVertexAttribBinding` is an authored value helper, not a resource. Capture owning-wrapper identity and manager-owned revision for its facts; after mutation, resolve the selected current buffer/offset/stride/divisor before fallback reuse or reject unavailable facts. Vertex contents stay external. | V1, L1 |
| Program / `GpuProgramMetadata` | Native executable/interface availability and immutable native interface facts used by existing preparation, including queried names, `ProgramResourceKind` categories, semantic `ShaderValueType` values, nullable typed location/extent/block fields, vertex inputs and fragment outputs, `ShaderStageMask` ownership, atomic-counter buffer descriptors and sampler/image unit assignments. Native property bags and token identities remain beneath the backend. Known managed geometry/tessellation stage execution primitives are published with the same completed snapshot; unknown external stage facts remain absent. `GpuProgramObject` carries this type; RHI shaders and shader asset policies compose that native owner. Reflected facts no longer have independent authoritative mutable copies in `bindingCache/programLayout`. | Existing `GpuProgramLayout` remains a derived prepared interface combining native facts with authored contract/policy, stamped with entry revision. Authored names are separate from native names. Settings, callbacks and context stay local. Owner installation revision remains local across replacement objects; native relink revision is entry-owned. | P1, L1 |
| Shader / `GpuShaderMetadata` | Fixed semantic `ShaderStage`; the wrapper exposes its published stage without conversion. | Selected stage code and authored contracts belong to frozen RHI descriptions and asset policy; native stage wrappers retain identity/lifetime and fixed-stage metadata. | acquisition, L1 |
| Query / `GpuQueryMetadata` | Empty struct. | Results queried on demand. | L1 |
| Sync / `GpuSyncMetadata` | Empty struct; pointer ID is registration state. | Signal/wait results queried on demand. | L1 |
| TransformFeedback / `GpuTransformFeedbackMetadata` | Empty struct. | No retained configuration mirror. | L1 |
| Sampler / `GpuSamplerMetadata` | Empty struct. | No retained parameter snapshot. | L1 |
| ProgramPipeline / `GpuProgramPipelineMetadata` | Empty struct. | No retained stage/program mirror. | L1 |
| Semaphore / `GpuSemaphoreMetadata` | Empty struct. | Import ownership/capability policy remains local. | L1; I1 no represented mutation |
| MemoryObject / `GpuMemoryObjectMetadata` | Empty struct. | Import ownership/capability policy remains local. | L1; I1 no represented mutation |

`GpuFramebufferAttachment` represents either image kind and derives from `GpuResource<GpuImageMetadata>`.
The manager projects selected dimensions, format and samples from canonical texture/renderbuffer metadata
into that image struct. Local mip/layer/cube-face selection determines the projection. Former external
width/height/format/sample fields become accessors; backing reference, ownership and selection remain
local. Projection uses canonical data without another query or entry. Unsupported selections make that
association unavailable. It is a typed view of the backing entry, not a second canonical metadata source.

Explicit image acquisition and imported framebuffer publication use private RHI resource operations.
Wrappers retain selection and composition; the manager chooses readers and resolves each distinct backing
once per discovery. Attachment validation consumes published facts without recovery. Registered images
receive projections through their backing entry; manager-owned weak owner/image links cover local tools
and intentional owner replacement without duplicating canonical delivery.

#Image storage schemas use `TextureDimension`, `CubeFace` and `TextureFormat`; framebuffer descriptors
use `FramebufferSlot` keys. These values remain semantic through initialization, manager publication,
wrapper accessors and validation. Native format and target tokens are encoded or decoded exclusively
by backend execution or actual external native adapters. Surface facts use `SurfaceColorBuffer`.

## Manager API and generic base

Use one `ConcurrentDictionary<GpuResourceHandle, GpuResourceRegistration>` inside the context-scoped
manager. `GpuResourceHandle` is a readonly value key containing kind and `nint` ID, without ownership
or lifetime state. Each entry contains canonical metadata, availability/revisions and shared guards.
A `ConcurrentDictionary<WeakReference<GpuResource>, bool>` provides its removable recipient set and
records whether image projection or typed assignment succeeded for each recipient. The manager's
`ConditionalWeakTable<GpuResource, GpuResourceRegistration>` preserves original association identity
without retaining wrappers. Unregister removes delivery membership; original identity remains until
the wrapper is collected, preventing attachment to a replacement native object with the same ID.
Weak slots and membership remain entirely in the manager entry; removal matches wrapper identity.
Wrappers retain no registration or weak-slot reference. Typed dispatch assigns snapshots directly;
image dispatch reads wrapper-local selection without allocating association objects or callbacks.
The internal entry supplies registration state; its reference is not a generation counter or key.
BCL concurrent dictionaries provide synchronized operations, not completely lock-free writes.

Internal operation contracts:

- `RegisterCreatedResource(kind, id)` registers a created/reserved name without inventing storage.
- `AcquireResource(kind, id)` reuses existing canonical metadata or validates/captures an external object.
  `LookupAvailableResource` never reads; `RecoverResource` explicitly replaces external facts.
  Wrapper acquisition follows the same split through `AcquireWrapperMetadata` and
  `RecoverWrapperMetadata`; there is no optional forced-refresh switch.
  A generated but unrealized external name is insufficient. Texture/renderbuffer borrowers and raw
  image aliases reuse available covered entries. Unsupported native mutations require explicit
  invalidation/recovery; registry membership alone does not certify observation coverage.
- `AssociateResource(entry, wrapper)` / `UnregisterResource(entry, wrapper)` initialize typed snapshots,
  maintain weak associations and prune expired references. Image associations include local selection.
- `RefreshMetadata(entry, change)` explicitly acquires/recovers unavailable native facts. Controlled
  publication constructs a complete candidate from command facts without calling that reader. Both
  commit through the manager and deliver typed snapshots; wrappers do not query. Untracked commands
  still execute normally without eagerly populating a speculative registry of every native object.
- `RetireResource(entry)` / `ObserveDeletion(kind, id)` integrate scheduled owner retirement and observed
  native deletion, invalidate associations and cancel obsolete queued work.

The common `CreateResource` boundary freezes the initialization value, validates it through a typed
value backend, and keeps the newly owned ID private until canonical commit and typed handoff finish.
Failed handoff removes wrapper membership and identity. When native cleanup must wait for its original
context, only the existing unavailable retiring entry remains until the deletion queue drains.
Explicit backend operations own buffer/image allocation and mip-range changes; OpenGlStateCache owns attachment replay.
Internal steps receive their operation state explicitly. The manager commits a supplied complete result
once; publication never silently stages, and disposal never performs recovery. A temporary execution
guard rejects unrelated same-resource reentry before dispatch, while deletion cancels original completion.
Local operation admission withdraws the wrapper snapshot through manager services. The guard stores no
metadata or alias recipients; local tools introduce no persistent registry. Controlled texture and framebuffer operations construct complete candidates from command-established facts;
explicit recovery remains available for unknown or invalidated native state.
Native helper binding acquisition belongs to
`GpuResourceManager.AcquireVertexBindingFacts` and `GpuVertexBindingReader`, with requested fields only.

`GpuResource<TMetadata> : GpuResource where TMetadata : struct` retains a private snapshot with
`protected ref readonly TMetadata metadata` access for derived wrappers. Public non-virtual
`HandleMetadataChanged(TMetadata data)` assigns that complete private snapshot. Native metadata
accessors are getter-only; the compiler rejects derived snapshot writes. The non-generic base retains native identity, disposal and ownership with no
metadata members or notification contract. The generic base owns no tracking state, availability flags,
revision copies or lifecycle notification handlers. `GpuResourceManager.WrapperAssociations.cs` owns
association identity and exposes manager-side tracking, availability and retirement checks.
Revisions stay on canonical entries. Metadata-dependent consumers validate through the manager;
no registration or recipient back-reference is stored in a wrapper.
The owning backend or remaining OpenGlStateCache operation owns native mutation execution and sends observed results to manager publication. Wrappers
never construct metadata candidates or call mutation refresh/publication themselves. Initial native
acquisition and selected-image projection also belong to the manager, including tools without a registry.
Requested buffer usage is separate allocation policy; requested texture dimensions/format/mips are
command arguments until dispatch; the manager then publishes them under the controlled-input policy.
Manager publication coordinates dependent observers outside the generic assignment.
No abstract per-wrapper metadata-query handler remains.

CPU declarations retain requested creation parameters locally until native realization. Register in the
actual owning context. Default framebuffer zero stays on surface publication with no native entry.
Framebuffer creation uses GpuFramebufferInitialization and the common private handoff. Empty creation
publishes known defaults without descriptor queries; optional selections and attachmentless defaults
are validated against cached capabilities. Shader creation uses GpuShaderInitialization to publish its
fixed stage before compilation; programs use GpuProgramInitialization to publish an empty executable.
Compilation and linking retain their existing result checks. Deferred links retain original owner,
registration and context until exactly-once completion; submission guards prohibit reentrant discovery.
Known unit/block changes preserve unrelated interface facts: managed assignments publish supplied values,
while exact external commands read only affected values. Unsupported bindless assignments remain withdrawn.

Program installation transfers the candidate association and executable snapshot without repeating interface
reflection. Driver-binary cache loading retains the candidate wrapper through managed RHI execution,
so both registered and context-local candidates receive the linked snapshot before preparation or transfer.
Binary installation acquires the executable result once; cache acceptance reuses its link outcome.
Context-local legacy authored assignments recover only their affected mutable fields before
transfer; compiled preparation consumes the supplied snapshot directly. Unregistered
or offline paths remain conservative until a live manager exists; a thread-local OpenGlStateCache does not
confer persistent tracking. A fresh registered context lifetime requires a fresh manager/registry;
never retarget an existing manager. Preserve existing RenderContextRegistry checks at the manager
boundary, not in resource keys. Subscribe once per manager to context retirement, unsubscribe on shutdown.
Native acquisition, wrapper initialization and metadata publication occur on the owning context thread.
Available-entry command inputs and wrapper-based operation/link/transfer admission check that affinity
before native dispatch or availability withdrawal; explicit participating steps recheck it after callbacks.
Pure registration lookup and recipient removal support concurrent callers. Terminal context retirement
and late admission reconcile through atomic map operations and registration state. Worker retirement
marks the original entry atomically unavailable before queuing native work. Manager-side association
checks observe retirement without wrapper notification handlers or GL calls. Existing exception policy remains.

Buffer readers call core named backend queries only for explicit external acquisition/recovery.
Image metadata readers use core named queries, retaining bound scopes only for required cube-face facts.
External view
acquisition validates discovered ranges; controlled view creation validates its inputs against known
source metadata and derives the result without a full reader snapshot. Context-local views retain an opaque storage certificate published by the
manager on the texture owner. Metadata-only failure and recovery preserve this certificate independently
of snapshot availability; completed or interrupted storage updates replace it even when dimensions match.
Views reject unavailable source image facts until refresh succeeds. No additional registry or generic-base
tracking is used. SetMipRange commits once after both native parameters complete; interruption leaves metadata unavailable.
Texture command paths retain their established target,
and managed aliases reuse the canonical target. An arbitrary unregistered external texture whose
target is unknown remains unavailable without named target discovery; do not probe targets with
invalid binds or claim that forced bound-path tests qualify that external boundary. Selected image
factories use manager acquisition/projection, and managed texture views reuse their backing entry.
A generic BorrowedTexture cannot represent texture-buffer storage; reject that acquisition before
changing the existing buffer-view reader, snapshot or availability. GpuBufferView remains its typed API.
Owning construction cleans up newly allocated names through the owning backend if initialization or metadata
acquisition fails, preserving the original error and originating context. Borrowed acquisitions retain
external deletion authority and never use owning construction cleanup.
### Publication, failure and validation

Context-local tools without a registration discard uncertified snapshots after a failed read or an
interrupted explicit operation. Empty descriptors represent unavailable facts, not native zero-sized storage.
The manager retains only the established texture target for explicit native refresh (for example,
SetMipRange); image binding rejects an empty image snapshot. Local framebuffer image validation projects
the typed owner's current snapshot without native reads; raw local names retain native discovery.
Discarded owner facts require explicit owner recovery before image use; failed
reads or absent selections cannot retain a previous projection. Registered resources retain canonical availability and recovery.
Managed texture views require available manager snapshots for both their own registration and their
original source dependency before use. Metadata-only recovery restores usability; storage revision
changes continue to reject views certified against the previous allocation.
Failed replacement cleanup also clears the surviving texture wrapper's native ID after retiring the new
name, so later disposal cannot delete a reused name.

Mark affected metadata unavailable during acquisition or explicitly composed mutation. Controlled commands use
inputs/defaults/derivations; external acquisition initializes read outputs to invalid sentinels and
validates them in the original context. Do not drain GetError or add unconditional Is-object checks.
Reported failures do not publish success. Silent native failure detection remains the existing error
boundary; external uncertain effects remain unavailable until recovery. Unqueryable external facts
stay unavailable. The command-publication proposal lists the justified read exceptions.

Commit canonical metadata, assign all typed snapshots, then expose the revision as available and notify
existing dependents. Do not invoke arbitrary dependent observers between assignments. Failed refresh
keeps all affected aliases unavailable. If a projection or dependent notification fails, continue other
notifications and keep affected dependent use unavailable under existing error policy. Selected-image
refresh always invalidates its framebuffer dependents, including when projection fails. Storage observer
dispatch reports aggregated failures only after processing every image. Successful later
refresh may recover metadata, never a deleted registration. Mutable framebuffer configuration can remove
or replace invalid outgoing images without rendering validation of the old configuration. Other dirty
attachments retain their pending reattachment until rendering use, which still validates every image.
New associations receive a current snapshot
before becoming usable. Collections shared through struct copies must be immutable.

Entry MetadataRevision advances for changed facts or executable/configuration invalidation. StorageRevision
advances for storage replacement effects, including equal-sized reallocation where existing source
storage dependencies require it. Neither is an ID generation. Existing owner notification paths consume
manager publication once without duplicate increments. Extend existing ShaderInputValidation.InputKey
with wrapper identity and manager-owned revisions alongside current facts; preserve complete-input validation and the existing
cache. Program preparation rebuilds derived layouts for the current native revision. Attachment projections
and framebuffer observers invalidate pass metadata without bypassing frozen locks. Native binding
knowledge, producer content readiness and frame qualification remain independent.

### Retirement, ownership and retention

Entry states are Live -> Retiring -> Deleted or Live/Retiring -> ContextRetired; terminal entries never
revive. Owner retirement makes the entry unavailable to managed use and queues native deletion under
existing policy. A queue item retains its original entry; execution requires that exact entry still be
registered in the original manager. Never reacquire by numeric ID during drain. External deletion removes
the entry, invalidates wrappers and cancels pending deletion. Reused names get new entries; old owners
cannot delete them. Native deferred destruction remains intact: notification concerns the deleted name
and affected represented dependencies, not a claim that driver storage has already been reclaimed.
List outcomes preserve ordering and per-object failures. Failed deletion keeps managed retirement unavailable.

Borrowed disposal only unregisters its association. Detach unregisters the transferring wrapper while
preserving the live native entry; borrowed transfer remains rejected. Replacement detaches the old
association and acquires the new object. Alias-wide guard counters live in the entry with existing
checks/releases; declaration-only state remains local until realization. Do not duplicate mutation
guards at adapters. Context retirement clears the registry, invalidates aliases and discards pending
native work without issuing GL into another context.

Borrowed framebuffer identity replacement rejects outstanding local lifetime borrows before changing
the association; refreshing the same live identity remains permitted outside active passes. Deferred
managed disposal retains resources rejected before local retirement and retries them on a later drain.
Each drain processes at most its initial queue count, allowing unrelated cleanup to proceed without
spinning on retained resources. Terminal closure still abandons blocked context-owned allocations.
Complete native metadata reads validate identity themselves; canonical acquisition and refresh do not
repeat that validation before invoking the reader. Reserved buffer-name admission remains distinct
from complete acquisition so uninitialized generated names can wait for their first binding/storage.

Strong entry retention is bounded by live registered native objects. Weak associations are pruned on
acquisition, publication, unregister and teardown. Wrapper collection cannot erase a live tracked
object's metadata; registration never adds native ownership.

## Native coverage and consumer gates

Generated callbacks replace selected entries in both OpenTK GL tables. Supported Core/Ext/Arb
operations reach cache-owned observations independently of caller assembly, overload marshalling,
generic element type, delegates and dynamic callers. Native ABI signatures come from the compiler's
OpenTK PE reference; attributes select 306 native symbols and ABI-identical aliases share effects.
The saved original executes exactly once before observation; contained reconciliation faults neither
cancel the call nor escape to its caller. Backend-owned work uses generated `OpenGLDriverCalls`
overloads and its existing publication owner. No caller rewriting, generic closure cache or managed
adapter catalog remains. See the [native interception design](Rendering.OpenTKBindingInterception.Proposal.md)
and [API matrix](Rendering.GpuResourceHandles.ApiCoverage.md) for supported boundaries and verification.
Texture image definitions and mip generation resolve bound targets, explicit names and EXT texture
units through OpenGlStateCache. Cube faces resolve their cube object; proxy targets never select an object.
External texture-view creation observes the destination's registered identity and reads its native level/layer
selection; it does not publish a storage mutation to the source texture's registration.
Image acquisition limits view-relative mip queries to that exposed level count, independently of the
source allocation's immutable level count.
Buffer/image post-call observations use the existing focused cache and manager services.
Texture parameter overloads acquire only the changed represented base/max field when the remaining
snapshot is known. Single-image definitions acquire only the selected level/face; complete storage
definitions and unknown external objects retain descriptor acquisition. Subimage, copy-subimage and
compressed-subimage writes change contents only and have no metadata adapters. Explicit mip
generation remains observed.
Mip generation on immutable storage is content-only. Mutable generation compares only potentially
generated descendant images when the original snapshot is available, retaining other images and
parameters, and advances storage revisions for changed allocation. Image dependents are notified for
that change or recovery from unavailable metadata; regenerating existing levels preserves storage certificates.
Untracked commands execute without creating registry entries. External buffer storage withdraws
existing facts through `InvalidateNativeBufferStorage`; explicit acquisition on demand recovers
capacity/usage and commits the pending storage effect. Post-call interruption prevents an overlapping acquisition from certifying a stale candidate;
observation retains withdrawal without querying. Other image results recover affected facts,
including rejected allocations, while preserving native errors and binding effects.
Manager publication invalidates associated framebuffer-image dependents after snapshot assignment or
refresh failure. Local image tools and native-name replacement retain their wrapper observer route.
Buffer texture availability includes its tracked backing buffer's metadata availability. Successful
backing storage publication advances associated texture storage revisions while retaining their
whole-buffer convention or explicit range. A texture withdrawn for another reason is not recovered
by a backing resize; deletion continues to withdraw its association before native-name reuse.
Publishing a buffer texture acquires its concrete backing dependency in the same registry, including
when the caller supplied a raw buffer ID. This adds neither a buffer wrapper nor native ownership;
an existing usable backing entry requires no further native acquisition.
Buffer-texture association observations preserve bound, named and explicit-unit selection across core,
ARB and EXT calls. Native backing/format queries and range admission facts certify the result;
rejected commands preserve the previous certified descriptor. Whole-buffer mode comes from the
accepted command, not equality with an explicit full-range attachment. Range arguments retain
pointer width at the native observation boundary and in semantic resource ranges.
Framebuffer attachment/default observations select the original registration and recover only the affected
slot or default when the remaining descriptor is available. Unknown descriptors use initial discovery.
Controlled attachment composition publishes supplied selections once, without verification followed by
a full scan. External pass discovery reuses its canonical descriptor for transactional image reconciliation.
Readers use named attachment queries with DSA,
preserve split read/draw bindings on fallback, and gate attachmentless/multiview queries by capability.
Native configuration notifications invalidate retained helper/completeness state; managed helper
updates notify aliases while preserving the initiating helper's image ownership and completion event.
Named and pointer routing observations propagate legal object-local arguments for established identities
without changing bindings or draining errors. Window selectors use cached double-buffer/stereo facts.
Unknown named identity, unavailable initial surface facts and failed dispatch retain targeted acquisition. Read routing has no cached mirror and remains a native
selector, not framebuffer resource metadata.
OVR selected-image helpers preserve proven attachment mode and range on replay; ambiguous external
acquisition retains an unknown mode and rejects replay that cannot preserve its semantics. The API
matrix records supported families and native execution limits independently of installed engine callers.
Third-party mods, delegates, dynamically emitted callers and OpenGL4 callers share this native-table
observation boundary. Other libraries and independently cached native pointers are outside it.
External calls through excluded auxiliary owners likewise have no continuing observation.
Direct VGE multiview commands use explicit optional backend operations with publication; their shared
metadata, capabilities and replay helpers are retained independently of engine interception.
Persistent consumers must establish that subsequent mutations/deletions stay inside covered routes.

Generic uploads retain OpenTK's pinning and native marshalling before reaching the intercepted
slot. Native observers receive the same pointer and byte count without inspecting the managed element
type or discovering closed generic methods. Single-name managed deletion overloads reach the same
native deletion entrypoints as array/pointer overloads.

Cloud `CloudNativeMapAdapter` validates native map storage at allocation and keeps publication/world/
upload/generation certificates. Ordinary non-owning texture recipients qualify those certificates
against their original manager associations and storage revisions. A changed or unavailable backing
withdraws content admission; fresh allocation validation and a successful upload are required again.
`CloudStoredVolumeConsumer` retains map, primary visibility and liquid-depth borrowers across stable
selections and shares the owned material-depth texture directly. `DepthHierarchyRenderer` retains its
primary-depth borrower and replaces it when selection or original allocation validity changes.
`ScreenResourceManager` preserves executed knowledge and invokes ordered consumers after framebuffer rebuild;
consumer teardown releases managed recipients without deleting native storage. Allocation hooks retain
content-certificate responsibility; common OpenGlStateCache deletion owns native lifetime observation.
Available registered snapshots require no repeated descriptor acquisition. Context-local tools without
a manager retain conservative acquisition, while pre-installation or unrepresented facts retain explicit acquisition. Ordinary third-party calls
through supported native tables use the same mutation/deletion observations.

Raw IDs without original allocation associations retain explicit acquisition; this is distinct from
caller coverage and cannot substitute numeric equality for an original lifetime. Coverage is derived
from API families and native slot declarations, rather than a static engine caller inventory.

## Validation and restoration dependencies

Controlled program, VAO and pipeline selections retain submitted identities after dispatch and suppress
known-equal calls. External selectors execute natively once, publish established selections and preserve
prior facts on proven rejection; uncertain outcomes retain targeted recovery. Querying CURRENT_PROGRAM
does not certify full-program stage installation. Backend execution uses saved originals; native-table
callbacks invoke the cache-owned selector and routing observations after external dispatch.
Framebuffer scopes capture only their affected read/draw directions through cached getters. Element-buffer
scopes retain the original VAO association when another VAO becomes selected before restoration.
Per-VAO element-buffer knowledge is independent of canonical VAO configuration revisions.
Managed buffer allocation, mapping, flushing, unmapping and uploads use core named operations without
selector emulation. Required external bound operations restore only their affected selectors. Vertex
helpers retain authored fields and acquire requested unknown facts through manager query services.
Freshness is selected-array configuration/revision, not a context-wide duplicate registry or full layout
mirror. Resource wrappers and the canonical empty VAO schema remain unchanged.

`ShaderPreparedSubmission` checks lifetime each use; `ShaderInputValidation` keys contain wrapper,
numeric ID, target/format/dimensions/mips or view/range/capacity and manager-owned metadata/storage
revisions. Availability is checked before validation reuse. Typed sampled-slot publication uses manager-certified resources and may suppress known-equal binds.
Explicit engine membership/assignment remains a separate integration boundary.

The backend deletion route forwards observed retirement to `OpenGlStateCache.ResourceDeletion`, invalidating image slots and resetting matching
texture bindings after deletion. `OpenGlStateCache.ResourceSlots` keys all image view parameters and indexed
buffer ranges. `OpenGlStateCache.BoundaryBindings` captures/reconciles incoming state and refuses restoration
of retired names. Context retirement and unknown-state recovery remain independent of resource validity;
a live registration is never evidence that an object is bound. The Prepared resource submission section of `Rendering.ShaderBindingState.md` describes the current
validation path; historical receipts remain in planning/evidence records.


## Optional resource labels

The existing owner supplies optional names through the private RHI diagnostic contract. Native label
selectors and allocation IDs are interpreted only in backend diagnostics or its explicit engine-label
integration helpers. Labels preserve ownership, bindings, pending errors and canonical metadata; no
`GL.Is*` probe is used. Native labels compile out of Release independently of managed naming.

Scalar post-call state delivery names compiler-checked handwritten withdrawal methods and uses generated
origin checks/targeted recovery. Withdrawal receives the cache and native arguments; cache categories
and argument-dependent affected targets remain inside domain methods.
Handwritten classifiers distinguish known changes, proven rejection and uncertainty. Unsigned ordinary
uniform setters have no represented canonical effect; signed sampler/image and opaque-handle assignments
retain their metadata routes. Program relevance is established before operation interruption or refresh.
