# GPU resource metadata implementation inventory

Planning companion to [the approved proposal](Rendering.GpuResourceHandles.Proposal.md) and
[implementation checklist](Rendering.GpuResourceHandles.todo). This records the source inventory and
chosen implementation mechanics. The implementation checklist distinguishes verified registry and
wrapper/lifecycle integration from the remaining command and consumer integration.
Legacy fixed-function/compatibility facilities are excluded from API coverage, including matrix stacks,
display lists, server/client attribute stacks, client default resets, legacy client arrays, alpha testing,
point smoothing, separate polygon face modes, line/polygon stipple and automatic mip generation. Modern retained operations remain required regardless of introduction date. The separate
GpuCommandList RHI design is unaffected.

## Publication mechanics authority

The approved [command-publication contracts](Rendering.CommandPublication.Proposal.md) govern the
planned migration. Controlled initialization/mutations use inputs, defaults and derived values, not
full reader refresh. Native readers remain for external acquisition/recovery and listed output
exceptions. The source inventories below describe existing paths to migrate; references to reader
refresh, post-command verification and wrapper-owned allocation loops are not requirements to retain
those mechanics. Ownership, original associations, context safety, dependency semantics and notification
ordering remain required. No universal command-success detector or parallel requested/actual cache is added.

## Wrapper and ownership inventory

Paths below are relative to `VanillaGraphicsExpanded/Rendering` unless stated otherwise. All wrappers
inherit `GpuResource`'s local disposal, `Detach`/`ReleaseHandle`, captured manager/context and default
ownership unless a row says otherwise. All native IDs are integers except `GpuFence`'s pointer-sized
sync. Registration must occur when a native object exists, not merely when a CPU declaration is created.
Resource wrappers retain growth/sampling policy, attachment selection and upload helper strategy.
Manager creation orchestrates allocation from initialization records.
StateCache executes their native commands and owns metadata publication, including deferred publication
for complete texture allocation and framebuffer reattachment operations.

| Kind | Existing wrappers / acquisition | Metadata and disposition |
| --- | --- | --- |
| Texture | `GpuTexture`: `Texture2D`, `Texture3D`, `DynamicTexture2D` (including `DepthTexture`, `StencilTexture`, `DepthStencilTexture`), `DynamicTexture3D`, `DynamicTextureCube`, `PmjJitterTexture`, `PBR/Clouds/CloudBlueNoiseTexture` | Allocation helpers generate/bind/allocate. Target, dimensions, internal format, allocated mips, immutable-layered flag and storage generation participate in validation/dependencies. Replacement registers the new object; in-place mutation updates canonical metadata and entry revision. Filter mode is an owner policy, not a complete snapshot of arbitrary GL sampler parameters. |
| Texture | `BorrowedTexture(int)` | Validates `IsTexture`, queries target/base dimensions/format and mip widths. Non-owning; transfer rejected; dispose is local only. Same live native object must share the owner's canonical entry. |
| Texture | `GpuBufferView`, derived `GpuBufferTexture` | Own the texture name, not the buffer. Buffer ID, format, offset and size describe the view; track texture association changes. Whole-buffer views currently use size zero as a sentinel, not a cached capacity; preserve that convention. |
| Texture | `GpuTextureView` | Owns a distinct texture object sharing immutable storage, with source reference/ID/generation, target, format, base/count levels and layers. It gets its own canonical texture entry, distinct from its source. Preserve current source-generation validation. |
| Buffer | `GpuBufferObject`: `GpuVbo`, `GpuEbo`, `GpuUniformBuffer`, `GpuShaderStorageBuffer`, `GpuAtomicCounterBuffer`, `GpuIndirectBuffer`, `GpuPixelPackBuffer`, `GpuPixelUnpackBuffer` | CreateBuffers with GenBuffer fallback; size/usage are storage metadata. Target is the wrapper's intended binding role. EBO index type/count are authored interpretation/content extent, not discoverable from GL bytes. Pack/unpack methods return mapping pointers to their callers; they do not retain a mapping snapshot for notification to refresh. |
| Framebuffer | `GpuFramebuffer`: `DynamicGpuFramebuffer`, `StaticGpuFramebuffer` | Generated owners and external non-owning wraps. Attachment dictionary, wrapped extent, surface/pass metadata and dirty publication. External attachment changes refresh actual attachment descriptions, preserving ownership of existing image wrappers. Window-surface format/samples remain on the explicit surface publication path; default framebuffer zero is not a native object handle. Static attachment locks stay in force. |
| Texture / Renderbuffer | `GpuFramebufferAttachment` | Constructors immediately allocate owned backing storage, or factories retain a typed/raw external image; base `OwnsResource=false`, but owned backing storage is released through its existing disposal queue/finalizer mechanism. Associate with the backing entry and receive an image metadata projection. Selected mip/layer/cube face remain local view choices. External dimensions/format/samples must refresh. `Detach` rejected. |
| Renderbuffer | `GpuRenderbuffer.Create` / `Wrap` | Create owns; Wrap borrows. Storage format, width, height and actual sample count refresh from native storage. Only owning wrappers may transfer their handle; Wrap borrowers reject Detach and ReleaseHandle without affecting the external owner. |
| VertexArray | `GpuVao.Create` | Wrapper retains ID/debug name, not a complete attribute mirror. `GpuVertexAttribBinding` is a value configuration helper holding VAO/index/buffer/offset/stride/divisor. Preserve authored geometry descriptions; make stale helper reuse detectable by owning-wrapper identity and manager-owned revision, or resolve current facts before fallback use. No invented global VAO layout cache. |
| Program | `GpuProgramObject.Create` / `Adopt`, `GpuComputePipeline` | Binding cache/layout and executable revision/context. Compute declarations can begin without an executable and later install one; acquire on installation in its actual context. Relink/binary replacement invalidates prepared executable metadata; existing owner preparation must establish a usable layout again. `Shaders/GpuProgram` and `GpuComputeProgram` are composition owners, not additional GpuResource subclasses. |
| Shader | `GpuShaderModule` | Adopt/compile/load shader, fixed shader type and authored binding contract. Shader compilation data is not a mutable storage descriptor; program link preparation remains responsible for reflected interfaces. Fixed shader type becomes canonical metadata; no mutable storage refresh. |
| Query | `GpuQuery` | Generated name; results are queried on demand. No retained mutable resource metadata. |
| Sync | `GpuFence` | FenceSync creates pointer-shaped native identity; signal/wait result is queried, not cached storage metadata. |
| TransformFeedback | `GpuTransformFeedback` | Generated object; no retained binding/configuration snapshot in wrapper. StateCache binding state remains separate. |
| Sampler | `GpuSampler` | Generated name; setters do not retain a complete parameter snapshot. No mutable wrapper metadata to refresh. |
| ProgramPipeline | `GpuProgramPipeline` | Generated name; stage/program calls and validation query native state, no retained stage mirror. |
| Semaphore / MemoryObject | `GpuSemaphore`, `GpuMemoryObject` | EXT creation/import, capability gated. No represented mutable metadata; importing handles does not change ownership semantics. |

The `new IsValid` properties in concrete wrappers must also consume shared validity; changing only the
base property would leave bypasses. Debug labels and authored policies remain wrapper-local.

## Lifecycle and existing enforcement

`GpuResource.Dispose` checks retirement, clears the local ID, invokes hooks and schedules owned deletion.
`Detach` performs the same existing guard check, clears local state and transfers the ID without deletion.
`GpuResourceManager` queues kind/ID/context today; its drain has no incarnation token. Both immediate
and queued switches use StateCache for Buffer, VertexArray, Texture, Framebuffer, Renderbuffer, Program,
Sampler, and direct GL for Query, Sync, TransformFeedback, ProgramPipeline, Shader, Semaphore, MemoryObject.
All fourteen kinds need a common deletion notification route. EXT operations need explicit adapters;
`EngineStateCallMap` currently matches methods on `GL`, not nested `GL.Ext`.

Existing enforcement sites: `GpuResource.RequireMutableStorage`/retirement; texture allocation and
dynamic resize; renderbuffer allocation; framebuffer Configuration/PassMetadata/SurfaceMetadata/
Publication; `StaticGpuFramebuffer` frozen attachment references; `RenderPassTargets` pass references;
`RenderPassSequence` lifetime references; `GpuProgramLifetime` and compute scopes. Move their alias-wide
counters to the manager entry, retaining the same checks and releases. Declaration-only state remains local
until realization. Do not introduce a second mutation guard at the adapter boundary.

`GpuResourceManager` drains managed cleanup before native deletion, defers live foreign contexts and
abandons dead contexts. `RenderContextRegistry` identifies handle plus registration generation and emits
`Retiring`; absence of registration is not authority for persistent external tracking. Preserve the
context-free declaration path and existing shutdown exception policy.

## Metadata command matrix

Interception is restricted to the managed native owners GL, GL.Ext and GL.Arb.
The pinned OpenTK binding exposes core methods directly on GL; adapter classes named Core
refer to that owner. All other auxiliary owners are excluded, including Amd, Ati, Ibm, Intel,
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
Acquisition accepts a manager-executed typed reader that validates native kind and complete facts;
the buffer reader provides native acquisition coverage for this foundation. Other per-kind native
readers accompany their wrapper/command integration. No existing consumer opts into tracking yet.
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
| Buffer / `GpuBufferMetadata` | Actual byte capacity and usage; `sizeBytes/usage` become accessors in `GpuBufferObject`, inherited by VBO/EBO/UBO/SSBO/atomic/indirect/pixel pack/unpack wrappers. | Intended target, requested allocation usage, EBO index type/count, queued uploads, mapping scopes, growth policy and labels remain local. No content or mapping-ownership mirror. | B1, I1, L1 |
| Renderbuffer / `GpuRenderbufferMetadata` | Width/height/format and actual samples; `storage/width/height/samples` become accessors. | Ownership and labels remain local. | R1, L1 |
| Framebuffer / `GpuFramebufferMetadata` | Immutable attachment descriptors keyed by slot: kind/ID, mip, layer/face, layered state, multiview base/count and proven ordinary/multiview mode (unknown when native queries cannot establish it); attachmentless default dimensions/layers/samples where applicable. Image facts resolve through backing entries. | Retained image wrappers/ownership, frozen locks, dirty/publication state, viewport fallback extent and surface metadata stay local. The wrapper dictionary reconciles from descriptors rather than independently defining native facts. Selected range/replay policy remains on the image relationship. Pass extent/format are derived; routing stays in existing StateCache routing cache. | F1, backing T/R changes, L1 |
| VertexArray / `GpuVertexArrayMetadata` | Empty struct: `GpuVao` has no retained native layout fields. Entry revision invalidates represented helper assumptions; no full attribute mirror. | `GpuVertexAttribBinding` is an authored value helper, not a resource. Capture owning-wrapper identity and manager-owned revision for its facts; after mutation, resolve the selected current buffer/offset/stride/divisor before fallback reuse or reject unavailable facts. Vertex contents stay external. | V1, L1 |
| Program / `GpuProgramMetadata` | Native executable/interface availability and immutable native interface facts used by existing preparation, including queried resource names/properties, block stage flags, atomic-counter buffer descriptors and sampler/image unit assignments. `GpuProgramObject` and `GpuComputePipeline` share this type. Reflected facts no longer have independent authoritative mutable copies in `bindingCache/programLayout`. | Existing `GpuProgramLayout` remains a derived prepared interface combining native facts with authored contract/policy, stamped with entry revision. Authored names are separate from native names. Settings, callbacks and context stay local. Owner installation revision remains local across replacement objects; native relink revision is entry-owned. | P1, L1 |
| Shader / `GpuShaderMetadata` | Fixed shader type; `shaderType` becomes an accessor. | Authored BindingContract and compilation inputs stay local; no mutable storage descriptor. | acquisition, L1 |
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

### Manager API and generic base

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
  A generated but unrealized external name is insufficient. External texture/renderbuffer wrapper
  construction explicitly refreshes an existing entry until its complete mutation observation is
  qualified; retaining a registry entry alone does not qualify persistent consumer metadata.
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
Explicit StateCache operations own multi-command allocation, mip-range changes and attachment replay.
Internal steps receive their operation state explicitly. The manager commits a supplied complete result
once; publication never silently stages, and disposal never performs recovery. A temporary execution
guard rejects unrelated same-resource reentry before dispatch, while deletion cancels original completion.
Local operation admission withdraws the wrapper snapshot through manager services. The guard stores no
metadata or alias recipients; local tools introduce no persistent registry. Texture and framebuffer
operations explicitly invoke manager recovery services while their existing readers remain necessary.
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
StateCache owns native mutation execution and sends observed results to manager publication. Wrappers
never construct metadata candidates or call mutation refresh/publication themselves. Initial native
acquisition and selected-image projection also belong to the manager, including tools without a registry.
Requested buffer usage is separate allocation policy; requested texture dimensions/format/mips are
command arguments until dispatch; the manager then publishes them under the controlled-input policy.
Manager publication coordinates dependent observers outside the generic assignment.
No abstract per-wrapper metadata-query handler remains.

CPU declarations retain requested creation parameters locally until native realization. Register in the
actual owning context. Default framebuffer zero stays on surface publication with no native entry.
Program installation transfers the candidate association without duplicate registration. Unregistered
or offline paths remain conservative until a live manager exists; a thread-local StateCache does not
confer persistent tracking. A fresh registered context lifetime requires a fresh manager/registry;
never retarget an existing manager. Preserve existing RenderContextRegistry checks at the manager
boundary, not in resource keys. Subscribe once per manager to context retirement, unsubscribe on shutdown.
Native acquisition, wrapper initialization and metadata publication occur on the owning context thread.
Pure registration lookup and recipient removal support concurrent callers. Terminal context retirement
and late admission reconcile through atomic map operations and registration state. Worker retirement
marks the original entry atomically unavailable before queuing native work. Manager-side association
checks observe retirement without wrapper notification handlers or GL calls. Existing exception policy remains.

Metadata readers select named queries only when the cached DSA capability permits them; otherwise
known targets use existing binding scopes. Immutable-storage/view parameter queries use their own
cached capabilities. Before texture-view
queries are supported, immutable level counts come from the queried allocated image chain. External view
acquisition validates discovered ranges; controlled view creation validates its inputs against known
source metadata and derives the result without a full reader snapshot. Context-local views retain an opaque storage certificate published by the
manager on the texture owner. Metadata-only failure and recovery preserve this certificate independently
of snapshot availability; completed or interrupted storage updates replace it even when dimensions match.
Views reject unavailable source image facts until refresh succeeds. No additional registry or generic-base
tracking is used. SetMipRange preserves best-effort native
dispatch handling but propagates authoritative publication failures and leaves metadata unavailable.
Texture command paths retain their established target,
and managed aliases reuse the canonical target. An arbitrary unregistered external texture whose
target is unknown remains unavailable without named target discovery; do not probe targets with
invalid binds or claim that forced bound-path tests qualify that external boundary. Selected image
factories use manager acquisition/projection, and managed texture views reuse their backing entry.
A generic BorrowedTexture cannot represent texture-buffer storage; reject that acquisition before
changing the existing buffer-view reader, snapshot or availability. GpuBufferView remains its typed API.
Owning construction cleans up newly allocated names through StateCache if initialization or metadata
acquisition fails, preserving the original error and originating context. Borrowed acquisitions retain
external deletion authority and never use owning construction cleanup.
### Publication, failure and validation

Context-local tools without a registration discard uncertified snapshots after a failed read or an
interrupted explicit operation. Empty descriptors represent unavailable facts, not native zero-sized storage.
The manager retains only the established texture target for explicit native refresh (for example,
SetMipRange); image binding rejects an empty image snapshot. Local framebuffer image validation projects
available backing facts and recovers only unknown/invalidated backing before dependent use; failed
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

`EngineStateTargets` scans only VintagestoryLib, VSEssentials, VSSurvivalMod and VSCreativeMod. Its IL
discovery recognizes supported GL member references and closed generic MethodSpecification tokens.
Generic containing engine methods remain rejected. `EngineStateCallMap` retains exact static state/
deletion mappings, with those mappings taking precedence over metadata adapters. Ordinary C# adapters
under `HarmonyPatches/NativeAdapters` explicitly forward every supported metadata signature, including
generic upload definitions. `MetadataAdapterCatalog` declares each OpenTK owner/adapter type pair;
registration validates exact names, managed types and generic constraints. `EngineMetadataCallAdapters`
closes compiled definitions over arbitrary legal caller types without emitting IL or interpreting
command/parameter names. Payload forwarding neither boxes nor copies upload arguments; core, ARB and
EXT overloads share StateCache's captured-entry observation.
Renderbuffer storage uses the same exact-signature dispatch, including EXT coverage-sample storage.
Texture image definitions and mip generation resolve bound targets, explicit names and EXT texture
units through StateCache. Cube faces resolve their cube object; proxy targets never select an object.
Texture-view creation observes the destination's registered identity and reads its native level/layer
selection; it does not publish a storage mutation to the source texture's registration.
Image acquisition limits view-relative mip queries to that exposed level count, independently of the
source allocation's immutable level count.
Texture parameter overloads refresh represented base/max levels. Subimage, copy-subimage and
compressed-subimage writes change contents only and have no metadata adapters. Explicit mip
generation remains observed.
Mip generation on immutable storage is content-only. Mutable generation compares native image
descriptors and advances storage revisions for changed allocation. Image dependents are notified for
that change or recovery from unavailable metadata; regenerating existing levels preserves storage certificates.
Untracked commands execute without creating registry entries. Native results refresh existing aliases
once, including after rejected allocations, while preserving native errors and binding effects.
Manager publication invalidates associated framebuffer-image dependents after snapshot assignment or
refresh failure. Local image tools and native-name replacement retain their wrapper observer route.
Buffer texture availability includes its tracked backing buffer's metadata availability. Successful
backing storage publication advances associated texture storage revisions while retaining their
whole-buffer convention or explicit range. A texture withdrawn for another reason is not recovered
by a backing resize; deletion continues to withdraw its association before native-name reuse.
Publishing a buffer texture acquires its concrete backing dependency in the same registry, including
when the caller supplied a raw buffer ID. This adds neither a buffer wrapper nor native ownership;
an existing usable backing entry requires no further native acquisition.
Buffer-texture association adapters preserve bound, named and explicit-unit selection across core,
ARB and EXT calls. Native backing/format queries and range admission facts certify the result;
rejected commands preserve the previous certified descriptor. Whole-buffer mode comes from the
accepted command, not equality with an explicit full-range attachment. Range arguments retain
pointer width in both native adapters and the owning wrapper's bound fallback.
Framebuffer attachment/default adapters capture the selected existing registration and refresh
canonical descriptors after exact native dispatch. Readers use named attachment queries with DSA,
preserve split read/draw bindings on fallback, and gate attachmentless/multiview queries by capability.
Native configuration notifications invalidate retained helper/completeness state; managed helper
updates notify aliases while preserving the initiating helper's image ownership and completion event.
Named and pointer routing adapters withdraw the existing draw-route cache without changing bindings
or assuming success. Subsequent capture reads actual routing without consuming native errors. Read
routing has no cached mirror and remains a native selector, not framebuffer resource metadata.
OVR selected-image helpers preserve proven attachment mode and range on replay; ambiguous external
acquisition retains an unknown mode and rejects replay that cannot preserve its semantics. The API
matrix records supported families and native execution limits independently of installed engine callers.
Third-party mods and dynamically emitted callers are outside the assembly interception boundary.
External calls through excluded auxiliary owners likewise have no continuing observation.
Direct VGE multiview commands remain explicit StateCache operations with publication; their shared
metadata, capabilities and replay helpers are retained independently of engine interception.
Persistent consumers must establish that subsequent mutations/deletions stay inside covered routes.

Installed engine IL already confirms `ClientPlatformWindows.CreateFramebuffer` and
`SetupDefaultFrameBuffers` use the pointer `TexImage2D` signature and `FramebufferTexture2D`;
setup also uses `TexImage2D<float>(..., float[])` and `BufferData(BufferTarget,int,IntPtr,BufferUsageHint)`.
`DisposeFrameBuffers` uses single `DeleteTexture(int)` / `DeleteFramebuffer(int)`; `DisposeFrameBuffer`
uses the latter. `SystemRenderOITLayers.BeforeOIT.rebuild` uses pointer `TexImage2D`/`TexImage3D` and
`FramebufferTexture2D`/`FramebufferTextureLayer`; `freeResources` uses single texture deletion.
Reconcile all of these, including liquid-depth rebuild, before persistent depth acquisition.

Closed generic upload overloads resolve through their exact constructed MethodInfo, preserving their
native marshalling and constraints. This supplies the common discovery mechanism for installed calls
such as `TexImage2D<float>` and `TexImage2D<int>`.
Buffer and texture commands use that mechanism, including callers containing only generic native calls.
Nested extension deletion uses exact declaring types/signatures within the same assembly boundary.

Cloud `CloudNativeMapAdapter` validates native map storage at allocation and keeps publication/world/
upload/generation certificates. `CloudStoredVolumeConsumer` currently constructs map, material, primary
visibility and liquid-depth borrowers per use. Material depth is already owned and should be shared
directly. `DepthHierarchyRenderer` retains a primary-depth borrower until resize/world retirement;
`ScreenResourceManager` invalidates state and invokes ordered consumers after framebuffer rebuild.
These intended consumers may gain persistent tracking only after their exact allocation/mutation/
deletion signatures are adapted and exercised through actual redirection. Allocation hooks retain any
content-certificate responsibility; they are not the common identity mechanism.

Unsupported third-party raw IDs keep current conservative validation/invalidation. This is a deliberate
external boundary, not permission to omit the installed engine's required cloud/depth paths. Native
signatures are preserved in [resource-command-inventory.txt](../artifacts/gpu-resource-handles-baseline/resource-command-inventory.txt)
(VGE, CloudRendererMap and ClientPlatformWindows, with adapter/discovery flags) and
[engine-gl-inventory.txt](../artifacts/gpu-resource-handles-baseline/engine-gl-inventory.txt)
(broader engine callers plus built-in assembly references). `target=True` means the containing method
is discovered for some supported call; it does not mean its unadapted mutations are intercepted.

The retained baseline inventory records `CloudRendererMap.makeTexture` using pointer `TexImage2D`
(adapter false, target true at that baseline);
`FreeGlResources` has four single `DeleteTexture(int)` calls and one `DeleteFramebuffer(int)`
(adapter and target true). Texture allocation and framebuffer attachment now have family adapters;
consumer integration still requires their current native-path verification. These use the common
deletion path and do not require another producer teardown hook.
The VGE IL confirms `NamedBufferData(int,int,IntPtr,BufferUsageHint)`, both buffer-texture range
signatures in the matrix, and `BufferStorage(BufferTarget,int,IntPtr,BufferStorageFlags)`.

## Validation and restoration dependencies

Controlled program, VAO and pipeline selections retain submitted identities after dispatch and suppress
known-equal calls. Exact external selectors execute each original overload and leave affected selections
unknown for lazy recovery; querying CURRENT_PROGRAM does not certify full-program stage installation.
Selector and routing execution resides in `Rendering/Commands`, behind compiler-checked native adapters.
Framebuffer scopes capture only their affected read/draw directions through cached getters. Element-buffer
scopes retain the original VAO association when another VAO becomes selected before restoration.
Per-VAO element-buffer knowledge is independent of canonical VAO configuration revisions.
Vertex-binding helper values retain established fields and acquire only requested unknown fields after
an observed revision change. Context-local helpers use one conservative cache epoch instead of a second
registry; observed vertex changes or buffer/VAO deletion expire those local facts. Non-DSA authored
descriptions reject stale reuse. The resource wrappers and canonical empty VAO schema remain unchanged.

`ShaderPreparedSubmission` checks lifetime each use; `ShaderInputValidation` keys already contain wrapper,
numeric ID, target/format/dimensions/mips or view/range/capacity. They lack registration revision and assume
wrapper fields are current. Raw sampler submission checks `IsTexture` and invalidates its unit before
binding. Typed publication can suppress known-equal binds. Preserve both paths until coverage permits migration.

`StateCache.TextureLifetime` records boundary retirement, invalidates image slots and resets matching
texture bindings after deletion. `StateCache.ResourceSlots` keys all image view parameters and indexed
buffer ranges. `StateCache.BoundaryBindings` captures/reconciles incoming state and refuses restoration
of retired names. Context retirement and unknown-state recovery remain independent of resource validity;
a live registration is never evidence that an object is bound. Review source as authoritative over the
historical findings in `Rendering.ShaderBindingState.md`; its Prepared resource submission section
describes the current validation path.


## Reproducible baseline

Source HEAD: `35e1481c9d6f94ef8de4fdded5499bccf6a11867`; runtime implementation unchanged.
Applicability rechecked for the manager-owned metadata plan: all 40 artifact-manifest entries and
2,701 source-manifest paths match their recorded hashes; runtime/build sources match the baseline.
The retained 105-case and 3-case test receipts and package/test shader identities remain valid.
No baseline rerun was needed for documentation-only reconciliation. New registration/publication costs
remain implementation measurements, not costs claimed by these baseline fixtures.
[Baseline artifacts](../artifacts/gpu-resource-handles-baseline/) retain the source archive, final test
instrumentation patch/source manifest, full test-output ZIP, canonical package ZIP, binary/package
SHA256 manifests and exact fixture outputs. [replay.ps1](../artifacts/gpu-resource-handles-baseline/replay.ps1)
records the commands and process-only observation settings. Use the retained baseline source plus
instrumentation for comparison, not a later checkout's runtime DLL. Restore dependencies before using
its `--no-restore` test command. SDK: .NET 10.0.401; Release; Windows x64; RTX 4090; reported GL 4.3.0,
NVIDIA 610.88. Binary identities include the installed engine, API, VSEssentials and OpenTK.

Subagent verification: [focused suite](../artifacts/gpu-resource-handles-baseline/baseline-existing.trx)
105 passed, zero failed/skipped; [final calibrated fixtures](../artifacts/gpu-resource-handles-baseline/baseline-final.trx)
3 passed, zero failed/skipped. [Canonical package](../artifacts/gpu-resource-handles-baseline/package.log)
used `./build.ps1 --configuration=Release --spirv=true` and passed. Packaged runtime SHA256 matches tested
`A5D61F8844D81D1C2E1C5F14C5813F26E793452E6308AAA12100742468116176`; all 761 SPIR-V files match test outputs,
and 762 shader/manifest entries match canonical generated assets. The final test DLL is
`D5ADB644588F1853238C851C7DB33DC27597B56DC345B673C83092B6AD2066A8`.

| Measured interval | Native texture binds | Texture metadata queries |
| --- | ---: | ---: |
| Cloud map acquisition | 5 | 12 |
| Eight stable cloud publication generations | 0 | 0 |
| Four cloud input borrower constructions | 0 | 24 |
| Typed cloud view draw | 68 | 0 |
| Depth first publication | 4 | 6 |
| Depth stable publication | 2 | 0 |

Each interval has a named `.counts.txt` receipt. `ResourceCallObserver` calibrates all four intercepted
OpenTK entry points: BindTexture and the bound/DSA texture-parameter/level queries. Counts cover those
entry points, not all GL queries, IsTexture checks, FBO queries, CPU costs or whole-frame time. No
registration/dispatch implementation exists in this baseline; later measurements must report its cost
separately. Calibration restores its native binding and is excluded from measured intervals.

The cloud fixture uses a 20x20 map and 18x14 view with authored stored fields, constructs the four
borrowers and runs actual typed view submission. Its six output images exactly match the owned-input
fixture; draw/read FBO, active unit and texture binding are restored. `cloud-typed-view-output.f32`
retains 20,160 bytes. This is not a full live CloudStoredVolumeConsumer scheduler measurement.
The depth fixture runs the actual renderer with a 7x3 target, checks complete mip chains of 0.4 then
0.7, and retains two 100-byte `.f32` outputs. Existing engine boundary/FBO tests provide complementary
restoration/rebuild coverage. None of this establishes game appearance or frame-time improvement.

Initial sandboxed restore/test attempts stalled and were canceled; successful runs used the available
restored packages with process-only observer settings. Logs also preserve corrected test-only compiler
failures (source removal during enumeration and an ambiguous Buffer type). The final candidate passed;
no runtime baseline failure was observed in the selected checks, and no failing test was waived. The removed call-site inventory
fixture's historical output is optional integration context, not required API coverage evidence or a
fixture to restore in the final replay.
