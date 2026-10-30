# RHI migration units

## Resource-change publication migration map

Planning map for [Rendering.ResourceChangePublication.todo](Rendering.ResourceChangePublication.todo),
Phase 1; production behavior below is the inspected baseline, not an implemented completion claim.
The approved [publication proposal](Rendering.ResourceChangePublication.Proposal.md) controls the replacement.
Source paths below are relative to `VanillaGraphicsExpanded/Rendering/`; backend paths start at
`RHI/Backends/OpenGL/`. Verification entries describe required focused cases, not executed tests.

### Authority and traceability

| Plan task group | Consulted controlling sections | Requirement and evidence |
| --- | --- | --- |
| Producers and completion | Publication proposal: Contract, One completion result, Publication and delivery order; `Rendering.GpuCommands.Contracts.md`: Authority and ownership, Internal resource execution; `Rendering.GpuResources.md`: Wrappers and initialization, Command results and queries | Manager owns facts and completion; backend owns execution. Source routes below identify every direct entry publication and snapshot-assignment family. |
| Recipients, families and revisions | Publication proposal: Equality and resource coverage; resource contract: Explicit acquisition and image dependencies, Maintained image facts, Shader executable ownership, Binding knowledge and retirement; `Rendering.ShaderBindingState.md`: Prepared resource submission; `Rendering.GpuResourceHandles.Inventory.md`: Canonical metadata schemas and field migration | Fourteen registration kinds, selected-image projections, original identity and query-free validation. Family and reader tables below retain backend state ownership. |
| Owner events and lifetime | Publication proposal: Publication and delivery order; RHI proposal: Resource creation and adoption, Shader and pipeline objects; contracts: Executable and pipeline contracts | Explicit owner completion survives equal facts; borrowers and fixed executables never follow reused identities. Lifecycle routes and callback ordering below specify the coupled migration. |
| Inherited constraints | `Rendering.OpenTKBindingInterception.Proposal.md`: Dispatch design, Generated callbacks and handwritten observers, Responsibility boundaries; archived `3f307365:docs/Rendering.CommandPublication.Proposal.md`: Operation-owned publication and Shared commit and interruption contract; archived `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md`: Identity and metadata, Binding and selection | Native observers never veto/retry/throw to callers; managed composition commits once; consumer refs add no registry. The newer RHI/publication proposals supersede old execution placement and benchmark language. |
| Completion boundary | Parent `Rendering.GpuCommands.todo` 9.1 completed; 9.2 approved design and dedicated plan | This map closes only the inventory step. Implementation, focused execution evidence and parent 9.3 acceptance remain open. |

### Producer and recipient closure

| Current route | Replacement owner and preserved behavior | Focused verification |
| --- | --- | --- |
| `GpuResourceMetadataEntry.Publish/Associate/AssignSnapshot`, `GpuResource.Metadata.HandleMetadataChanged` | Entry classifies facts/effects and commits revisions; manager-internal snapshot assignment is separate from the shared resource callback. Keep weak membership, disposed-recipient pruning and initial/missing-recipient delivery even for equal facts. | CPU: equal separately allocated collections, initial publication, late association, missing projection, disposed recipient. |
| `RHIResourceManager.Creation.CreateResource`; `BufferLifecycle`, `ImageLifecycle`, `ExecutableLifecycle`, `EmptyLifecycles`, `SynchronizationLifecycle` | Same completion for supplied creation/defaults; preserve publish-before-owner-association and private identity handoff. Gen reservations are not usable merely because their empty schema compares equal. Initial pre-install/EXT provenance and realization without complete facts remain distinct. | CPU family matrix: reserved, realized, first available, equal repeat, import success; candidate failure remains distinct from post-commit callback failure. |
| `MetadataPublication.RefreshMetadata/PublishMetadataCore` | Common manager classification, dependency reconciliation, snapshot commit and notification. Retain existing authorized reader gates, framebuffer mode reconciliation, original-context checks and pending effects. No query for equality. | CPU: unchanged refresh, changed facts, equal-descriptor Storage/Executable, failed recovery, successful recovery with accumulated effects. |
| `MetadataOperation.Commit/Dispose`, `OperationPublication`, `MetadataObservation`, `MetadataAcquisition.RecoverWrapperMetadata` | Carry generic initiating owner and previous committed facts/visible availability through the existing operation. Mark commit successful before any consumer code. Local supplied snapshots use the same rules without another persistent registry; retain explicit local-buffer recovery and local texture storage identity. | CPU: successful callback throws then Dispose, dispatch throws, caught interruption, unchanged success without availability event pair; local owner recovery. |
| `ProgramFields.PublishProgramAssignment` directly calls `entry.Publish` and program notification | Route assignments through common completion without losing unit/block patching or pending executable effects. `OpenGLProgramAssignments.Execute` currently withdraws preparation before execution; successful exclusion must not itself emit an availability pair. | CPU plus focused native assignment: equal units/block binding, actual assignment, failure/recovery, equal-interface executable replacement. |
| `OpenGlStateCache.NativeFramebufferMetadata`, `NativeProgramMetadata`, `NativeBufferStorage/Binding`, `NativeRenderbufferStorage`, `ImageDefinitions`, `NativeConflicts`, `VertexBufferRealization`, `QueryLifecycle`, native `Auxiliary/QueryNames` and `Textures/TextureView` | All direct BeginRefresh/withdrawal sites feed manager transition classification. Keep native acceptance/interruption and selector knowledge in their backend owners; rejected calls retain facts. `OpenGLVertexConfiguration` and `OpenGLProgramAssignments` retain synchronous exclusion but use the same completion semantics. | Focused native: established/rejected/uncertain effects, exactly one dispatch, nested mutation, no extra acquisition. CPU: final withdrawal vs temporary exclusion. |
| Entry `NotifyImageDependents/SetImageRecipientAvailability/ClearImageRecipientSnapshots`; `ImageMetadata`, `ImageAcquisition` | Assign all affected `GpuImageMetadata` projections before callback delivery. `GpuFramebufferAttachment.NotifyDependents` currently fans out to weak framebuffer observers; preserve that ownership while classifying projection/backing changes once. Missing images become unavailable without invalidating successful owner storage. | CPU: same dimensions with changed backing storage; missing selection; several framebuffers sharing an image; one failing recipient does not starve others. |
| `OwnerImages.PublishOwnerImages/PublishOwnerImage`; backend `OpenGLBackend.TextureOwners/TextureStorage` | Retain ConditionalWeakTable owner-to-weak-image relationships for local tools and intentional owner replacement. Existing same-entry exclusion prevents duplicate delivery and must survive consolidation. Registered projection uses available facts; local projection uses supplied snapshots. | CPU: local owner, replaced owner, image already delivered by entry, projection error after committed allocation. |
| Entry `NotifyProgramDependents` -> `GpuProgramObject.OnNativeProgramMetadataChanged` -> `GpuProgramLayout.ObserveNativeMetadata` | Shared resource callback drives preparation withdrawal/rebuild or fixed-executable invalidation. Preserve attempt-all-recipient behavior and original failures. Remove caller-owned program notification from normal publication, assignments and native uncertainty. | CPU: preparation unavailable/recovered; native integration: same-interface relink invalidates old shader/PSO. |
| Backend `OpenGLBackend.ProgramLink.SubmitProgramExecutable/CompleteProgramLink` directly invoke `OnNativeProgramMetadataChanged` when no entry exists | Replace these local withdrawal/completion calls with the same manager-owned local completion classification. Preserve deferred submission, consumed-once completion, pending-owner supersession and driver-produced interface output; do not remove local preparation notification. | CPU/local integration: pending, failed, completed and superseded link; equal-interface replacement still invalidates preparation. |
| Entry `NotifyFramebufferDependents/NotifyFramebufferBackingRetired`; `OperationPublication`, native framebuffer observation, `DeletionDependencies` | Shared framebuffer reaction clears retained descriptor reconciliation and emits only meaningful canonical changes. Preserve distinction between observed configuration and retired backing that must not cancel pending owner reattachment. Initiating-owner policy suppresses only duplicate explicit completion, not other aliases. | CPU/native: equal attachment definitions, changed original backing, alias notification, bound automatic detach vs unbound retained identity. |
| `BufferTextureDependencies/RenderbufferTextureDependencies`, `TextureBackingIdentity` | Existing original backing association remains authoritative. Propagate storage/availability through common classification even if dependent texture descriptors are unchanged. Replace recursive callback delivery with completion-local dependency reconciliation before callbacks; no general scheduler. | CPU/native: same-shape backing replacement, failed backing, recovery, stale original registration under same name. |
| `NativeTextureViews`, `TextureViewSources`, `TextureReferences` | Retain original source registration, cumulative selection and StorageRevision/local storage certificate. Source withdrawal/recovery updates projections; source replacement retires validity of old certificates rather than retargeting. Finish nested-view facts before notifications. | CPU/native: nested view chain, ordinary borrower of a native view, equal projection with changed source storage, name reuse. |
| `Registry.RetireResource/ObserveDeletion/RetireMetadataRegistry`, `DeletionDependencies`, `ResourceLifecycle.TryScheduleResourceRetirement`; creation-failure invalidation | Capture recipients before ClearAssociations, close usability immediately, classify terminal delivery once for the original lifetime. Preserve native bound/unbound deletion semantics and original-registration queue checks. A later delete must not repeat scheduled retirement. | CPU: immediate/queued/native/context retirement, late association race, duplicate terminal request, reused name. |

Direct entry `Publish` calls are confined to `MetadataPublication` and `ProgramFields`; direct snapshot
assignments additionally occur in creation, local recovery/discard, operation publication and image
projection/owner fallback. These are the source closure, not permission to leave fallback notification
routes alongside the new path. Preserve direct available-fact lookup as lookup, not as a new producer.

### Resource-kind coverage

Canonical structs reside in `Metadata/`. Lifecycle sources named below are manager partials; all kinds
also use the generic association, availability and terminal completion contract.

| Kind | Canonical struct | Producer/dependent scope |
| --- | --- | --- |
| Buffer | `GpuBufferMetadata` | BufferLifecycle, owned storage, observed storage; capacity/usage/access, buffer-texture backing, range validation. Content uploads are excluded. |
| Texture | `GpuTextureMetadata` | ImageLifecycle, image definitions/storage/mips/base-max, buffer/renderbuffer associations, native views; selected images, source certificates, streaming and shader validation. Include all texture wrapper variants. |
| Renderbuffer | `GpuRenderbufferMetadata` | ImageLifecycle and storage; selected images and renderbuffer-backed textures. |
| Framebuffer | `GpuFramebufferMetadata` | ImageLifecycle, managed configuration/import, attachment/default observation and deletion dependencies; retained helpers/pass targets. Read/draw routing remains cache state. |
| Shader | `GpuShaderMetadata` | ExecutableLifecycle and owned stage creation; stage/lifetime only. Compile results remain backend outputs. |
| Program | `GpuProgramMetadata` | ExecutableLifecycle, link/binary completion, ProgramFields; interface/prepared layouts and fixed shader/PSO validation. |
| VertexArray | `GpuVertexArrayMetadata` (empty) | EmptyLifecycles and managed/native configuration admission; lifecycle notifications only. Geometry configuration certification uses the existing backend epoch (reader correction below). |
| TransformFeedback | `GpuTransformFeedbackMetadata` (empty) | EmptyLifecycles; realization and lifetime. Indexed binding/capture state remains backend-owned. |
| Sampler | `GpuSamplerMetadata` (empty) | EmptyLifecycles; realization and lifetime. Sampler parameter knowledge remains backend-owned. |
| ProgramPipeline | `GpuProgramPipelineMetadata` (empty) | ExecutableLifecycle; reservation/realization/lifetime. Native stage selections remain backend-owned; managed PSOs are not this registered kind. |
| Query | `GpuQueryMetadata` (empty) | SynchronizationLifecycle, query-name and QueryLifecycle admission; lifecycle only, not execution revision or result readiness. |
| Sync | `GpuSyncMetadata` (empty) | SynchronizationLifecycle; nonzero returned fence and retirement; wait/result readiness stays in synchronization services. |
| Semaphore | `GpuSemaphoreMetadata` (empty) | SynchronizationLifecycle; generated reservation vs established import usability, preserving the authorized targeted import check. |
| MemoryObject | `GpuMemoryObjectMetadata` (empty) | BufferLifecycle memory-object creation/import; lifecycle, not inferred opaque payload validity. |

`GpuImageMetadata` and `GpuImageSelection` project Texture/Renderbuffer facts, never a fifteenth entry.
Structural equality/hash work is confined to collection-bearing Texture (mip/face), Framebuffer
(slot dictionary, including OriginalBacking) and Program (interface/index and unit location) records;
all scalar fields remain included. Default/unavailable differs from known-empty. Backing registration
and storage certificates outside those records participate in dependency classification separately.

### Revision and guard readers

| Reader/source | Required disposition and focused case |
| --- | --- |
| `GpuResourceMetadataEntry.Publish`, pendingChange, `GpuResourceRegistration` | Replace effect-flag increments with one revision per actual committed transition; preserve Storage/Executable evidence and uncertainty. BeginRefresh exclusion alone is not a change. Test equal publication and failed/recovered effects. |
| `Registry.AcquireResource/RecoverResource`, `RetainInitialCandidate/DiscardInitialCandidate`; `OpenGLTextureQueries`, `NativeBufferTexture/Storage`, `NativeFramebufferMetadata`, framebuffer availability checks | MetadataRevision zero currently means no completed initial facts. Availability/retirement revisions must not let zero/nonzero shortcuts certify unresolved initial facts. Use the existing explicit realization/discovery/available-fact distinctions in these predicates; test withdrawal before first publication and EXT initial candidates. |
| `ShaderInputValidation`, `GpuTexture.StorageGeneration`, `TextureStreamingManager` queued request checks; `PBR/Clouds/CloudNativeMapAdapter` (relative to project root) retained storage revisions | Preserve identity, availability and metadata/storage comparisons. Equal republication keeps compatibility history; replacement still rejects stale streaming/storage work and cloud publication reuse. |
| `GpuProgramLayout.NativeMetadataRevision` and InstallCandidate; `GpuProgramObject`, `RHIShader`, `ExecutableReferences.RequireFixedExecutable`; backend `ProgramBindingAssignments`, pending `ProgramLink` | Fixed shader captures existing metadata revision; actual interface/assignment change still rejects it, equal republication does not. Executable effects always invalidate preparation even with equal interfaces. Pending links and assignment completion retain original identity/revision/context rejection. Do not create another executable generation token. |
| `RHIGeometryImport.IsCurrent`, backend `GeometryVertexArray.Matches`, manager `GeometryAcquisition` | Tracked paths currently rely on empty-schema metadata increments, while local paths check configuration epoch. Check existing `RHIGeometryContext.ConfigurationEpoch`/cache `GetVertexConfigurationRevision` for both paths, plus original registration, availability and context. Preserve acquisition before/after certification. Test real VAO mutation rejecting tracked imports/cached arrays while equal metadata alone does not. |
| Backend `Passes/OpenGLPassTarget`, `OpenGLAttachmentIdentity`, `OpenGLBackend.PassCompleteness`, `RenderPassTargets`; `GpuFramebuffer.AttachmentRevision` | Metadata revision certifies canonical configuration; storage identity certifies attachments; explicit owner AttachmentRevision still reports rebuild/import completion. Keep route/configuration cache ownership independent. Test unchanged metadata reuse versus explicit owner event and changed backing. |
| `TextureViewSources/TextureReferences`, `NativeTextureViews`, `TextureBackingIdentity`, backing dependency publication | Keep original registration and StorageRevision (or existing local storage identity); no numeric-name recertification. Test nested aliases and reused names. |
| `MetadataOperation`, `MetadataPublication.BeginMetadataCompletion/RequireUninterruptedCompletion`, `OpenGlStateCache.NativeConflicts` and `IRHIResourceOperation` | Keep operation identity/context/exclusion and CompletionInterrupted/PendingChange. Snapshot commit precedes callbacks; post-callback identity/revision/availability checks stop stale delivery after native reentry. Test managed rejection, native non-vetoing mutation and callback retirement. |

PendingProgramLink currently captures its revision before submission withdraws availability. Once an
actual availability transition advances the revision, capture the expected post-submission transition
under that existing pending owner; otherwise its own withdrawal would reject every completion. Preserve
rejection of subsequent unrelated mutation, retirement and superseding requests rather than dropping
the revision check. Verify both ordinary completion and a genuinely stale pending link.

### Owner events, teardown and observed defects

`GpuFramebuffer.Configuration.SetAttachmentCore/RemoveAttachmentCore/ResizeCore`,
`GpuFramebuffer.Publication.CompleteExternalPublication`, and detach/dispose retain explicit owner events.
`GpuFramebuffer.SurfaceMetadata.PublishSurfaceMetadata` also emits an explicit window-surface event;
framebuffer zero stays unregistered. `PBR/SceneColor/SceneColorParticleTargets` (project-root path)
subscribes `Invalidate` to `AttachmentsChanged` and unsubscribes on Dispose, expiring retained particle
capture targets. This ordinary owner event is not weak; preserve its explicit unsubscribe contract.
The image-to-framebuffer observers and canonical recipients, in contrast, are weak.
`GpuFramebuffer.OnAttachmentChanged` guards disposed/unselected images and external publication;
`resizing` coalesces completion. Carry the initiating owner through common completion, preserving these
guards and events even when imported selections compare equal. Other framebuffer aliases still react.
Images keep weak framebuffer observers; owner replacement can reproject owned selections, whereas
borrowed aliases/native views keep original associations. A dependency notification is not native reattachment.

`GpuResource.Dispose` can run off-thread and calls `TryScheduleResourceRetirement` before unregistering
the wrapper. `RHIResourceManager` already owns `GpuDeletionCommand` with original registration/context
and optional cleanup, `EnqueueContextCleanup`, and `DrainDeletionQueue`; `RHIResourceDisposalQueue`
separately transfers owned wrappers from finalization to render-thread disposal. Use this existing lifecycle
handoff for captured notification work, before destructive recipient clearing, rather than a new event
queue or worker-thread consumer call. Preserve registration checks at drain, live foreign-context deferral,
dead-context abandonment and CPU-only terminal invalidation. Context-affine callbacks must not execute
against a replacement/dead namespace; closure must release captured recipients when that context ends.
Do not retry native deletion because notification failed. Test retirement-before-drain and closure-before-drain.

Source-established gaps, rather than inferred runtime failures:

- Publish currently increments MetadataRevision from any nonzero effect and has no structural equality;
  normal completion notifies image/program recipients even for equal snapshots. Program preparation rebuilds
  via UseNativeMetadata. These are concrete unnecessary-change routes; no claim that every call duplicates events.
- MetadataOperation marks `completed` after publication callbacks. A thrown callback can therefore enter
  Dispose's unsuccessful-operation withdrawal after canonical commit. Commit status must be separated first.
- Buffer/renderbuffer dependency publication recursively invokes callbacks while sibling dependencies may
  still be unreconciled. Common completion must finish the affected dependency facts before notification.
- Retirement currently clears membership through Invalidate without a common terminal callback. View/deletion
  withdrawal paths update subsets; validation rejecting use is legitimate but does not replace notification.
- Existing weak OwnerImages same-entry exclusion, native dispatch containment, operation guards, backend
  configuration epochs and explicit owner events serve real contracts and must not be deleted as duplicates.

`Rendering.GpuResources.md` currently claims equal framebuffer definitions retain revisions and describes
the removed RG-to-RGBA texture-clear workaround. The former is an intended publication behavior not supplied
by the inspected baseline; reconcile it when the new contract is implemented. The latter is unrelated stale
qualification documentation for parent 9.3, not permission to restore the workaround. Neither changes the
approved publication scope or adds a design decision needed before implementation.
The canonical-schema inventory's VAO entry-revision wording describes the current coupling; reconcile
that wording with backend configuration-epoch certification during the dedicated plan's documentation
closeout. Empty metadata must not acquire invented configuration fields to preserve that older wording.

### Coupled implementation order and verification boundary

Implement value equality and completion classification first inside the single replacement unit, then
snapshot/dependency commit, committed-operation status, common recipient delivery and lifecycle handoff.
Migrate creation/recovery/assignments/native withdrawals, image/program/framebuffer reactions, local owner
fallback and all revision readers before deleting old notification entry points. In particular, migrate
tracked VAO epoch consumers with revision semantics, and preserve initial-publication predicates when
availability begins advancing revisions. No intermediate implementation is a separately accepted shim.

Required CPU cases are specified in the tables; `Unit/Rendering/GpuResourceMetadataRegistryTests.cs`
in the test project supplies controlled context identity without GL. Existing focused GPU anchors include
`GpuMetadataOperationFoundationTests`, `GpuResourceMetadataRecoveryTests`, `GpuFramebufferPublicationTests`,
`GpuFramebufferMetadataInterceptionTests`, `FramebufferAttachmentNotificationTests`,
`GpuProgramDerivedMetadataTests`, `GpuExecutableLifecycleTests` and `GpuGeometryAliasTests`.
Reuse applicable cases and add missing behavior checks, rather than duplicating entire fixtures.
Focused GPU cases cover observed attachment/storage changes, same-interface relink,
VAO configuration, original backing/name reuse and explicit owner events. No execution evidence is claimed
by this inventory. No production change, build, GPU run, benchmark or full-suite run is needed for Phase 1.
Implementation and qualification remain in dedicated Phases 2–3; parent 9.3 and 10 retain their boundaries.

Planning inventory for [Phase 1](Rendering.GpuCommands.todo), implementing the final
[contracts](Rendering.GpuCommands.Contracts.md) under the [approved proposal](Rendering.GpuCommands.Proposal.md).
The implementation checklist controls acceptance of each unit. This document defines replacement boundaries, not a
second execution design or a claim that existing APIs already satisfy the RHI.

## Source inventory and coverage

Source inspection covers production `VanillaGraphicsExpanded`, `ShaderContractGenerator`,
`ShaderBuildTool` and `SpirvBuild`, excluding generated `obj`/`bin` output. The retained static inventory
is `artifacts/rhi-contracts-baseline/production-sites.txt` (path, line and source),
`production-files.txt` and `family-files.txt` (initialization, native readers, exact adapters, command
modules, pipeline and uniform domains). Reproduce the site inventory from the repository root:

```powershell
rg -n --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' '(\bGL\.|\bStateCache\b|\bGraphicsCommandContext\b|\bGraphicsPipeline\b|\bGpuComputePipeline\b|\bGpuProgram\b|\bGpuComputeProgram\b|\bUniformPublication\b|\bShaderPreparedSubmission\b|\bGpuPreparedBindings\b|\bGpuVertexAttribBinding\b)' VanillaGraphicsExpanded ShaderContractGenerator ShaderBuildTool SpirvBuild
```

This is a caller/navigation inventory, not a supported-method whitelist or a new reflection test.
The [API coverage](Rendering.GpuResourceHandles.ApiCoverage.md) families B1/T1–T4/R1/F1/V1/P1/I1/L1
and their explicit exclusions define the native observation/execution obligations. Consult that matrix's
expanded command names and registry authority when replacing each family. Current native symbol/source
mapping and unobserved-name dispositions are recorded in `artifacts/phase8h/native-coverage.json`, with
the inspected registry input in `artifacts/phase8h/gl.xml`. The older baseline CSV is historical evidence,
not a required missing input. Current engine usage never narrows supported overloads.
Content-only operations still migrate execution but do not gain metadata refresh observers.

The [existing inventory](Rendering.GpuResourceHandles.Inventory.md#canonical-metadata-schemas-and-field-migration)
defines all canonical structs, fourteen native resource kinds and selected `GpuImageMetadata`
projection. The new contracts retain those schemas/authorities. Existing older-context branches in
inventory descriptions document current code, not requirements to preserve them under the 4.5 RHI.

Exact interception remains `HarmonyPatches/EngineStateCallMap.cs`, `EngineMetadataCallAdapters.cs`,
`NativeAdapters/MetadataAdapterCatalog.cs`, `EngineStateTargets.cs`, `EngineGenericStateTargets.cs`
and `EngineStateSwitchingHook.cs`. Retain compiler-checked static signatures and discovered concrete
value-type generic instantiations. Never patch open generic definitions. Only GL (Core), Ext and Arb
owners are included. Other auxiliary owners and legacy fixed-function/client-stack/display-list APIs
remain excluded. Explicit managed optional features with other origins do not widen interception.

## Shared dependency rules

Each row below owns its complete callers, creation, native execution, readers, exact overload forwarding,
retirement, tests and obsolete API removal. The listed files name source families; include all partials
and inherited callers from the retained inventory. Re-scan the unit before acceptance, comparing old
entry points with final routes. Family ownership partitions existing cache/manager services; it does
not copy them. Later consumers can call completed final operations directly while retaining their
unmigrated orchestration. No completed operation calls an old executor.

Infrastructure introduced with buffers is the final context/backend/creation machinery, not a buffer-only
context to replace later. Shader, PSO and pass contracts are fixed before command payload work. Unit E
is one acceptance gate: its work packages may be implemented in order, but none may use a temporary
current-program, graphics-pipeline or context facade. If an earlier unit cannot close without E, pull
the blocking caller/dependency into it or merge acceptance with E before implementation; never add a shim.

No unit defers obsolete overload removal to the final cache rename. Tests requiring forced non-DSA
execution are replaced in the owning unit with baseline named/state-preservation checks. Keep tests
for required mutable/bound semantics, external exact overloads and optional capability rejection.

## A — Admission, creation transaction and buffers (Phase 2)

- **Existing entry points:** `GpuBufferObject` creation/allocation/binding methods; `GpuBufferUploader.BufferData`,
  `BufferSubData`, private `TryNamedBufferData`, `TryNamedBufferSubData`, `CanUseDsa` and mutable fallback state;
  `StateCache.BufferCreation`, `BufferStorage`, `BufferContents`, `BufferNamedContents`, buffer binding/scopes
  and `Commands/BufferStorage`; manager `CreateBuffer`, generic `CreateResource` and `IGpuResourceCreation`.
- **Callers:** `GpuVbo`, `GpuEbo`, `GpuUniformBuffer`, `GpuShaderStorageBuffer`, `GpuAtomicCounterBuffer`,
  `GpuIndirectBuffer`, `GpuPixelPackBuffer`, `GpuPixelUnpackBuffer`, `GpuBufferView`; `CpuUniformBuffer`,
  `GpuUniformRingBuffer` and `Uniforms/*`; managed geometry allocation, `LumOnBufferManager`, scene/world-probe
  uploaders and cloud/depth storage. These callers retain their growth/role/content policy.
- **Final routes:** context admission and typed factory; internal owner allocation; semantic upload/copy/clear
  and map/flush/unmap services through backend core named operations. No generic target or native ID on the
  public path. Pointer-shaped mapping stays scoped immediate/internal or explicitly ready readback.
- **Manager/readers/retirement:** `GpuBufferInitialization`, `GpuBufferMetadata`, `GpuBufferMetadataReader`,
  buffer-texture backing notifications, original-entry deletion/queue services and buffer cases in
  `EngineStateDeletionCalls.Core/Arb`. Native queries move to backend services without moving acquisition
  policy out of the manager. Private recipient-bearing creation remains the final handoff mechanism.
- **Dependencies/removals:** bring cached capability projection, context identity and backend resource-operation
  admission into A. Remove named/bound retry state and old buffer native executors, preserving uploader policy
  helpers with revised final signatures. Uniform publication/geometry drawing remain in E; their buffer calls
  are migrated directly now. B's buffer-image relationships consume A's final buffer operations.
- **Verification:** `GpuBufferImageCreationTests`, `GpuResourceMetadataAcquisitionTests`,
  `GpuResourceMetadataPublicationTests`, `GpuEboIntegrationTests` and affected buffer/uniform/adapter tests;
  add focused ready/failure/candidate cleanup, content, sentinel selectors, no retry, capacity publication,
  omission/zero, original-context/name-reuse cases. Test receipts belong to A, not a future compatibility path.

### Buffer source closure

The implemented entry route is `RHICommandContext` -> `IRHIBackend` -> focused
`RHI/Backends/OpenGL/Buffers` modules. Existing subclasses remain the owners; typed factories privately
lower `RHIBufferDescription` into manager initialization/handoff. `GpuBufferRef` retains only the typed
resource, while the existing manager supplies live metadata. `GpuBufferMetadata` additionally represents
immutable storage/access flags so later write/map validation needs no descriptor query.

Removed APIs/files: all eight buffer-wrapper `Create` factories; uploader `BufferData/BufferSubData`;
`TryNamedBufferData/TryNamedBufferSubData`, `BufferMapDsa` and named/bound retry state;
`StateCache.BufferCreation/BufferStorage/BufferContents/PixelTransferBindings`; native buffer-binding and
buffer-deletion execution methods on StateCache; the buffer-storage command partials and Core/Arb
BindBuffer selector partials. Callers now use ready-owner factories and semantic uploader/binding/map
operations directly. Buffer scopes retain only existing selector recovery/restoration responsibility;
those helpers invoke final backend execution rather than an old cache executor.

Exact storage, Core/Arb generic binding and Core/Arb deletion overloads reside in backend `Buffers/External`
modules. Their compiler-checked engine adapters preserve the original legacy OpenTK signatures, exact
argument/error behavior and one native call. Manager-owned buffer readers delegate unknown external
reads to core named backend query services; controlled allocation/upload does not invoke them.
The thread-local cache associates its knowledge with admitted physical/registered context identity and
withdraws that knowledge on a switch. Context-local buffer owners retain physical identity, reject
foreign-context use/disposal and require disposal before local context destruction.

Initial client bytes remain pinned through synchronous creation. Mutable/dynamic partial initialization
uses subdata; immutable storage without dynamic updates uses an operation-local staging copy. Deletion
after issuing the copy relies on native deferred destruction, so staging storage remains until consumed.
The staging candidate uses original-context retirement and never an unrelated context's numeric namespace.

Still incomplete: image/buffer-texture associations and image transfers are B; sparse residency and
external-memory workflows are C/D; uniform publication, prepared program binding and geometry draw
orchestration are E. Their buffer storage calls already consume final backend operations. Their retained
orchestration/execution does not sit behind a buffer compatibility shim. Sparse residency is not a
canonical buffer-metadata mutation; the existing unused `GpuSparseBuffer` helper belongs to C/D's explicit
residency contract and must be removed there together with the final optional operation.

## B — Images, samplers and backing relationships (Phase 3)

- **Existing entry points:** `GpuTexture` allocation/replacement/mips/parameters; `DynamicTexture2D/3D/Cube`,
  `Texture2D/3D`, depth/stencil subclasses, `PmjJitterTexture`, `CloudBlueNoiseTexture`; `GpuBufferView`,
  `GpuBufferTexture`, `GpuTextureView`, `GpuRenderbuffer`, `GpuSampler`, `BorrowedTexture` acquisition;
  StateCache texture/renderbuffer/sampler creation/storage/contents/views/parameters/generation operations.
- **Callers:** `ScreenResourceManager`, `GBufferManager`, direct-lighting buffers, material atlas/builder,
  postprocessing/LUT and atmosphere resources, LumOn physical atlases/world probes, cloud volume storage,
  `CloudNativeMapAdapter`, `CloudSimpleRenderer` depth borrower and `DepthHierarchyRenderer/Pass` resources.
  Framebuffer image constructors/projections consume the new creation APIs, preserving ownership.
- **Final routes:** typed image/sampler factories; immediate owner storage/configuration/mip generation;
  semantic upload/copy/clear and buffer-image transfer inputs. Buffer texture/view factories require backing
  refs and explicit ranges. Retain mutable definitions where requested, with backend-owned bound restoration.
- **Manager/readers/retirement:** `Gpu*Initialization` for texture/renderbuffer/buffer-texture/view/sampler;
  `GpuTextureMetadata`, `GpuRenderbufferMetadata`, `GpuSamplerMetadata` and image projection;
  `GpuTextureMetadataReader`, `GpuTextureFormatReader` (the unused single-image reader is removed),
  `GpuRenderbufferMetadataReader`, `GpuRenderbufferTextureMetadataReader`; manager image acquisition,
  selected-image projection, texture generation/backing identity/view-source and dependency partials.
  Move T1–T4/R1/image I1 exact adapters and texture/renderbuffer/sampler L1 together.
- **Dependencies/removals:** consume A, retain manager-owned original backing associations; remove baseline
  creation/readback capability forks and old executors across wrappers, readers and fixtures. FBO realization
  remains E, but its existing helpers must use final B allocation/transfer routes directly. External
  memory-backed storage and import are distinct operations: A/B exact storage adapters preserve supplied
  names without invoking import; D owns creating/importing memory and later uses final A/B storage.
  Existing memory/semaphore wrappers contain no production caller back-edge into A/B creation.
  Add the missing `GpuBufferView` wrapper-owned `GpuTextureRef` conversion using the unchanged typed-base
  value contract; do not claim this conversion already exists or create a second texture input abstraction.
- **Verification:** `GpuBufferImageCreationTests`, `GpuTextureFailureRecoveryTests`,
  `CloudNativeAdapterTests`, `CloudStoredVolumeViewTests`, framebuffer attachment discovery and texture/view
  lifetime tests. Check cube faces/mips/format derivation, narrow sample fallback, view-relative selection,
  same-ID reuse, backing resize/delete, unavailable snapshots, candidate cleanup and named/bound selectors.
  Retain producer readiness certificates; metadata usability never substitutes for fresh content.

## C/D — Synchronization, results and auxiliary resources (Phase 4)

- **Existing entry points:** `GpuFence.Insert/Poll/Wait/TryConsumeIfSignaled`, `GpuQuery.Create` and native
  query consumers, pixel-pack read/map and unpack staging, raw `GL.MemoryBarrier`, fence/query/wait calls;
  `GpuSemaphore.Create/Wait/Signal/ImportFromFd`, `GpuMemoryObject.Create/SetParameter/ImportFromFd`;
  `GpuTransformFeedback.Create/BindBufferBase/BindBufferRange/Begin/End/Pause/Resume`;
  optional `GpuSparseBuffer`/`GpuSparseTexture` page commitment and typed residency execution.
- **Callers:** uniform ring/persistent storage completion, scene/world-probe feedback and diagnostics,
  material-builder statistics, exposure diagnostics, cloud stored-volume diagnostics/work/publication,
  depth hierarchy transfers, `GlGpuProfiler` and context cleanup. Preserve DEBUG-only diagnostics.
- **Final routes:** barrier access descriptions, explicit fences/GPU waits/CPU poll/wait, precreated query
  operations, owned-staging readback tickets, scoped ready mapping; typed external import/ownership and
  resource-transition descriptions; feedback output ranges/capture operations. No implicit wait per command.
- **Manager/readers/retirement:** query/sync/feedback/semaphore/memory initialization and empty canonical
  schemas, their creation partials and kind-specific deletion outcomes. Native-width fences remain intact.
  Pending/Ready/Failed/Cancelled tickets are request state, not new canonical metadata or lifetime registry.
- **Dependencies/removals:** A/B transfers are prerequisites. The current feedback wrapper executes
  configuration/capture operations, not draws; move that complete execution surface into D. E's later
  draws consume these final operations, not an old feedback executor. Query/conditional-render control
  similarly preserves current native ordering without moving a draw executor into D. Remove wrapper/native
  query execution and old sync/auxiliary cache executors with this unit; retain final owners and completion
  rules. A capture scope spans its matching begin/end or unwind; per-method binding restoration must not
  disturb active capture. Unexpected caller dependencies reopen the closure before implementation.
- **Verification:** `GpuRemainingCreationTests`, `GpuSemaphoreMemoryObjectIntegrationTests`, result/fence/
  query/ring/feedback tests and exact deletion tests. Check optional rejection, pending/cancel/reset ownership,
  actual completion before staging reuse, original-context retire, import handle ownership, no duplicate
  deletion and explicit hardware gaps. A skipped native extension case is not successful qualification.

## E — Shader/PSO, geometry, passes, uniforms and immediate consumers (Phase 5)

This is an inseparable integration unit. Work order is executable → PSO → geometry/targets/uniforms →
complete context/all consumers/exact forwarding. No partial acceptance through an old shader or draw API.

| Work package | Existing entry points and affected callers | Final route and same-unit removal |
| --- | --- | --- |
| Executable | `GpuShaderModule.TryCompileGlsl/TryCompileGlslPreprocessed/TryLoadSpirv`, `GpuProgramObject.AttachShader/DetachShader/TryLink`, `GpuProgramPipeline` stage/configuration/validation, `GpuComputePipeline`, `ShaderCompilation/ShaderProgramLink`, `PendingShaderProgram`, `ShaderLinkBatch`, `Spirv/SpirvStageLoader`, binary/interface loading and `Shaders/GpuProgram.*`, `GpuComputeProgram*`. All concrete shader subclasses and asset owners in PBR/LumOn/Rendering participate. | Final shader descriptions and sync/pending factories, backend-private native owners and link completion. Asset/preprocessing/permutation/cache policy stays above. Remove public native program/stage execution inputs and old managed linking/activation routes; do not remove needed native wrappers or external exact operations. |
| PSO/reload | `GraphicsPipeline`, `GraphicsPipelineDesc`, `ShaderPipelineIdentity`, `GlPipelineDesc`, graphics/compute scopes and StateCache application. Renderer pipeline sets in direct lighting/composite, postprocessing, atmosphere/celestial, cloud view, liquids, debug, material builder and LumOn world-probe upload. | Immutable graphics/compute realization/ref, exact executable interface and atomic reload sets. Translate existing state values to project enum contracts directly, preserve state suppression. Remove old pipeline submission/activation APIs; reuse specialized validation/value logic. No global cache or second executable reflection. |
| Geometry | `GraphicsGeometry.Validate/Submit`, `ManagedGraphicsGeometry`, `ArrayGraphicsGeometry`, `EngineFullscreenGeometry`, `EngineLiquidPoolGeometry`, `CloudSimpleGeometry`, `GpuVertexAttribBinding`, `GpuVao` creation/configuration and `StateCache.Vertex*` / engine geometry. | Retained stream/index/indirect descriptors, manager validation and backend configuration/draw. Remove adapter-owned draws and try-named fallbacks. Keep authored layout fields, original engine ownership/borrow interval and manager-owned selected vertex queries; no full native-layout metadata mirror or index-content readback. |
| Targets/passes | `RenderPassDesc`, `RenderPassTargets`, `RenderPassOperations`, `RenderPassSequence`, dynamic/static framebuffer configuration, attachment ownership/discovery and routing; `RenderPassColor/DepthStencil`, `ScreenResourceManager`, GBuffer and renderer targets. | Attachment/surface descriptions, context-scoped realization cache and existing manager/pass guards. Remove renderer requirement to manufacture FBOs for portable passes. Raw integration FBO wraps remain explicit borrowed paths. Move F1 attachments/defaults/routing and framebuffer L1; preserve completeness before pass effects, no ID-only key. |
| Bindings/uniforms | `ShaderPreparedSubmission`, `ShaderInputValidation`, `GpuPreparedBindings`, `GpuProgramInterface/Layout`, `CpuUniformBuffer`, `ShaderUniformPublication`, generated desired state, `ShaderContractGenerator/RuntimeSubmissionEmitter`, `UniformPublication`, ring/pages/persistent storage and execution history. | Interface-qualified frozen tables/logical snapshots and current-epoch execution publication. Preparation remains supplied-metadata-only; manager owns queries. Rewrite generator and all generated consumers directly, not generator-to-old-Submit facade. Remove immediate physical allocation from recording, commit captured successful revisions only. |
| Unified context/boundary | `GraphicsCommandContext.TryRun/BeginPass/SetPipeline/Draw/EndPass`, `ExecuteExternal`, `CompleteGraphicsBoundary`, graphics/compute usage scopes and StateCache pass/pipeline/resource-slot execution. All graphics and compute renderers call the final context. | Final immediate semantic operations, full boundary declaration/admission, existing selective capture/restoration. Delete `GraphicsCommandContext` in this unit, no alias/facade. Retain specialized helpers behind the unified context, no duplicate boundary/pass state. |

Manager domains include program acquisition/transfer/fields/deferred link, framebuffer backing/discovery/
attachment modes/dependencies, vertex fact acquisition, metadata operations, original entry/context and
retirement. Native services include all program/framebuffer/vertex readers. P1/F1/V1 and remaining state/
binding selectors and L1 exact overloads move with these packages, retaining original native semantics once.
Sampler/image unit setters preserve metadata-versus-content classification and existing bindless withdrawal.
Bindings use baseline multi-bind where semantically eligible; reconcile separately scheduled work, no
older-context fallback or duplicate slot knowledge.

Representative closure receipts:

- Direct lighting/composite: create resources and shader/PSO → logical inputs/geometry → admitted pass →
  validated binding/draw → resize/reload compatible replacement → original-context disposal.
- Depth hierarchy: create R32F chain/SSBO → dispatch → access barriers → tail buffer/image transfers →
  producer publication. Remove raw pack/unpack/native dispatch without changing the algorithm or order.
- Clouds: retain native map/depth borrowers and source certificates → callback-admitted geometry/view pass →
  later volume update/publication. Retaining a list cannot extend native callback validity or certify content.
- Uniforms/reload: capture revision → prepare storage in execution epoch → bind → commit captured version;
  newer CPU edits stay dirty; cancel/failure doesn't commit. Reload preserves old installed shader/PSO set
  on candidate failure and rejects old tables against new generations.

Verification includes existing pipeline description/state/boundary/feedback/geometry/VAO/engine tests,
framebuffer routing/attachment/completeness tests, shader creation/binary/deferred link/prepared binding/
generated state tests, uniform epoch/publication tests and cloud/depth consumer suites. Add precise
fixed-generation/reload failure, target-cache identity/reuse, no native geometry escape and combined graphics/
compute tests. Headless outcomes establish contracts, not game appearance or performance.

## F — Recording (Phase 6)

New header/pages/reference-table/list lifecycle implements the contract over final E operations, with
no alternate recorder execution authority. Add named recordable operations from the contracts table.
Argument capture freezes bytes/arrays/descriptions; no metadata/cache/manager/native lookup or physical
uniform allocation. Submission retains exact shader/PSO/table identity and owner-following texture rules.
Migrate representative legal consumers only; immediate remains default and engine callback borrows remain
bounded. Test no-current-context recording, mutation after capture, lifecycle misuse, failure indices,
context retire, cancellation and independent ticket/staging ownership. No old API exists to bridge here.

## G — Final cache split, wrapper boundary correction and qualification (Phases 7–9)

Audit A–F removals first. Native execution remaining in a completed family reopens that unit. Then rename
the residual cache type/files/callers directly to `OpenGlStateCache`; no old alias. Reconcile architecture
documents to implemented state, not this planning history. Complete Phase 8's wrapper/helper corrections
and combined dependency qualification before Phase 9 acceptance. Review cache policy, creation/acquisition
orchestration, publication, capabilities and diagnostics as well as native dispatch location; the later
wrapper audit supersedes earlier blanket source-closure assessments. Run delegated integrated and canonical package
checks on the candidate, focused allocation/query/suppression checks and separately user-run live acceptance.
No performance baseline, benchmark, timing threshold or instrumented Release build is required.

## Contract traceability and baseline evidence

| Phase 1 task group | Controlling source / evidence |
| --- | --- |
| Inventory, units and exclusions | Proposal “Engine integration and migration boundaries”; API coverage “Explicit API exclusions” and B1–L1; inventory wrapper/schema/consumer sections; source sites and A–G above. |
| Context/backend/creation/shader/PSO/list contracts | Proposal component/operation tables, creation, executable and recording sections → Contracts ownership, resource table, signatures and lifecycle. |
| Project descriptors/refs | Proposal public inputs, geometry, attachment passes and payload ownership; archived creation/texture-reference contracts → Contracts descriptor/ref tables and no-authority-leak rules. |
| Uniforms/boundaries/source ownership | Proposal migration and admission sections; current resource and prepared submission contracts → Contracts execution/boundary/storage rules; E's combined caller/removal closure. |
| Order and verification | Proposal no-shim rule and representative outcomes → A–G dependency rules and per-unit tests; delegated baseline below. |

Fresh delegated baseline, 2026-10-25, SDK 10.0.401, `VINTAGE_STORY=G:/Vintagestory` and process-local
`NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`:

| Command / receipt | Observed result |
| --- | --- |
| `./build.ps1 --configuration=Release --spirv=true`; `artifacts/rhi-phase1-baseline/canonical-package-retry.log` | Canonical JSON validation, clean/publish, 761 production shader variants and ZIP packaging passed. The first overlapping invocation collided with the active Debug shader producer's output lock; the sequential retry passed. |
| `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj --configuration Debug --no-restore --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/rhi-phase1-baseline` | Debug production/test builds and 761 production/842 fixture shader variants passed. Broad test execution was deliberately interrupted after numerous unrelated catalog/lighting/world-probe failures; partial output records 97 failures and 43 skips. No complete result, passed count or Debug TRX is claimed. `debug.log` and `debug-partial.json` retain evidence. |
| Release focused resource/cache/interception/borrowed-view selection; exact full command in `commands-and-results.txt`, `release-focused.trx` | 469 passed, 4 failed, zero skipped, 473 total. Failures: both `FramebufferRoutingDsaTests.WindowRestorationAlwaysPreservesGroupedSelector` cases; `NamedAndBoundRestorationProduceEquivalentResults(standalone:false)`; `EngineFramebufferRoutingTests.PatchedNativeRoutingSharesTheOwnedMapping(array:false)`. |
| Same Release binary, `--no-build --no-restore --filter 'FullyQualifiedName~FramebufferRoutingDsaTests\|FullyQualifiedName~EngineFramebufferRoutingTests'`, process-only `COMPlus_JitNoInline=1`, `COMPlus_TieredCompilation=0`; `release-routing-observer.trx` | All 7 affected-class cases passed, zero skips. This supports observer/JIT sensitivity; it does not erase the ordinary Release failures or qualify the whole suite. No instrumented Release product was created. |
| `artifacts/rhi-phase1-baseline/package-identity.json` | Publish, package-directory, ZIP and tested runtime DLL SHA256 all `DEB104E0E7BCF8166CAC78C9C53C338D1AA797002C18203B74B5BA04A8948B88`; 761 packaged shaders match the canonical producer, zero mismatches, 761 in ZIP. |

Exact commands, environment, scheduling collision and nonfatal NU1900/ordinary compiler warnings are
retained in `artifacts/rhi-phase1-baseline/commands-and-results.txt`. A baseline failure records the
existing condition; Phase 1 introduces no runtime migration and does not authorize unrelated behavior
changes merely to make that baseline green. No repository-wide green claim is made. Installed-game
startup, appearance and GPU/frame-time performance remain unqualified; no game was launched or installed.

## Buffer migration qualification (Phase 2)

Second review and the independent stage audit reconciled the selected task contract with the proposal,
Contracts, unit A, the resource/binding contracts, canonical schema/API coverage and archived private
initialization authority. No remaining required buffer-unit defect or unresolved documentation gap was
found. Other units above remain incomplete; this is not acceptance of their retained execution APIs.

| Task group -> controlling contract | Implementation and evidence |
| --- | --- |
| Admission/context -> proposal baseline; Contracts authority/submission; A | OpenGLBackend, RHICapabilities, existing GpuSupport capture, StateCache.ContextIdentity and GpuResource.ContextIdentity. RHIBufferTests covers original thread/context, physical local tools, stale refs and selector knowledge. |
| Creation/operations -> proposal owned creation and reconciliation; archived initialization authority | Typed ready-owner factories, private manager handoff, named backend operations and explicit MetadataOperation participation. Initial-content, failure diagnostics, candidate cleanup, omission/zero, frozen requests and interruption tests; no success-on-dispose or retry. |
| Callers/removals -> proposal family-closure rule; A | Existing owners/uploader/growth and uniform/geometry callers use final buffer operations. Before inventory is the retained Phase 1 site inventory; after inventories are artifacts/rhi-phase2/buffer-callers.txt and buffer-native-sites.txt. removed-buffer-apis.txt is empty. Images, optional residency/results and render submission remain B/C/D/E. |
| Exact calls/readers -> API coverage B1/V1/L1 and explicit exclusions | Buffers/External, compiler-checked NativeAdapters and Core/Arb deletion mappings; manager-owned readers delegate to backend query services. EngineStateSwitching/EngineGeneric, buffer interception, reserved-name and deletion tests cover native signatures, errors and publication. |
| Behavioral qualification -> selected task tests and retained resource/binding contracts | Debug scoped 673 passed; subsequent final affected selections passed 53 and 84 respectively. Final Release scope is 675 tests, with the observer qualification below. ShaderContractGenerator.Tests: 180 passed. No game launch, visual qualification or performance measurement. |

Delegated exact commands, class selections, environment and results are retained in
`artifacts/rhi-phase2/commands-and-results.txt`. `debug-final-scoped.trx` records the broad Debug scope;
`debug-final-changes.trx` and `debug-interruption-recheck.trx` qualify the subsequent frozen-request and
interruption corrections. `generator-debug.trx` records generator validation.

Ordinary Release `release-final-scoped.trx`: **671 passed, 4 failed, zero skipped**. The failures concern
Harmony observation/injection visibility: the pre-existing engine routing case and three RHI creation/
allocation callback tests. The same ordinary Release assemblies pass all **13 affected cases** with
process-local `COMPlus_JitNoInline=1` and `COMPlus_TieredCompilation=0`
(`release-observer-controlled.trx`). No instrumented Release product was built; the ordinary run is not
reported as green. An additional irradiance rendering assertion was reproduced on the untouched prior
Release artifact (Phase 1 runtime SHA DEB104E0E7BCF8166CAC78C9C53C338D1AA797002C18203B74B5BA04A8948B88)
and excluded from this direct-family scope; its exact prior comparison is recorded in the command ledger.

Final `./build.ps1 --configuration=Release --spirv=true` passed. Publish, package-directory, ZIP and
tested runtime DLL SHA256 all **CB4518C0ADD30CBCBAFF39A02223353BDF9098A4111C38E6427F677816FAD7CE**.
All **761** packaged/ZIP shaders match the canonical producer, with zero mismatches.
`artifacts/rhi-phase2/package-identity.json` records identities;
`artifacts/rhi-phase2-canonical-package-final.log` records canonical packaging. These receipts qualify
this family and candidate, not a repository-wide clean suite or installed-game acceptance.

## Image migration qualification (Phase 3)

The before inventory is unit B and the retained Phase 1 production-site inventory. The final routes are:

| Removed entry points / execution owners | Final route and retained responsibility |
| --- | --- |
| DynamicTexture2D/3D/Cube, Texture2D/3D, GpuRenderbuffer and GpuSampler Create/CreateWithData factories | RHICommandContext typed factories, semantic descriptions/initial pixels, existing unpublished owners and private manager handoff. Existing growth, streaming, filtering and format policy remains above execution. |
| StateCache texture allocation/creation/storage/multisample/view/sampler/renderbuffer executors; wrapper native subimage/copy/clear/mip setters | Focused OpenGL texture/renderbuffer/sampler backend operations. Core named commands, required bound mutable definitions, one explicit publication, original-context scope restoration. Owner sampling/storage routes use the semantic context contract. |
| Buffer-view whole/range factories and managed native-view source factories | CreateBufferTexture/CreateTextureView with backing references, explicit ranges and unchanged GpuTextureRef. GpuBufferView supplies its wrapper-owned conversion. Raw names use explicit backend integration; renderbuffer borrowing uses OpenGLRenderbufferIntegration.Borrow. |
| Image native-reader capability forks and allocation-limit queries | Manager-owned reader facades delegate to named backend query services. Required cube-face queries remain bound. Existing cached limits/format support and narrow unsized-format/sample output reads retain their authority. |
| TextureStreamingManager native subimage dispatch and depth-tail/LumOn transfer calls | Backend streaming consumption and semantic upload/copy/buffer-image transfers. Backend owns pixel-buffer/layout restoration. Queued uploads retain destination storage provenance and never retry failed native work. |
| Rendering/Commands texture/buffer-texture/renderbuffer exact executors and image/sampler deletion overloads | Separate backend External modules retain exact GL/Core, Ext and Arb signatures/errors/native effects. Existing StateCache observation, dependency and deletion-outcome services remain singular. |

After inventories: `artifacts/rhi-phase3/image-callers.txt`, `image-native-sites.txt` and the empty
`removed-image-apis.txt`. Required bound mutable/cube-face semantics remain; forced old-DSA variants
and obsolete fallback helpers were removed. No temporary executor, alias or intermediate shim remains.

Residual execution is explicitly assigned to incomplete units: C/D owns existing CPU readback/fence and
optional residency/import APIs; E owns framebuffer/pass clears/routing, shader/image-slot and multi-bind
publication, draw/dispatch and complete GraphicsCommandContext replacement. Existing framebuffer
allocation helpers already consume the final image factories. The small existing feedback/pass/static
lock helpers follow complete managed view chains because image-view creation depends on that safety;
this does not claim completion of E's pass realization or command context.

| Task group -> controlling contract | Implementation / verification |
| --- | --- |
| Creation/storage/policy -> proposal creation/publication; Contracts factories/descriptors; archived initialization defaults; B | Typed ready-owner results, automatic omission flags, private manager transaction, mutable/immutable image geometry, sampler intent, partial creation/configuration cleanup and narrow sample/format reads. RHIImageTests and existing controlled creation/failure/cube tests. |
| Views/backing -> unchanged archived texture-reference contract; Inventory canonical schemas/lifetimes; B | Original manager dependencies, local certificates, explicit whole/range mode, view-relative coordinates, managed nested-view shared-storage feedback and recursive existing lifetime locks. Texture/reference/view/interception, cloud and stored-volume tests. |
| Borrow/read/retire/exact adapters -> resource/binding contracts; API coverage T1-T4/R1/image I1/L1 | Covered aliases reuse facts; native readers stay behind manager acquisition, exact adapters execute once without managed suppression or retry. Original identity and context retirement remain authoritative. Native metadata/interception/deletion/context-local tests. |
| Transfers/callers -> Contracts content and transfer values; proposal whole-family closure; B | Checked offsets/pitches, backend pixel scopes, array layers/cube faces, staged destination identity, content-only revision preservation. RHIImageTests, streaming/depth/cloud/binding and rendering regressions. |
| Completion -> mandatory family receipts and retained qualification rules | Second review and independent stage audit compare all task-linked authority with source closure, behavioral receipts, generator/package identity and disclosed pre-existing limitations. Final qualification results follow below. |

Delegated commands, class selections and intermediate/final receipts are in
`artifacts/rhi-phase3/commands-and-results.txt`. Final startup-corrected Debug: **929 passed, zero failed/skipped**;
the earlier affected image/view selection passed **38** cases, including equal-sample multisample copies.
ShaderContractGenerator.Tests: **180 passed**.
The broad required selection excludes the explicitly out-of-scope benchmark class and the independently
verified stale shader fingerprint class; neither exclusion hides an image-family failure.

The wider exploratory Debug run detected an existing shader golden mismatch. A fresh-process rerun
reproduced it, and current runtime binding rows/fingerprints exactly match the previous canonical
package runtime (SHA256 CB4518C0ADD30CBCBAFF39A02223353BDF9098A4111C38E6427F677816FAD7CE).
The expected golden was not changed. See `bindings-current.txt`, `bindings-prior.txt` and
`debug-isolated-shader-migration.trx`. The retained raster benchmark also conflicts with the pre-existing
conservative same-image feedback rejection before it reaches timing; no benchmark/timing acceptance
is claimed. New test-owned pixel state is restored, and the resulting water/adapter correction group
passed all 40 cases. Build warning-code counts match the preceding candidate (`warnings.txt`).

The startup audit also verified OpenTK's independent legacy/core binding tables without invoking any
uninitialized native pointer (`binding-startup-probe.log`). Exact image and buffer deletions now obtain
per-name outcomes from the same legacy loader as their preserved native call and supply that fact to
existing cache reconciliation. Managed deletion retains its admitted core path. EngineEarlyDeletionTests
forbids core Is* probes in all four exact deletion kinds; the entire startup-corrected Debug selection
passes **929/929**. This closes the early-engine boundary without forcing RHI admission or capability
queries from an intercepted deletion.

Final ordinary Release (`release-startup-final.trx`): **917 passed, 12 failed, zero skipped**.
The failures are Harmony native observation/injection cases. The exact same Release binaries pass the
entire **929-test** selection with process-only `COMPlus_JitNoInline=1` and
`COMPlus_TieredCompilation=0` (`release-startup-controlled.trx`). This is a test-process qualification,
not an instrumented Release product; the ordinary Release suite is not reported as green.

The final canonical `./build.ps1 --configuration=Release --spirv=true` passed
(`canonical-package-startup-final.log`). Publish, package-directory, tested and ZIP runtime DLL SHA256
all **A33807202AC211770EF56C932F26F2643E2620BCDE5CC25A77C469958F7A74D4**.
All **761** packaged and ZIP shaders match the canonical producer, with zero mismatches. Exact identities
are in `package-identity.json` and `package-startup-verification.log`. These startup-corrected identities
supersede the earlier candidate hashes in the command ledger.

The final independent stage audit, following the implementation's second review, found every required
image/sampler migration item satisfied and no unresolved source or controlling-document evidence gap.
The disclosed observer limitation and unrelated golden/benchmark cases do not establish image-family
defects. Other migration units remain incomplete. No installed-game, visual or performance qualification
is claimed.

## Auxiliary migration qualification (Phase 4)

Implementation, second review and independent stage audit are complete. Qualification limitations below
remain explicit; neither broad Release invocation is reported as green.

| Removed execution entry points | Final route |
| --- | --- |
| GpuFence.Insert/Poll/Wait, raw memory barriers and explicit flush/idle waits | RHICommandContext.InsertFence/Poll/WaitCpu/WaitGpu/Barrier/Flush/WaitIdle and focused synchronization backend. The retained internal TryConsumeIfSignaled helper owns successful lifecycle consumption, not native dispatch. |
| GpuQuery.Create and wrapper-owned native query calls | CreateQuery with declared kind; BeginQuery/EndQuery/WriteTimestamp and generation-qualified RHIQueryTicket. GpuTimestampQuery/GpuTimerQuery retain diagnostic ownership policy. |
| Wrapper/FBO-based CPU texture reads and GpuPixelPackBuffer.ReadPixels | EnqueueReadback, private staging/fence and exclusive ready mapping. Existing queue-owned fenced output and multi-region staging retain their storage policy through final transfer/map APIs. |
| GpuTransformFeedback.Create/bind/configure/capture methods and StateCache capture selectors | Named creation/configuration, consumer ref and one complete capture interval through the backend. Exact engine BindTransformFeedback preserves unsuppressed original execution. |
| GpuSemaphore/GpuMemoryObject raw creation/import/configuration/wait/signal | Typed RHI descriptions, explicit platform-handle ownership and transitions; focused optional backend using the pinned GL.Ext extension binding. |
| Raw sparse buffer/texture helpers and swallowed Try* failures | Explicit typed residency operations, cached sparse-buffer page size and unsupported-before-dispatch checks; no retry path. |
| StateCache auxiliary creation/deletion and corresponding engine deletion overloads | Private manager creation handoff and admitted backend deletion; exact GL/Core, Arb and Ext overload modules retain original-loader outcomes. |

Task-to-authority mapping:

| Task | Controlling documentation | Implementation / evidence |
| --- | --- | --- |
| Barriers/fences/waits | Proposal public operations, execution/results and retirement; Contracts synchronization signatures; GpuResources lifetime; Inventory guards | Synchronization backend, direct production caller migration, fence/completion/ring/queue tests. |
| Queries/readback ownership | Proposal query/results and recorded-completion boundary; Contracts resource/factory and result signatures; archived creation handoff/defaults | Query/result and owned-staging modules; pending/failure/cancel, generation, QBO restoration, region/pitch and source-retirement tests. |
| Optional external objects/residency | Contracts auxiliary descriptions, typed imports and optional rejection; Inventory empty schemas; API coverage I1/L1 | External-object modules and semantic residency; supported creation/rejection/native sparse tests. Actual cross-API producer imports require separate hardware/producer evidence. |
| Transform-feedback closure | This document C/D capture interval; Contracts auxiliary creation and capture; Inventory unchanged empty schema/guards | Named creation, frozen outputs and full capture lifetime; native pause/resume/output/sentinel/guard and unwind tests. No draw executor is moved into C/D. |
| Adapters/readers/deletion | Proposal exact-boundary and migration rules; API coverage allowed owners and L1; archived resource creation; unchanged archived texture ref | Exact auxiliary deletion and capture selection backend; kind-specific outcomes, original context, lifecycle/name reuse and no old executor source checks. No unrepresented descriptor reader is invented. |
| Completion receipts | Plan mandatory migration contract; Proposal efficient execution and qualification; Contracts source ownership | Focused Debug/Release, generator regression, canonical package/identity, source closure, second review and independent completion audit. E/F remain explicitly incomplete. |

Source closure is recorded in `artifacts/rhi-phase4/auxiliary-callers.txt` and
`auxiliary-native-dispatch.txt`; `removed-auxiliary-apis.txt` and `auxiliary-native-bypasses.txt`
are empty. The original inventory above supplies the before state. Native queries, fences, auxiliary
creation/deletion, capture, readback and residency execute only in their backend modules. Exact external
deletion keeps the original legacy loader; managed execution uses the admitted core binding, with the
pinned optional extension binding confined to focused backend modules. Query/conditional-render and
capture transitions reject native-callback reentry before altering unfinished execution state.

Remaining E work is shader/program/PSO, geometry/draw/dispatch, framebuffer/pass/routing and complete
GraphicsCommandContext replacement. Those callers now consume final C/D synchronization and transfers;
no old auxiliary executor is retained to keep them compiling. Recording and recorded completion markers
remain F. Shared manager metadata, guards, selectors and retirement remain singular.

Native sparse-buffer commitment, sparse-image commitment/content readback, pipeline statistics,
transform-feedback output/pause/resume, ready readbacks and fences execute on the test context.
Deterministic native interception covers pending/failed waits, unmap corruption and execution reentry.
Actual cross-API memory/semaphore imports and external-producer wait/signal exchange have no producer
fixture; they remain unexecuted native qualification cases, not successful coverage. Windows also
cannot establish actual FD-import execution. Supported object creation/configuration, typed ownership
validation and unsupported-feature rejection are tested separately. No game launch, installed-game,
visual or performance qualification is claimed.

Delegated commands, exact selections and intermediate/final results are retained in
`artifacts/rhi-phase4/commands-and-results.txt`, `final-classes.txt`, `final-filter.txt` and
`final-focused-filter.txt`. The expanded Debug selection passed **1,128** tests before the final
transition/admission corrections. The frozen candidate then passed **55** focused tests, including the
new query/capture reentry checks (`debug-frozen-focused.trx`). Generator regression passed **180** tests
(`generator-final-debug.trx`). These counts overlap; they are not added together.

Final ordinary Release: **1,106 passed, 23 failed, zero skipped** (`release-final-expanded.trx`).
The exact same binaries and 1,129-case selection with process-only `COMPlus_JitNoInline=1` and
`COMPlus_TieredCompilation=0` produced **1,124 passed, 5 failed, zero skipped**
(`release-final-controlled.trx`). The 18 resolved failures involve native interception/observation;
the remaining five occur in SceneColorParticleTests after resource-manager shutdown by other fixtures.
Fresh ordinary Release processes passed that class **12/12** and SceneColorParticleIntegrationTests
**3/3**. All **55** focused cases passed in the controlled broad run; see
`release-focused-coverage.json`. This is process qualification of ordinary Release binaries, not an
instrumented Release build or a claim that either broad run passed.

The earlier exploratory selection exposed five independent failures in PbrModeLifecycleTests,
SceneColorParticlePublicationTests and WaterRefractionLifecycleTests. Each reproduced in a fresh process
against untouched prior-product Release binaries, runtime SHA256
`A33807202AC211770EF56C932F26F2643E2620BCDE5CC25A77C469958F7A74D4` and test DLL SHA256
`EB38FA94DF1392C9BF19E40062EFFFEE5F8F3FDE1255943714525907FD2FBAFF`.
The final selection excludes those classes and retains the earlier independently established shader
golden/debug-binding exclusions; measurement classes remain outside the requested work. Exact failures,
baseline identities and standalone receipts are retained rather than changing their expectations.
ParticleIntegration also runs separately because its shared-fixture shutdown ordering is sensitive.

Canonical `./build.ps1 --configuration=Release --spirv=true` succeeded
(`canonical-package-final.log`). Publish, package directory, tested and ZIP runtime DLLs all have SHA256
`D7BF605C48A78FD8A22BFFADE63823ECB3ED34AB47A5C06419A492C5489B67AA`.
All **761** packaged and ZIP shader variants match the canonical producer, with zero mismatches.
`package-identity.json`, `package-verification.log`, `final-tested-identities.json` and
`final-source-sha256.txt` identify the qualified candidate.

The independent completion audit reconstructed the plan's inherited and task-linked authority after
the second code review, checked each accepted C/D unit against implementation and receipts, and found
no unresolved required implementation or source-closure issue. Unexecuted external-producer cases and
the disclosed broader test limitations remain qualification limits, not successful native evidence.
Shader/PSO/render submission and recorded lists remain outstanding under E/F.

## Rendering migration qualification (Phase 5)

Implementation is in progress. The entire E unit remains open; no shader-only or PSO-only acceptance
is claimed, and the Phase 5 checklist remains unchecked.

| Work package | Consulted authority | Current implementation / evidence |
| --- | --- | --- |
| Executable inputs, creation and lifetime | Proposal shader creation/ownership and backend publication; Contracts executable/factory/failure contracts; GpuResources initialization and retirement; archived creation handoff; Inventory program/shader schemas and guards | Frozen code, typed specialization and binding descriptions; fixed ready shader refs; pending candidate ownership; backend allocation, linking, canonical output queries and opaque binary export. New lifecycle tests use existing precompiled SPIR-V. Graphics and compute asset owners now create final RHI shaders; compute policy owns a compatible managed PSO without native re-registration. Cache policy selects/evicts/persists opaque binaries above backend execution. Obsolete pending native-program, stage-loader and wrapper compile/link execution APIs are removed. Full E integration remains outstanding. |
| Exact retirement and engine handoff | Proposal permanent engine boundary; API coverage P1/L1 allowed owners; Inventory original-entry retirement | Rendering-object deletion overloads use focused backend modules with original-loader external outcomes and admitted managed outcomes. Engine terrain replacement has an explicit integration-owned create/identity-check/install/retire handoff; terrain policy no longer reaches through the context to its backend. Broader caller closure remains under verification. |
| PSO, geometry, targets, bindings, context and renderers | Proposal graphics/compute realization, replacement, pass targets, uniforms and boundary admission; Contracts E signatures; this document's inseparable E work packages; prepared resource submission | Compute realization/ref and direct/indirect execution now use the final context/backend; existing graphics PSOs, geometry, target realization, uniform snapshots and full declared-pipeline boundary admission remain outstanding. Recording remains F. |

Baseline source commit: `487605beb84f7f9f0e78bef1f8f3c3a5da437e07`. Delegated baseline Debug build
passed; the selected 46-class regression passed **917/918**, with the existing
`GpuProgramCandidateMetadataTests.SubmissionDefersQueriesAndPreparationReusesSnapshot` assertion
failing at line 40. Logs, exact selection and DLL identities are retained in `artifacts/rhi-phase5`.
These are baseline receipts, not implementation qualification. Initial executable lifecycle tests
passed **2/2** after correcting their canonical fixture asset path; broader candidate checks are pending.

Intermediate checks: Debug shader/asset/declaration checks passed **29/29**; the subsequent
asset/execution/terrain selection passed **76/77**. Its sole test discarded known stage facts through
a raw local detach/re-adopt; that test now retains the typed native owner for its snapshot assertion.
The expanded shader-closure selection completed with **659/664** passing. Three failures observe old
loader entry points, one lacks stage-operation diagnostic context, and one uniform-publication failure
passes alone with the same binaries. Exact logs, TRX and runtime identities are retained under
`artifacts/rhi-phase5/debug-shader-closure*`. The corrected observer selection passes; replacement then
exposed a native error from mismatched SPIR-V stage ingestion. Entry-point admission now rejects that
input before native submission. A separate isolated regression proves that a deleted current program
could be captured and later restored after its native name was freed; managed retirement now unbinds
that selected owned executable first. Exact external deferred deletion is preserved. The focused
retirement/input/compute/publication selection passes **15/15** (`debug-retirement-fix-small.trx`);
the broader lifecycle and replacement selection passes **516/516** on the same candidate
(`debug-retirement-fix-lifecycle.trx`). Compute submission also guards PSO
retirement and recursive pipeline changes. Unused legacy metadata transfer/recovery methods are removed.
`retirement-fix-identities.json` identifies the tested runtime and test assemblies. Subsequent
`GpuProgramObject` visibility-region/comment organization does not alter executable behavior; a later
build must qualify the complete rendering migration's final source candidate.
None of these receipts qualifies the whole E unit.

Graphics state-description migration now replaces depth/stencil, blend and raster enum inputs directly,
including authored renderer/test descriptions and relevance/validation consumers. Explicit native value
translation remains in the backend; native cache observation/restoration schemas are unchanged. These
are value conversions rather than alternate execution APIs. Debug depth/stencil selection passes **66/66**
(`depth-stencil.trx`); blend/draw selection passes **133/133** (`blend.trx`). Expanded raster/boundary
selection passes **158/159** (`raster.trx`). Its sole failure expects an upload to leave unpack alignment
changed, contrary to the image restoration contract. That test now checks preserved native/cache state
and uploaded content; focused revalidation passes **46/46** (`raster-restoration.trx`).
The old vertex-layout/attribute/interpretation types are replaced directly by the public RHI contracts,
with copied read-only attributes, structural identity, semantic component formats and explicit native
storage translation. All existing consumers use the new types. The approved enum naming uses descriptive
domain names without an RHI/E prefix; namespaces and major abstractions retain RHI. Enum declarations
and callers are renamed directly, with no compatibility aliases. The resumed vertex/state/shader and
representative resource selection passes **285/285** (`enum-naming-attempt3.trx`); its build and assembly
identities are retained alongside the receipt. That supersedes the earlier interrupted vertex test run.
Primitive topology inputs and linked-stage facts now use the same semantic contract, with explicit backend
conversion. Target signatures, sized-format compatibility and typed clear validation use `TextureFormat`;
pass setup projects existing canonical attachment facts without querying native state. Renderer descriptions
use the attachment's semantic format accessor, keeping backend conversion out of authored target policy.
These input changes do not yet replace the existing graphics realization or pass execution APIs.

The topology/state run initially passed **244/248**; all four failures belong to terrain tessellation,
whose **13/13** tests passed alone on the same binaries. Boundary fixtures intentionally leave first-vertex
provocation, while terrain requires last-vertex provocation. The terrain fixture now establishes and restores
its required convention; the production admission guard is unchanged. The combined target/state/pass/terrain
and boundary selection passes **145/145** (`target-formats-attempt4.trx`). Expanded renderer/material/target
integration passes **173/189**, with sixteen cases stopping at one stale uniform-size assertion before drawing
(`target-integration.trx`). Both the source baseline and current composite block allocate 192 bytes and the
shader declares twelve vectors; the test's 160-byte expectation and constructor's ten-vector comment are
corrected to that existing contract. Pixel expectations and tolerances are unchanged. Final delegated Debug
build and the same expanded selection pass **189/189**, including all sixteen water-transmission pixel
checks (`target-integration-attempt2.trx`); build logs, selection and assembly identities share that prefix.
Attachment-based pass descriptions, full geometry execution, fixed graphics realizations, atomic graphics reload and
complete E caller closure remain outstanding.

The subsequent bulk graphics replacement is open and unqualified. `RHIGraphicsPipelineDescription`
now selects `RHIShaderRef`, freezes behavior without context/device discovery and compares exact generation
identity. The public immutable state values are reused directly. `CreateGraphicsPipeline` validates the
published interface and cached device limits, derives relevance once, and returns a managed
`RHIGraphicsPipeline` with a consumer-only reference and original-context/submission guards. It neither
registers a native PSO nor links/reflects the executable again. The previous graphics realization class
and descriptor are removed; renderer construction sites call the final factory directly. Asset-selection
identity no longer determines PSO reuse. No compatibility constructors or aliases are provided.

The binding portion of the same bulk candidate uses immutable `RHIResourceBindings` and
`RHIUniformSnapshot` inputs. Generated accessors retain desired resources; capture copies changed CPU
versions without slot publication or allocation. Execution validates the full interface-qualified set,
borrows original backing resources through native dispatch, and commits only the captured uniform
revision. Core texture/sampler multi-bind uses existing slot knowledge; absent textures retain
target-specific clears so unrelated targets are preserved. Image requests use `RHIImageBinding`,
`ImageAccess` and `TextureFormat`, with native translation confined to the backend. Controlled sampler
producers retain existing texture references; primary engine images are borrowed at framebuffer
publication. The CPU block's named/slot execution APIs, generated immediate-setter executor and raw-ID
sampler validator are removed. Injected engine uniform slots use the same backend uniform publisher.

Geometry replacement is included in this unqualified source batch. `RHIGeometryInputs` freezes stream
and index intervals; direct, indexed, noninstanced indexed-batch and indirect backend dispatch validate
known bounds without scanning index values or reading generated arguments. Controlled VAO creation and
named configuration use core 4.5 backend modules, existing manager registration and existing executed
selector knowledge. Private realizations retire through the originating disposal queue. Engine mesh
admission now routes configuration reads through the manager/backend and supplies certified packets;
the fullscreen, array, managed, cloud and liquid-pool producers expose data instead of native draws.
The old shader-owner activation/replay tracker, GraphicsCommandContext, GraphicsDraw and the former
renderer-lifetime placeholder are removed. Production rendering callers now enter explicit
RHIBoundaryDescription scopes on RHICommandContext, select fixed pipelines, publish captured bindings
and issue the final draw/dispatch operations. Existing cache snapshots restore executed program/slot
values without shader-owner activation or input replay. Compute-only declarations exclude unrelated
framebuffer, raster, clear and geometry state; undeclared pipelines and pass effects are rejected.

Attachment descriptions now cover postprocessing, direct lighting, water transport/refraction, cloud
view intermediates, LumOn and atlas baking. Their owners retain images and pure intentions instead of
portable framebuffer execution. The backend realizes named core framebuffers by original associations,
selected image/configuration fingerprints and routing; clear/load/store actions do not enter the key.
Repeated immutable descriptions reuse certified targets without native descriptor queries or new target
allocation. Failed candidates do not publish, replacement/deletion makes old configurations ineligible,
and context retirement queues private targets through existing original-lifetime cleanup. Selected image
facts in realization keys are checked against current canonical publications, never used as another
metadata authority. Explicit engine/window surfaces retain external ownership and specialized routing
restoration. The particle engine redirection remains an explicit native integration destination.

Named image blits use the same private target service, preserving source/destination conversion and
resolve rules as distinct from bitwise CopyTexture. They validate represented aspects, scalar classes,
samples, rectangles and shared-image overlap before target/native effects, borrow original resources
through dispatch, and preserve scissor/sRGB state. The celestial scratch-to-cube transfer retains its
format-converting blit semantics. Native framebuffer routing execution and original external-overload
program/vertex/framebuffer dispatch move behind backend modules; canonical metadata observation and
executed-state knowledge stay in the existing cache/manager. The previous GpuVao configuration/draw
facade, GpuVertexAttribBinding fallback helper and helper-only reader are removed; engine geometry
admission uses the manager-owned geometry reader instead.

Production source compiles in the canonical Debug configuration. Renderer-owned pipeline assets now
prepare complete candidate shader/pipeline sets and install them atomically; failed candidates preserve
the installed set. Standalone compute producers have explicit declared boundaries. Test callers remain
under migration from retired activation, synthetic identity, framebuffer ownership and wrapper draw APIs;
production compilation alone does not qualify this integration. The current bulk pass replaces those
callers directly and adds final geometry-range and framebuffer-feedback regressions. Combined tests,
second review and independent audit remain outstanding. The preceding 189-case receipt predates this
candidate and cannot qualify the replacement.


## Manager execution dependency correction (Phase 5b)

The manager and manager-owned readers no longer obtain `RHICommandContext.Current.OpenGL`, hold an
`OpenGLBackend`, or issue native queries directly. Existing native-valued canonical metadata and private
initialization records remain unchanged; public commands still accept only semantic descriptions.

| Previous route | Final route in this unit |
| --- | --- |
| Every manager family initializer → concrete backend lookup | Public context factory → backend owner declaration → existing manager transaction → explicitly supplied context → internal resource execution contract → backend allocation → manager association/snapshot handoff. No recursive public creation. |
| Manager deletion switch / immediate and local-owner native deletion | Existing original-context/association admission → context retirement operation → one backend kind dispatcher → existing family outcome/publication. Queues and registry remain unchanged. |
| Buffer/texture/shader/program readers → concrete backend lookup | Manager acquisition policy → typed internal context query → backend query implementation. Available canonical snapshots bypass acquisition as before. |
| Framebuffer reader owning native calls in the manager namespace | Manager reader in Metadata/Native → context → backend OpenGLFramebufferQueries; narrow affected-field recovery uses the same contract. No extra canonical store. |
| Program-field completion issuing GL calls | Supplied fields remain query-free; unknown external unit/block fields use narrowly selected backend query services before the existing manager commit. |
| Manager, private initialization and texture-view descriptor native capability lookup | Existing RHICapabilities projection; target/format cache misses use the internal context query contract. No second cache, discovery authority or variant strategy. Core 4.5 shader stages and attachmentless defaults need no older-feature branch. |
| Manager geometry acquisition reading backend origin/cache epoch | Context acquisition/certification and transient RHIGeometryContext; manager checks receipt reuse against original association/revision without layout readback. Consumer conversion remains in 5c. |

Traceability: 5b creation/acquisition/retirement tasks map to the proposal's The proposed boundary,
Resource creation and adoption, and Backend dispatch and publication, plus the contracts' Authority
and ownership and Resource references and factories. The private handoff retains the pinned
command-publication initialization contract. Geometry certification and retirement retain the existing
resource and binding identity, availability and restoration contracts. No new metadata authority,
service locator, public recipient, compatibility alias or old-executor shim is introduced.

Concrete dependencies still assigned to 5c–5e remain open: semantic objects/context composition,
ordinary wrapper operations and consumers, restricted engine integration, and combined qualification.

Final source candidate: uncommitted changes on `a8cdacb87e6ad7d7e7670bdd82633ebe668468d4`.
SHA-256 `2c4be0b477ae045c25751507dd857a0569ad0aa19f9e7d92c330e50d86115d81`
covers 3,380 existing tracked/unignored source and build-input files: sorted repo-relative paths ending
in cs, csproj, props, targets, glsl, frag, vert, comp, geom, tesc, tese or ps1. Hash input for each file
is UTF-8 path, NUL, raw file bytes, NUL; paths are unique and absent/deleted files are omitted.
Documentation is excluded so receipt/checklist updates do not change the source candidate.

Final delegated evidence under `artifacts/rhi-phase5b/`:

| Receipt | Result |
| --- | --- |
| `debug-build-complete.log` | Canonical Debug test-project compilation passed: 0 errors, 197 warnings. |
| `release-build-closure.log` | Canonical Release production compilation passed: 0 errors, 334 warnings. |
| `focused-tests-complete.log`, `phase5b-focused-complete.trx`, `focused-filter.txt` | 364 passed, 15 failed, 0 skipped, 379 total. Both manager-certification reuse/revision/retirement and explicit-default/invalid-extent initialization cases passed. |
| `units-capabilities-closure.log` and its TRX | 53 passed, 0 failed/skipped: registry, disposal, associations, initialization intent, format and capability cases. |
| `baseline/focused-tests.log`, `baseline/baseline-focused.trx` | Checkpoint baseline: 363 passed, 15 failed, 0 skipped, 378 total. Final candidate adds one certification fact; the exact 15 failing names are unchanged. |

The baseline used a `git archive` export of that checkpoint under the ignored baseline/source directory.
Its C# was compiled against checkpoint sources. Shader producers alone were disabled for this diagnostic
comparison, reusing the unchanged shader binaries from the canonical candidate build. This is an
inherited-failure comparison, not baseline canonical package qualification. Candidate Debug and Release
builds ran their canonical shader producers. An initial sandbox restore failure was resolved by an
approved network-capable retry; the failed attempt remains in `debug-build.log`.

The unchanged failing cases are two engine-graphics certification cases, one liquid index-association
case, five metadata-operation fixture mutation cases, one framebuffer affected-field observation case,
four desktop EXT program restoration observation cases, one exact-external/context-affinity case and
one external delete-pending-program case. These are explicitly assigned to 5c/5e. No assertion was
weakened to turn those failures green, and no broad green-suite claim is made. The obsolete pre-4.5
attachmentless capability fixture was replaced with cached-limit rejection plus explicit zero/false
initialization checks, preserving both pre-allocation failure and successful handoff coverage.

Second review and independent stage-completion audit confirmed the 5b boundary and its linked contracts.
No concrete backend retrieval remains in the manager, its readers, private initialization or the
format-capability cache. No public recipient API, parallel registry, temporary forwarding facade or
retry path was added. Combined Phase 5 qualification, live runtime and visual acceptance remain open.
No staging, commit, deployment, package publication or game launch was performed for this work.

Reproduction commands from the repository root (canonical default shader producers enabled; no
additional build properties or instrumentation):

```powershell
dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug -m:1 -v:minimal
dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Release -m:1 -v:minimal
$filter = Get-Content artifacts/rhi-phase5b/focused-filter.txt -Raw
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter $filter.Trim() --logger 'trx;LogFileName=phase5b-focused-complete.trx' --results-directory artifacts/rhi-phase5b
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~GpuResourceMetadataRegistryTests|FullyQualifiedName~GpuResourceDisposalQueueTests|FullyQualifiedName~GpuResourceAssociationStressTests|FullyQualifiedName~GpuInitializationIntentTests|FullyQualifiedName~TextureFormatHelperTests|FullyQualifiedName~InternalFormatCapabilitiesTests' --logger 'trx;LogFileName=phase5b-units-capabilities-closure.trx' --results-directory artifacts/rhi-phase5b
```

## Semantic objects and backend realization (Phase 5c)

| Previous dependency | Final route |
| --- | --- |
| Graphics/compute PSO stored concrete backend; graphics PSO stored native relevance/coverage | Fixed semantic description, shader identity and lifetime use IRHIContextLifetime. Opaque IRHIGraphicsPipelineRealization contains backend-owned OpenGL relevance/coverage, with no independent native allocation or registry entry. |
| Pending shader request stored native program/stage candidates | RHIPendingShaderCreation owns an IRHIShaderCreation operation. OpenGLPendingShaderCreation retains candidate owners and native completion/cleanup. Ready ownership still transfers once through the existing result. |
| Query/readback tickets depended on OpenGLBackend | Internal backend-neutral execution contract for captured-generation query observation, waits and immediate mapping. Original staging/fence ownership and cancellation remain intact. |
| Context cast its backend and exposed Current.OpenGL | Ordinary/owner operations use IRHIBackend, including mapping and private texture initialization. OpenGLRHIContext composes admission and exposes named cache/engine-integration operations without returning a backend. Native retirement notifications remain at that boundary. |
| Cloud/fullscreen/liquid adapters inspected native backend origin and cache epoch | Manager certification handles context/configuration freshness. Adapters retain engine ownership, authored layout, callback expiry and original-context retirement. |
| Local certification expired on unrelated VAO changes or staging retirement | Cache revisions apply to the selected array; certified buffer dependencies narrow deletion invalidation. Explicit acquisition also establishes its proven element-binding knowledge, so an equal managed bind does not invalidate its own receipt. No full VAO layout or canonical metadata mirror is introduced. |
| Engine liquid allocator left its element binding detached | Explicit engine draw-time index selection certifies the detached state; backend-owned VAO realization supplies the original index stream for RHI draws. A different nonzero association still rejects. Engine-owned arrays remain untouched. |
| Streaming queue obtained the concrete backend | Pinned immediate bytes use UploadTexturePointer and the ordinary semantic upload; PBO payloads retain a GpuBufferRef and use TransferBufferToTexture. Existing destination storage certificates, staging ownership and ring completion remain. Unknown raw engine destinations use explicit nonowning acquisition; owned destinations reuse supplied metadata. The old streaming executor is removed. |

Traceability: fixed realization and candidate/result ownership map to the proposal's Shader and pipeline
objects and Immediate execution and recorded lists, and contracts' Executable and pipeline contracts and
Submission and failure rules. Context composition and native integration map to Steady-state source
ownership and Engine integration and migration boundaries. Geometry retains the existing resource and
prepared-submission identity, bounds, borrow-expiry and restoration contracts. The unchanged private
initialization/retirement transaction retains the pinned command-publication and texture-reference
contracts. Neither a public native-object operation, parallel owner/registry nor temporary migration shim
is introduced. Native fixture operations now select their implementation explicitly instead of depending
on a semantic context escape; pending cleanup fixtures observe native allocations without exposing
provisional objects through the pending API.

Installed-engine inspection: VintagestoryLib 1.22.7.0, MVID
8a3ad3aa-2940-4c39-ae1b-881de9e65346. AllocateEmptyMesh detaches its EBO before releasing its VAO;
ordinary/grouped non-SSBO RenderMesh selects mesh.vboIdIndex for the draw and detaches afterward.
The exact IL receipt is artifacts/rhi-phase5c/baseline/engine-geometry-il.txt. RHI geometry does not call
those engine draw helpers or inherit their temporary association mutation.

Validation used HEAD 382905ccbfe476725c1569adc335e6bc22aa47a3 plus the working candidate.
The 3,392-file source/shader/tool-input fingerprint is
bec03a7ef30234fa4ae036ad414c9847d2397db2f0a28eb3f3d12140676effc2
(path, NUL, bytes, NUL in sorted repository-path order; docs excluded).
The machine-readable receipt is artifacts/rhi-phase5c/source-candidate.json.

| Receipt under artifacts/rhi-phase5c/candidate | Result |
| --- | --- |
| final-debug-build.log | Canonical Debug test-project build: 0 errors, 530 warnings. |
| final-release-build.log | Canonical Release production build: 0 errors, 334 warnings. |
| final-targeted-tests.log, final-targeted.trx, final-targeted-filter.txt | 121 cases: 119 passed, 2 inherited failures, 0 skipped. All four direct/PBO × R8/RGB8 padded-row content and unpack-restoration cases passed. |
| final-dependencies.trx | Registry, disposal, associations, initialization intent, formats and capabilities: 53 passed, none failed/skipped. |
| final-pending-retirement.trx | Pending candidate original-owner retirement: 2 passed, none failed/skipped. |

The two unchanged RHIProgramCaptureTests failures (CaptureIsPureAndGuardsAuthoredCallbacks and
FailedCaptureReleasesItsMutationGuard) reject the authored render_infrastructure fixture interface
schema/identity before entering their capture assertions. Both fail on the canonical checkpoint baseline
in artifacts/rhi-phase5c/baseline/focused.trx; fixture reconciliation remains explicitly assigned to 5e.
They do not qualify capture behavior. Dedicated lifecycle, completion, fixed-generation, wrong-thread,
context-retirement, geometry certification and pending-owner tests pass on this candidate. Inherited
geometry failures are resolved without weakening original association or configuration checks.

Canonical commands used default shader producers, without instrumentation or shader-build suppression:

```powershell
dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug -m:1 -v:minimal
dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Release -m:1 -v:minimal
$filter = (Get-Content artifacts/rhi-phase5c/candidate/final-targeted-filter.txt -Raw).Trim()
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter $filter --logger 'trx;LogFileName=final-targeted.trx' --results-directory artifacts/rhi-phase5c/candidate
```

Second source review and independent stage-completion audit reconciled the selected tasks with their
linked ownership, publication, reference and submission contracts. No required 5c finding remains.
The remaining native descriptor translation in streaming and other wrapper/consumer bypasses remain
with 5d; combined migration and fixture qualification remain with 5e. Phase 5 and command recording
remain open. No staging, commit, deployment, package publication or game launch was performed.

## Wrapper and consumer execution correction (Phase 5d)

| Previous route | Final responsibility |
| --- | --- |
| Texture/view/sampler slot bind methods and layout immediate executors | Captured RHI resource tables, or complete typed sampled-slot packets for engine programs. Superseded methods and their unused scope types are removed with callers. |
| Frame/light hook through native uniform integration entry | RHICommandContext.BindUniformSnapshot and the existing backend logical publication service; no second allocator, provisional range, or forwarding integration method. |
| Atmosphere and terrain raw texture IDs/native selector writes | Retained references and semantic dimensions. Terrain relief prepares its three slots; the atlas hook submits the combined set once, retaining nearest sampling on their shared normal/depth slot. Engine membership and sampler assignment remain native integration policy. |
| Wrapper native descriptor conversion and streaming native targets/pixel types | Private owner initialization records, project-owned formats/encoding/dimensions, and query-free published image-fact projection. Queued CPU/PBO payload ownership, destination storage certificates and completion remain unchanged. |
| Program wrapper owns concrete pending-link state | Backend weak-key pending records against the same existing wrapper/association/revision, with supersession, one-time completion and candidate-only cleanup. |
| Framebuffer wrapper downcasts backend target to check completeness | Backend realization/completeness service, keyed by attachment revision and routing. Framebuffer owners dispatch native configuration through private RHI contracts. |
| Uniform page/ring post-upload GetError | No ambient error consumption. Actual exceptions still abort new publication, retain dirty work and preserve the last completed range. |
| Remaining direct wrapper native draw/residency/surface execution | Unused EBO draw helpers removed. Optional bindless owner and explicit window-surface discovery use private RHI services. Existing resource identities and manager authority remain unchanged. |

Task-to-document traceability:

- Wrapper ownership, legal inputs, replacement, cleanup and consumption refs -> proposal Public operations
  and resource inputs / Resource creation and adoption; contracts Resource references and factories /
  Internal resource execution; Rendering.GpuResources.md; pinned creation/publication
  3f307365:docs/Rendering.CommandPublication.Proposal.md and unchanged texture reference
  b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md -> existing owner/manager handoff, private native
  lowering, exact original-context cleanup and affected owner/borrow/view regressions.
- Engine hook/resource publication, layout API removal and selective scopes -> proposal Engine integration
  and migration boundaries; contracts Operation signatures and dispositions / Submission and failure
  rules; Rendering.ShaderBindingState.md Prepared resource submission -> complete supplied-set validation,
  retained backing guards, shared suppression/publication and target-specific empty binding tests.
- Streaming translation and native pixel helpers -> the same public-input/owner contracts and
  TextureStreaming.Architecture.md Upload Request Model -> semantic CPU/PBO request paths and existing
  padded row/readiness/certificate tests. The backend owns selectors and restoration.
- Pending link and pass completeness -> proposal Shader and pipeline objects / Attachment-based pass
  targets, plus the selected phase's owner/public-operation authority -> backend bookkeeping on existing
  identities, coherent metadata completion and revision-keyed target validation.
- Uniform result handling -> contracts Submission and failure rules and pinned command-publication
  operation policy -> actual interrupted upload preserves dirty work/old range; unrelated native errors
  remain available to their original caller, for mapped and unmapped publication lifetimes.

The outer MaterialAtlasNormalDepthGpuBuilder, PBRCompositeRenderer and LumOnRenderer restoration scopes
around migrated operations are removed. SceneColorParticleDrawScope remains a genuine raw engine
particle callback/blend restoration boundary, not a redundant DSA fallback. Its wider engine integration
classification is retained for 5e. Native GBuffer/cloud acquisition and error-policy reconciliation,
RenderPass helper placement, combined graph qualification and the explicitly listed inherited 5e fixture
failures remain open; this receipt does not qualify them.

Validation used HEAD c7d055c69e031c40ead5655bb6da82a7339e89e3 plus this working candidate. The
3411-file source/shader/tool-input fingerprint is
0e84d3fdd52a87eaf7c8874e379285f848cbd6dd00407603c7418e466a60f040
(path, NUL, bytes, NUL in sorted repository-path order; docs excluded).
The machine-readable receipt is artifacts/rhi-phase5d/source-candidate.json.

| Receipt under artifacts/rhi-phase5d/candidate | Result |
| --- | --- |
| qualified-debug-build.log | Canonical Debug test-project build: 0 errors, 333 warnings. |
| qualified-release-build.log | Canonical Release production build: 0 errors, 334 warnings. |
| qualified-focused-tests.log, qualified-focused.trx, qualified-focused-filter.txt | 227 passed, no failures/skips. Covers engine hook/terrain/relief integration, sampled-slot failure, owner/reference/view lifetimes, uniform interruption/reuse, pending programs, framebuffer operations and CPU/PBO transfers. |
| qualified-binding-cache.log, qualified-binding-cache.trx | 8 generated-resource and vertex/multi-binding cache checks passed, no failures/skips. |
| final-dependencies.log, final-dependencies.trx | 53 registry, disposal, association, initialization-intent, format and capability checks passed, no failures/skips. |
| final-generator.log, final-generator.trx | Full generator suite: 157 passed, no failures/skips. |

Builds used the canonical default shader producers with no shader suppression, alternate package path
or instrumented Release configuration. The inherited frame-hook boundary fixture now clears before
activating engine shader handoff. Native relief fixtures activate their actual engine owner, moving
world fixtures refresh their native player before callbacks, and bindless residency checks retain the
issued handle instead of querying zero after disposal. Uniform failure tests interrupt the actual upload
boundary; they no longer misattribute ambient GL errors. The framebuffer composition catch covers
validation failures, preventing a caught failure from committing an otherwise partial transaction.

Second source review and the independent audit-stage-completion workflow reconciled every 5d item
with the linked public-input, ownership, initialization, publication and migration contracts. Source
inspection confirms no remaining ordinary wrapper/helper execution bypass within this unit, no obsolete
slot-binding facade, no parallel resource registry or dispatcher, and no native error drain introduced
as command success detection. Source fingerprint recheck found no drift after qualification.
All required 5d findings are resolved. Phase 5 and 5e remain open. These headless checks do not claim
installed-game startup, visual acceptance or measured performance. No staging or commit was performed.

## Combined rendering dependency closure (Phase 5e)

The candidate is based on HEAD `c1893abd52f6cdde14f0c8cc2216e518bf24dd19`. Earlier E and 5b–5d
receipts remain historical checkpoints; this section qualifies their combined immediate-execution graph.
Command recording and final integrated/package acceptance remain separate unchecked work.

### Consulted authority and implementation traceability

| Work | Controlling source | Implementation and observed verification |
| --- | --- | --- |
| Complete E gate: executable/PSO lifecycle, geometry, target realization, logical uniforms, boundaries and immediate consumers | Proposal shader/pipeline objects, resource creation/adoption, immediate execution, pass targets, backend publication and representative verification; Contracts authority/factories/executables/pipelines/submission/uniform/source ownership; E work packages above | Existing final RHI context/backend, fixed-generation objects, manager registry and logical publication remain singular. Combined and additional suites exercise shader creation/binaries/pending/reload, pipeline identity, resources/geometry, target lifetimes, uniform allocation/publication, binding suppression and original-context cleanup. |
| Cloud storage/executable acquisition and content certificates | Same authority; GpuResources query/retirement policy; PBR.Clouds borrowed map contract; archived CommandPublication private handoff and TextureRef identity contracts | `GpuResourceManager.AcquireExternalFacts` reuses available canonical entries; native readers execute behind query services. `CloudNativeMapAdapter` retains expected layout and content policy without native selector changes, validity probes or error drains. Explicit upload/generation outcomes, skipped callbacks and exception withdrawal remain producer-owned. A covered-reader rejection test preserves an unrelated ambient native error. |
| Engine framebuffer and callback execution | Proposal permanent engine boundary/backend publication; Contracts authority and submission; GpuResources explicit integration | `OpenGLRHIContext` admits named native integration operations, returning no backend. Backend services own GBuffer attachment transactions, routing, blend application, external completeness, engine mesh upload/deletion, patch selection and particle selector/restoration scopes. `VerifyBlendState` and `RelinquishIfDeleted` are removed. Original manager association is checked after attachment observers before completeness; original-context admission surrounds callbacks. |
| Specialized passes and wrapper cleanup | Proposal pass validation/restoration and no-shim migration; Contracts source ownership | RenderPass, RenderPassSequence, RenderPassTargets and their native descriptions live under `Rendering/RHI/Backends/OpenGL/Passes`. Only backend pass lifecycle starts a pass; validators retain specialized checks. Unused indirect-buffer binding scopes and redundant framebuffer scopes are removed with callers. No facade or duplicate executor replaces them. |
| Inherited fixture reconciliation and affected B–D evidence | 5e inherited-failure task; Proposal 4.5 baseline and representative regressions; archived metadata/creation contracts | Observers target actual OpenGL4 operations; exact native context/deferred-delete cases preserve external semantics. Captured shader tests use the packaged shader's real contract. Engine framebuffer fixtures model external surfaces; sparse discarded outputs use semantic holes. Native draw failure/reentry observers act at backend packet preparation with resource guards held, rather than expecting removed engine mesh callbacks. |

### Dependency classification

Source inspection includes imports, qualified names, aliases, fields/constructors/casts, direct helpers,
native calls and locally retained cache references. Ordinary manager/resource/semantic-object/render
execution does not retrieve a concrete backend. Remaining native dependencies have explicit ownership:

- OpenGL backend modules and StateCache's native knowledge/restoration collaborate under the existing
  execution owner. Cache final naming/layout consolidation remains scheduled separately.
- RHICommandContext composition selects the concrete implementation; semantic objects retain only
  their neutral lifetime/realization contract. No context backend escape property is restored.
- EngineStateCallMap, NativeAdapters and EngineStateCalls preserve exact native signatures/outcomes.
  EngineStateTargets discovers engine call sites; it does not provide portable rendering dispatch.
- GBuffer, cloud external completeness, terrain program/stage replacement and patch selection,
  fullscreen engine upload/retirement and particle redirection use restricted native integration.
- Frame/terrain/atmosphere hooks retain linked engine membership, locations and sampler/block assignment
  at native executable preparation. Ordinary texture/sampler and uniform/SSBO binding goes through RHI.
  FrozenLighting.BindBase, SceneColorProgramBindings.BindScene and chunk-slot parameter BindBase retain
  their existing semantic buffer routes; no second integration binding path is introduced.
- GpuProgramLayout's raw linked-interface query/assignment helpers serve explicit engine preparation;
  generated owned shader interfaces remain supplied metadata. ShaderPreparedSubmission is backend-used
  pure validation; native format translation compares canonical facts without issuing commands.
- GlDebug, GpuSupport and GlExtensions retain context admission, cached capability discovery and DEBUG
  diagnostics. Source mutator references identify native methods; they do not execute rendering.

### Correctness fixes found by combined qualification

The disabled surface-lighting fallback buffer now reserves its authoritative header plus one record,
matching the reflected minimum storage-block extent even when capture is disabled. Validation remains
strict. Immutable framebuffer declarations are scoped by original context and native ID inside the
thread-local cache; context switching cannot confuse equal names. Admission and declared-route reuse
discard terminal manager associations before deferred wrapper cleanup, without native probes; a
deletion-before-disposal regression and the independent PSO context workloads cover both cases.
Cloud output-mode changes release obsolete target/pipeline recipes before installing a shader
variant with a different fragment-output contract; ordinary asset reload remains a coherent candidate
installation. Equivalent-format surface replacement reuses its compatible fixed pipeline.

Framebuffer fixtures now select an engine surface without replaying their original owned attachment
set over GBuffer's extension. The routing fixture exercises the original external framebuffer IDs and
injects failure during restoration after both owners were touched. Hostile-state cleanup deliberately
invalidates cached knowledge; tests distinguish that from successful native program restoration. A raw
incoming-program snapshot grants no ownership: explicit retirement produces a fatal restoration error,
unknown program knowledge and withdrawn capture publication, without rebinding the deleted name. Raw
engine raster fixtures explicitly establish their input raster state rather than inheriting prior tests.
Core 4.5 capability tests no longer demand older-version fallback behavior. The obsolete EBO draw-method
reflection check is removed with the retired wrapper execution API; actual indexed draws are qualified
through semantic geometry tests.

### Combined validation

The following receipts qualify the combined immediate-execution candidate; failed historical runs and their limits remain explicit.

The frozen source contains 3,417 files at HEAD `c1893abd52f6cdde14f0c8cc2216e518bf24dd19`, with
SHA256 `80fc0816dfb4b617f4aee4bc0d53c85f3fd5403648590746931006313057ca7e`.
The fingerprint excludes documentation and build outputs. Receipts are under
`artifacts/rhi-phase5e/candidate/`; none of this work was staged or committed.

- Canonical Debug test-project compilation: `final2-debug-build.log`, zero errors. Combined immediate
  rendering/resource regressions: `final2-debug-combined.trx`, **722/722 passed**, zero skips.
- Additional affected GPU checks: `final-debug-additional.trx`, **155/155 passed**. Relevant unit checks:
  `final-debug-unit.trx`, **124/124 passed**. Generator suite: `final-generator-debug.trx`, **157/157 passed**.
- Canonical Release test-project compilation: `final4-release-build.log`, zero errors/204 warnings.
  Existing test references to DEBUG-only counters are now guarded; their actual execution, restoration
  and output assertions remain unconditional. Final Debug recompilation and all 13 affected cloud
  checks passed (`final4-debug-build.log`, `final4-debug-cloud.trx`).
- Ordinary optimized Release combined execution: `canonical-release-combined.trx`, **700 passed,
  22 failed**, zero skips. Exact failures remain in `canonical-release-failures.txt`. These cases depend
  on native method interception for call observation or fault injection. The failed run is retained;
  it is not represented as a green Release suite.
- Ordinary Release additional GPU/unit/generator suites passed **155/155**, **124/124** and **157/157**
  (`canonical-release-additional.trx`, `canonical-release-unit.trx`, `canonical-generator-release.trx`).
- A separate Debug test harness also ran against the canonical optimized Release production assembly:
  `release-final.trx`, **171/171 passed**, zero skips. Production assembly SHA256 was
  `D7F0B3653CBB074FD0A5E98C0ED8C9651283782C93C274C5D581773CD220F798` in both locations.
  This is a focused Release-product receipt, not a replacement for the failed ordinary Release run.

An earlier broad non-GPU selection recorded 2,146 passed, eight failed and nine explicit skips.
Five obsolete architectural fixtures were corrected and are covered by the passing relevant unit suite.
The other three asynchronous timing cases passed in isolation (eight theory cases); that rerun does
not make the original broad selection green. No live startup, visual, frame-time or optional external
producer-import qualification is claimed. Recording and final integrated/package acceptance remain
assigned to the later unchecked work.

The same canonical Release binaries passed **722/722**, zero skips, with process-only
`COMPlus_JitNoInline=1`, `COMPlus_TieredCompilation=0`, and `COMPlus_ReadyToRun=0`
(`canonical-release-noinline.log` and `.trx`). This confirms the native observer/fault-injection
sensitivity to optimization. The command used the same combined filter and Release `dotnet test`
with `--no-build --no-restore` (exact invocation: `release-noinline-invocation.txt`); no production instrumentation, DEBUG definition, substitute executable
or fallback path was added. The canonical Release assembly hash and final source fingerprint remained
unchanged (`noinline-fingerprint-after.txt`). Ordinary optimized Release remains 700/722, as recorded above.

Second implementation review and independent stage-completion audit consulted the inherited proposal,
contracts, inventory/API scope, resource/cloud/binding documentation and archived controlling handoff
and reference contracts. Each original Phase 5 and 5e requirement maps to the routes and behavioral
receipts above. No required implementation or documentation discrepancy remains. Exact native integration,
cache observation and backend composition remain explicit final boundaries; they are not portable
submission bypasses. StateCache naming/layout consolidation and command recording remain later work.
The combined Phase 5 gate and dependency closure are satisfied within these qualification limits.


## Single-use recording qualification (Phase 6)

### Authority and traceability

Phase 6 inherits the mandatory migration contract and approved proposal, especially Execution and
recording lifecycle, Geometry inputs, Attachment-based pass targets, Backend dispatch/publication,
and Representative outcomes. Contracts' operation disposition table and List lifecycle/storage define
the named recordable surface. Retained resource/binding rules and archived publication/reference
contracts continue controlling identity, snapshots, dirty history and original-context cleanup.

| Task group | Controlling source | Implemented route / required evidence |
| --- | --- | --- |
| Headers, typed pages, ownership, close/reset/cancel | Proposal recording lifecycle; Contracts list lifecycle/storage | Commands storage and RHICommandList lifecycle; copied client bytes/spans, no native context recording, page reuse and terminal-state regressions. |
| Complete recordable operation surface | Contracts operation signatures/dispositions; migration unit F | Focused recording/interpreter families for buffers, images/blits, graphics/compute, bindings/uniforms, barriers/fences/semaphores, queries/readback and capture. No factories/storage/mip commands or callback payloads. Each opcode calls final immediate context operations. |
| Execution identity, admission and failure | Proposal submission/retirement/boundaries; GPU resource lifetime | RHICommandContext.Commands, fixed origin/thread, whole-footprint admission, no widening or caller-pass cleanup, current resource/borrow checks, first index/original error, independent cleanup error and pending request withdrawal. |
| Logical uniforms and result ownership | Prepared resource submission; Contracts publication/result rules; archived command-publication proposal | Captured snapshots execute through existing publisher in the current epoch; missing allocator fails, newer edits stay dirty. Existing query/readback tickets and RHIFenceTicket distinguish unissued markers from independent enqueued completion. |
| Representative depth/graphics/borrowed paths | Proposal representative verification; GPU resources external-consumer rules | Optional DepthHierarchyPass recording retains immediate default and freshness policy. Recorded cloud fixture submits during the original native geometry borrow. Indexed batch, resource snapshot, image and pass restoration checks compare actual output and state. |
| Review and reconciliation | Phase 6 completion and mandatory receipts | Source audit, delegated Debug/Release checks, second implementation review and independent stage audit; final receipts below. |

### Routes and source closure

The new opcode interpreter owns argument retrieval and ordering only. Native execution, metadata,
resource/usage guards, limits, feedback checks, suppression and target realization still belong to
existing immediate operations/backend services. No OpenGL import, manager/cache lookup or native
validity inspection occurs in recording. Existing immutable descriptions and binding tables are retained;
variable spans and upload data are copied. No obsolete executor existed to shim or retain for recording.
General creation/configuration, mapping, CPU waits, mips and native engine interception remain immediate.

Explicit helper sampled/uniform slots augment immutable boundary declarations, and backend boundary
admission projects those supplied slots through existing selective restoration. Ordinary boundaries
without helper declarations allocate no recording storage. Lists retain no extra resource registry,
metadata history or captured native identities. Shader/PSO/table references stay fixed; texture ownership
and borrowing retain existing manager rules. Cancellation clears both payload references and footprint
collections; issued result ownership survives reset and disposal.

### Validation

Fresh baseline Debug build and 102 focused RHI tests passed before implementation
(artifacts/rhi-phase6/baseline). Final candidate receipts and exact invocations are under
`artifacts/rhi-phase6/final2`:

- Canonical Debug and Release builds passed with zero errors (532 and 539 warnings respectively).
- Combined recording, immediate RHI, depth, cloud, uniform and engine-boundary checks passed
  **178/178** in Debug, with zero skips.
- Ordinary optimized Release passed **177/178**, with zero skips. The existing native fault-injection
  case `RHIBufferTests.FailedCreationReturnsNoOwner` missed its optimized hook. All new recording,
  completion, fence, depth/cloud and boundary checks passed in this ordinary run.
- The same unchanged Release binaries passed **178/178**, zero skips, using process-only
  `COMPlus_JitNoInline=1`, `COMPlus_TieredCompilation=0` and `COMPlus_ReadyToRun=0`.
  This qualifies the observer limitation; it does not replace the ordinary 177/178 receipt.
  No instrumented Release product or source-level no-inline workaround was introduced.
- Supporting CPU checks passed **11/11** in both configurations and generator checks passed
  **157/157** in Debug (`artifacts/rhi-phase6/final`). Subsequent changes concerned only the
  recorded fence diagnostic and GPU fixture admission, covered by the final combined selection.

The final source manifest covers 3,214 files, with zero drift during validation (`source-drift.json`).
SHA256 of `source-before.csv` is
`13FF1D011107BD632BD6BA137ACC34C8C8CE64E27C5A7F252CB001CFAC5A12B8`.
Production assembly hashes in `runtime-hashes.json` are
`05E34DAF0D8F9830119C55C0DE1C9DB8DC80892F2BCE7F8FA6D538682C492A5F` (Debug) and
`C35D1210C2403AEB57F68EA2FE0E078819C31F2BD9C19E00A3E5EF7F484B5B5A` (Release).

Cloud fixture assertions now require the shared validator's precise range/packing exceptions while
retaining rejection and native ownership checks. Boundary fixtures explicitly admit their context before
private cache capture; cold admission during capture was a fixture defect, not a missing production cache.

Second implementation review and independent stage-completion audit directly reconciled the selected
tasks with the proposal, contracts, retained resource/binding rules and archived controlling contracts.
Cancellation/reference release, caller-owned scope protection, missing uniform epochs, retirement,
result ownership and helper-slot restoration were rechecked after corrections. No required implementation
or documentation gap remains. Cache naming and final integrated/package qualification remain later work.
No game launch, live appearance, frame-time measurement or external producer-import qualification is claimed.

## Backend cache closure (Phase 7)

### Authority and source traceability

The selected checklist inherits the approved RHI proposal's backend publication, exact integration,
no-shim replacement and source-layout requirements. Contracts' ownership table and internal resource
execution sections control the manager handoff. Current resource, prepared-binding, metadata inventory
and API coverage contracts retain their original associations, query policy and owner exclusions.
Archived creation/publication and texture-reference contracts remain normative for private initialization
and consumer references; they do not restore older-context execution alternatives.

| Task / controlling source | Implemented closure and evidence |
| --- | --- |
| Audit A–F / migration family receipts; proposal engine integration | Reinspected buffers, image/backing/samplers, auxiliary/results, shaders/PSOs, geometry/passes/bindings and recorded commands. Their focused backend modules remain sole execution owners. No internal native draw/upload/create/delete path reappears outside the backend. |
| Cache split / proposal responsibilities; Contracts source ownership | The one thread-local type is `OpenGlStateCache` under backend `State`, with Bindings, FixedFunction, Observation and Boundaries modules. All compiled callers and source links change together; no old class/file alias remains. Stateless typed `OpenGLStateEffects` modules own core fixed-state native effects; cache methods retain field knowledge, admission and suppression. Native selector/restoration observation remains with that same backend knowledge owner. |
| Remove residual executors / mandatory migration contract | Deleted cache FramebufferTexture2D/Layer commands; direct test consumers use the existing exact backend attachment methods. Removed the unused production arbitrary-callback ExecuteExternal surface, indexed raw-capability helper and face-selector polygon forwarding helper. Managed capability setters accept represented fields only; exact adapters retain raw enum behavior. No replacement forwarding facade was added. |
| Exact adapters / API owner boundary; archived publication contract | EngineStateCalls lives in backend State/External. Fixed-state overloads execute their original legacy binding call once, without managed clamping/validation/suppression, and withdraw affected knowledge. Existing field admission protects declared restoration without rejecting covered calls. Stencil faces and indexed blend slots retain independent coverage. Signature catalog/IL replacement still covers only GL/Core, Ext and Arb; backend calls are outside the built-in rewritten assemblies. |
| Baseline / proposal OpenGL 4.5 and fixed implementation | Removed old cache alternatives for independent blending, clip control, sample shading, fixed-index restart, tessellation and separate shader objects. Actual cached limits/profile differences and optional beyond-baseline operations remain. Manager creation/acquisition/retirement still invokes private RHI contracts, never retrieves the backend, and retains sole metadata authority. Its existing cache references are guard/knowledge collaboration, not native execution. |
| Current documentation / checklist reconciliation | Resource, shader binding, pipeline authority, engine integration, authoring and shader asset/cache documents describe final APIs and ownership. Active inventory/coverage and parent references follow the implemented routes. Historical implementation receipts stay here; core architecture documents no longer describe removed contexts, activation APIs or older-context fallbacks as current behavior. |

Static source review includes qualified/native aliases, exact adapter registrations, manager/readers,
owner wrappers, context composition, all cache modules and recorded interpretation. Remaining native
dependencies outside backend execution are existing capability/admission discovery, DEBUG diagnostics,
and explicit engine executable membership/location preparation. These do not create internal execution
escape hatches. No parallel cache, registry, metadata history, dispatcher authority, retry route or new
resource hierarchy is introduced. Local links in the updated live contract documents resolve.

### Validation and review

Fresh canonical Debug baseline passed 159/159 focused tests with zero skips
(`artifacts/rhi-phase7/baseline`). Consolidated candidate receipts remain under `artifacts/rhi-phase7`.
Final consistent-source receipts are under `artifacts/rhi-phase7/final5`:

- Canonical production/test builds passed with zero errors: Debug 533 warnings, Release 540 warnings.
- The broad 425-case selection passed **424/425** in Debug and **416/425** in ordinary optimized Release,
  with zero skips. The complete failed runs remain recorded, not represented as green suites.
- The same Release runtime/test binaries passed **424/425** with process-only
  `COMPlus_JitNoInline=1`, `COMPlus_TieredCompilation=0`, `COMPlus_ReadyToRun=0`.
  All eight ordinary observer/fault-injection failures resolve under those controls. No instrumented
  Release product, production no-inline attribute or alternate runtime path was introduced.
- All **424** cases other than the full-lighting numerical case pass in Debug and controlled Release.
  These include exact signatures/generic rewriting, native fixed-state arguments/errors, duplicate
  execution, categorized suppression, partial boundaries/restoration, metadata interception and RHI
  resource/shader/pipeline/recording contracts. Generator suites passed **157/157** in both configurations
  (`artifacts/rhi-phase7/final/Generator-Debug.trx`, `Generator-Release.trx`); later edits do not touch
  the generator or its contract inputs.

The full-lighting fixture had stale R32F color storage for comparison samplers and omitted two composite
fragment outputs. Its declarations now use depth storage and all three MRT outputs; ownership coverage
checks all 17 actual images. The now-reachable direct-only numerical oracle still fails at pixel zero,
green: expected `9.213684`, actual `9.2109375`, delta `0.002746582`, tolerance `0.002`.
A tentative half-precision oracle projection did not explain every pixel and was reverted. The original
independent sum and tolerance remain unchanged. Production shaders/numerical algorithms were not altered.
This is an explicit unresolved integrated-lighting qualification item in Phase 9, not a cache/source
boundary completion claim or a waived numerical failure. No integrated all-green result is claimed.

`identity.json` records 3,185 frozen source/project files and zero drift. Runtime SHA256 values are
`5BAA75C5B9B0032679CBD3F26C2C97B3355F2DB7F1F9C1DF8EA751F06B841EE7` (Debug) and
`72BA3D92BE6104296A164F9B2BB70BA053904A6EA8E86AACF3EF0FBF13AF06DA` (Release).
The identity file also names both test assembly hashes; `invocations.txt`, `filter.txt`, build logs and
TRXs retain the exact candidate and execution conditions. HEAD remained
`0dbbc637d77916bf8a2c100f7a5b32d6d1062f75`; no changes were staged or committed.

Second source review and independent stage-completion audit directly reconciled the selected checklist
with the linked proposal/contracts, current resource/binding documents and retained family receipts.
The obsolete raw-cache helper removal and exact field admission were rechecked after correction.
The review then reported no remaining required Phase 7 gap. A subsequent wrapper-dependency audit found
remaining cache-policy, creation/acquisition, publication, capability and diagnostic boundary violations;
the implementation plan's Phase 8 explicitly corrects these and requires fresh combined qualification.
The earlier receipts do not establish complete architectural closure. The broader numerical mismatch,
canonical package identity, installed-client behavior and user-run visual acceptance remain Phase 9
qualification work. No game launch, performance measurement or package/live acceptance is claimed here.

## Framebuffer owner admission qualification (Phase 8a)

Authority: implementation-plan Phase 8a and its inherited mandatory migration contract; approved proposal
“The proposed boundary”, “Attachment-based pass targets” and execution/recording constraints; final
contracts “Authority and ownership”, “Internal resource execution” and boundary admission; retained
resource ownership/retirement and prepared-binding contracts. Archived creation/publication
(`3f307365`) and texture-reference (`b9fca04c`) proposals were consulted for retained manager handoff,
operation failure and nonowning reference semantics. Phase 8g's approved removal policy controls purges.

| Requirement and source | Final route and observed evidence |
| --- | --- |
| Owner configuration guards; proposal/backend authority and contract private handoff | SetAttachmentCore, RemoveAttachmentCore and ResizeCore call RHI RequireMutableFramebufferOwner; backend checks context, immutable aliases and owner guards. Attachment configure/refresh also admit at execution. Dirty Bind refresh now precedes selection, so rejection cannot change native framebuffer selection. Static/attachment/boundary tests cover alias rejection, no-op removal, resize and restoration. |
| Immutable configuration lifetime; resource original-context retirement | StaticGpuFramebuffer retains only an IDisposable lease supplied by RHI SealFramebufferConfiguration. OpenGLFramebufferConfigurationLease uses the existing immutable declaration store and unregisters its captured original ID on release or context retirement. Worker cleanup makes no native call. Owner backing guards remain balanced on disposal/detach; static tests cover view/source guards, worker disposal and context retirement. No new registry or concrete backend field was added to an RHI object or ordinary wrapper. |
| Routing knowledge and lifecycle; 8g removal policy | Wrapper deletion/detach/publication/refresh no longer invalidate routes. Existing observed deletion publication removes retired native routing. ScreenResourceManager no longer purges all knowledge; the uniform renderer retains controller.BeginFrame without cache purging. New FramebufferOwnerKnowledgeTests cover detach, published borrowed refresh, callback ordering and retained selectors/routes. The old routing fixture now preserves same-object routing and represents a native change through EngineStateCalls instead of relying on unobserved GL plus a later purge. |
| Preparation admission; proposal boundary and binding lifetime contracts | GpuProgram.RequireOutsideSubmission and EngineGeometryInputs use the RHI preparation gate. Its backend composition check consults existing thread-local boundary authority without creating/admitting a native backend; CPU-only shader asset work still works without a current context. Engine geometry/boundary tests and GpuProgramWorkflowTests cover these paths; UniformLifetimeTests retains frame epoch checks. |
| Dependency/document closure; mandatory no-shim contract | Targeted wrapper/helper files have no OpenGlStateCache or concrete-backend imports/types/calls. No old-path forwarding shim or second execution/metadata authority was added. Current resource and binding documentation describes the lease, preparation admission and retained knowledge. Creation/acquisition/diagnostic/interception corrections assigned to 8b–8h remain explicitly incomplete. |

Second source review and the audit-stage-completion workflow independently reconstructed the selected
contract and linked authority, inspected the corrected source/callers and reconciled the behavioral
receipts. No required Phase 8a issue remains. The early in-flight build found a missed no-op alias
removal guard and a stale purge-dependent fixture; both were corrected before frozen validation.

Final delegated shader-enabled production/test compilation succeeded in Debug and ordinary Release.
Debug: 139 focused plus 36 lifecycle/workflow cases passed. Release: 171 focused/lifecycle plus four
CPU workflow cases passed. Both total 175, with zero failures/skips and no process JIT overrides.
Production/test source timestamps did not change after final Debug validation began at
`2026-10-27T20:15:46.8908894Z`; subsequent edits only recorded documentation/checklist evidence.

Logs: `artifacts/phase8a-final-debug.log`, `phase8a-final-debug-lifetimes.log`,
`phase8a-final-release.log` and `phase8a-final-release-workflow.log` in the same directory.
Matching TRX files are under `artifacts/TestResults/` with those prefixes.

Reproduction: `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug
--no-restore --tl:off -v:minimal --filter <selection> --logger trx;LogFileName=phase8a-final-debug.trx
--results-directory artifacts/TestResults`, keeping the logger argument quoted in PowerShell.
Core selection is OR-joined `FullyQualifiedName~` terms for StaticGpuFramebufferTests,
FramebufferOwnerKnowledgeTests, GpuFramebufferAttachmentTests, FramebufferAttachmentNotificationTests,
FramebufferAttachmentDiscoveryTests, EngineBoundaryEntryTests, EngineBoundaryContextTests,
EngineBoundaryRestorationTests, EngineBoundaryBindingTests, FramebufferRoutingCacheTests,
FramebufferRoutingDsaTests, RHICommandContextIntegrationTests, UniformRing, EngineFramebufferRebuildTests,
EngineFramebufferRoutingTests, GpuFramebufferExecutableCreationTests and EngineGraphicsGeometryTests.
Supplemental Debug uses `--no-build --no-restore` for GpuProgramWorkflowTests, UniformLifetimeTests,
ShaderRegistryLifetimeTests, ComputeShaderAssetTests and ShaderAssetCandidateTests. Release uses
`-c Release` with core plus supplemental except GpuProgramWorkflowTests, then runs that class separately
with `--no-build --no-restore` and the matching receipt name.

Package identity, live rendering acceptance and integrated lighting remain later qualification work.
Complete interception, startup purge/API removal and handler-level direct state propagation remain 8g
requirements; passing these tests does not claim those requirements are implemented. Nothing was staged
or committed.

## Owner creation boundary qualification (Phase 8b)

Controlling sources: the implementation plan's 8b tasks and mandatory migration contract; proposal
“Resource creation and adoption” and “Engine integration and migration boundaries”; contracts
“Internal resource execution” and “Resource references and factories”; retained resource/metadata
ownership in Rendering.GpuResources.md and Rendering.GpuResourceHandles.Inventory.md. The archived
creation/publication proposal at `3f307365:docs/Rendering.CommandPublication.Proposal.md` controls
private recipient handoff, failure cleanup and query policy. The texture-reference and prepared-binding
contracts remain unchanged; acquisition/recovery and diagnostics retain their explicitly open units.

| Required correction | Final route and evidence |
| --- | --- |
| Framebuffer owner and attachment renderbuffer creation | `GpuFramebuffer` constructor → `RHICommandContext.AllocateFramebufferOwner` → private execution contract → backend → existing `GpuResourceManager.CreateFramebuffer`. `CreateRenderbufferStorage` uses the equivalent `AllocateRenderbufferOwner` route. No public recipient-bearing factory or constructor recursion is introduced. Dynamic/static factories retain image composition and borrowing policy. |
| Backend geometry and program recipients | Remove `GpuVao.Create` and `GpuProgramObject.Create`. Geometry realization calls private `CreateGeometryVertexArray`; shader realization and engine tessellation call private `CreateProgramOwner`. Those backend methods complete the existing manager handoff and clean failed candidates; the internal wrapper constructors declare only unpublished recipients. |
| Unused pipeline factory | Remove `GpuProgramPipeline.Create` without adding a replacement production factory. Existing private native initialization remains available to explicit integration machinery. Native interception/lifecycle fixtures initialize unpublished recipients in test-only `NativeResourceSetup`; no production operation exists solely to preserve test setup. All old factory callers were updated directly. |
| Failure and ownership | `OwnerCreationBoundaryTests` injects failure after native allocation for dynamic/static framebuffers and renderbuffer attachments, after framebuffer attachment configuration, and during owned-image projection. Cleanup retires only the candidate and preserves borrowed images. Failed attachment construction queues only owned backing storage in its captured disposal lifetime. Manager creation/publication, original association and context cleanup remain singular. |

Source review covers production callers, all mechanically updated native fixture callers, private
initialization dispatch and unchanged manager transaction cleanup. Remaining wrapper acquisition,
capability, label, retention and interception work is assigned to 8c–8h, not claimed by this unit.

Second source review and the independent audit-stage-completion workflow reconstructed the four tasks
and their linked authority, then checked private recipient construction, caller closure, single manager
publication, candidate cleanup and documentation against the final evidence. No required 8b gap remains.

Delegated canonical shader-enabled builds passed in Debug and ordinary optimized Release. The final
focused matrix passed 235/235 in each configuration, with zero failures/skips, including all six new owner
creation failure cases. No JIT overrides or instrumented Release configuration were used. Final source
last changed at `2026-10-27T20:45:16.6283117Z`; canonical Release compilation began at
`2026-10-27T20:45:31.2049289Z` and Debug at `2026-10-27T20:45:50.3247432Z`. Subsequent edits only
recorded documentation/checklist evidence.

Initial qualification exposed two obsolete VAO formatted-ToString tests: the pre-change owner already
had no such implementation. Those assertions were removed; identity, disposal and diagnostic-name tests
remain. An inherited native-dispatch fault fixture also depended on patching an OpenTK leaf that could
already be inlined in Release. It now replaces the exact compiler-checked dispatch inside the real operation
and invokes that operation through reflection, preserving its actual admission/finally cleanup while avoiding
inlined callers. The other cases still exercise call-site interception. No production behavior or JIT policy
was changed for fixture observation.

Final logs are `artifacts/phase8b-accepted-debug.log`, `phase8b-accepted-release.log` and corresponding
`-build.log` files. Matching TRX files are in `artifacts/TestResults/`. Canonical build/test commands use
`dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c <Debug|Release>
--no-restore --tl:off -v:minimal --filter <selection> --logger 'trx;LogFileName=<receipt>.trx'
--results-directory artifacts/TestResults`; the full final matrix reuses those built binaries with
`--no-build --no-restore`. The existing pinned shader compiler is discovered using
`NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages`; shader generation remains enabled.

The selection OR-joins `FullyQualifiedName~` for OwnerCreationBoundaryTests,
GpuFramebufferExecutableCreationTests, StaticGpuFramebufferTests, GpuFramebufferAttachmentTests,
FramebufferOwnerKnowledgeTests, GpuManagerCreationFoundationTests, GpuRemainingCreationTests,
GpuVaoIntegrationTests, GpuResourceKindLifecycleTests, GpuResourceOperationContextTests,
GpuResourceManagerDeletionQueueIntegrationTests, GpuResourceRetirementRegressionTests,
RHIShaderCreationTests, EngineGraphicsGeometryTests, TerrainTessellationEngineDrawTests,
GpuProgramMetadataInterceptionTests, GpuProgramNarrowPublicationTests, GpuProgramUniformMetadataTests,
GpuVertexMetadataInterceptionTests, GpuVertexWrapperMetadataTests, CommandBindingKnowledgeTests,
ClientRenderingCompositionTests and CompleteGraphicsDrawTests.

Package identity, integrated lighting and user-run live acceptance remain later qualification work.
The agent did not stage or commit changes; existing user staging was left untouched.


## Acquisition and image-publication boundary (Phase 8c)

Authority: the implementation plan's mandatory migration contract and 8c tasks; the RHI proposal's
Resource creation and adoption, Backend dispatch and publication and Engine integration sections;
Rendering.GpuCommands.Contracts.md internal resource execution; Rendering.GpuResources.md;
Rendering.GpuResourceHandles.Inventory.md selected-image and canonical-entry contracts;
Rendering.ShaderBindingState.md prepared bindings; archived command-publication proposal
`3f307365:docs/Rendering.CommandPublication.Proposal.md` acquisition/helper dispositions and operation
commit rules; archived texture-reference proposal `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md`.
Existing API exclusions and the unchanged consumer-only texture reference remain in force.

| Task | Final route / source evidence |
| --- | --- |
| Borrowed texture construction and reuse | `BorrowedTexture` calls private RHI `AcquireBorrowedTexture`; the backend admits context, and the existing manager chooses acquisition/reuse. Nonownership and original entry identity remain unchanged. |
| Attachment discovery/reconciliation | `DiscoverFramebufferAttachment` and `ResolveFramebufferAttachments` cross the private execution contract. Manager FramebufferImport/FramebufferImages own reads and one backing snapshot per native object; wrappers retain exact selection/composition and commit references only after resolution. |
| External framebuffer refresh/publication | `PublishImportedFramebuffer` owns replacement, discovery and failure withdrawal. Existing `MetadataOperation` keeps the framebuffer unavailable through backing resolution; mode evidence is reconciled before image projection. Wrapper domain hooks install composition and emit one final event, suppressing intermediate owner notifications. |
| Image construction/validation | Construction explicitly invokes `AcquireAttachmentImage` or `AcquireExternalAttachmentTexture`. `ValidateSlot` consumes supplied snapshots and availability, without hidden reacquisition. Failure clears local projections. |
| Executable adoption/interface fallback | `GpuProgramObject`, `GpuShaderModule` and `GpuProgramInterface` use private RHI import/acquisition operations; native reader selection stays in the backend/manager transaction. No competing shader ownership system is added. |
| Image dependent publication | Remove `GpuFramebufferAttachmentObservers` and wrapper allocation notification calls. Canonical entries remain authoritative. Manager OwnerImages retains weak relationships only for local tools/replacement and skips current-entry images. Local metadata operations and explicit acquisition/recovery publish or withdraw projections. Texture replacement reports dependency errors after allocation/filter completion; failed initialization cleanup lives in backend execution. |
| Verification | Existing external reuse, local failure/recovery, same-ID retirement, view/alias, ownership and shader-import suites, plus ResourceAcquisitionBoundaryTests covering single notifications and guarded import failure/recovery. |

No old-name forwarding APIs, second canonical registry, temporary adapters, forced query-on-validation,
Release instrumentation, benchmark requirement, automatic staging or commit is introduced. Further
capability, label, lifetime and interception cleanup remains in the explicitly incomplete subphases.


Second review and the independent audit-stage-completion pass reconstructed all seven tasks against
the linked authority, reviewed the final source and caller closure, and reconciled the documentation.
No required acquisition/publication gap remains in this unit. Later capability, label, retention and
full interception qualification tasks remain unchecked; this does not establish live visual acceptance.

Delegated canonical shader-enabled Debug and ordinary optimized Release compilation used
`NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages` and
`dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c <Debug|Release>
--no-restore --tl:off -v:minimal --filter <selection> --logger 'trx;LogFileName=<receipt>.trx'
--results-directory artifacts/TestResults`. Debug passed 253/253 with no failures/skips.
Release passed the same 253 cases across two fresh-process partitions (68 and 185), with no failures/skips
in either partition. Those partitions use the same final binaries, `--no-build --no-restore` and unchanged
assertions; no JIT overrides, production NoInlining changes or instrumented Release build were used.

The broad single-process Release run is retained and is not a clean-suite receipt: it passed 246/253.
Seven failures concern order-sensitive Harmony observation/fault injection in
GpuFramebufferExecutableCreationTests.ExternalFramebufferEditsReadOnlyAffectedFields,
GpuProgramMetadataInterceptionTests.DesktopExtStageSelectionForcesFullProgramRestore (two cases),
DesktopExtActiveProgramSelectsUniformOwner (two cases), and
OwnerCreationBoundaryTests.FailedConfigurationPreservesBorrowedImages (two cases).
All three complete affected classes passed together in a fresh process (68 cases); the complementary
selection passed 185. This qualifies focused behavior with an explicit combined-process test-harness
limitation, rather than claiming the broad Release run passed. The association-read fixture's separate
legacy-GL observer was corrected to observe the actual OpenGL4 native query methods.

Final logs: `artifacts/phase8c-accepted-debug.log`, `phase8c-accepted-release.log`,
`phase8c-release-isolated.log` and `phase8c-release-complement.log`; matching TRXs are under
`artifacts/TestResults/`. Latest production source changed at `2026-10-27T21:08:50.1839050Z`;
Debug production/test assemblies were compiled at `21:09:32Z` / `21:09:44Z` and Release at
`21:10:41Z` / `21:10:47Z`. Subsequent edits only record documentation and checklist evidence.

The full selection OR-joins `FullyQualifiedName~` for GpuExternalAcquisitionReuseTests,
FramebufferAttachmentDiscoveryTests, GpuFramebufferAttachmentTests, GpuTextureFailureRecoveryTests,
FramebufferOwnerKnowledgeTests, OwnerCreationBoundaryTests, ResourceAcquisitionBoundaryTests,
GpuFramebufferExecutableCreationTests, GpuResourceMetadataAcquisitionTests,
GpuFramebufferMetadataInterceptionTests, GpuFramebufferMultiviewInterceptionTests,
GpuProgramMetadataInterceptionTests, GpuResourceRetirementRegressionTests,
GpuProgramUniformMetadataTests, RHIShaderCreationTests, CloudNativeAdapterTests, GpuTextureRefTests,
GpuResourceKindLifecycleTests, GpuTextureViewInterceptionTests, GpuTextureDependencyRecoveryTests,
GpuInitialTextureDependencyTests, GpuBufferViewDependencyTests, CloudStoredVolumeViewTests,
ArrayTextureViewTests and GpuMetadataBoundAcquisitionTests. The isolated Release selection contains the
three affected classes above; the complement ANDs the full selection with `FullyQualifiedName!~`
exclusions for those same classes. New boundary coverage comprises nine cases.

Package/integrated/user qualification remains later work. The agent did not stage or commit changes;
user-controlled staging was preserved.


### Semantic resource boundary correction

The selector-only implementation was insufficient: wrappers converted legacy schemas through
`OpenGLResourceSelectors`, while the image codec lived in general integration. The correction replaces
those inputs throughout their owning contracts instead of retaining translation facades.

| Owning area | Completed contract |
| --- | --- |
| Framebuffer wrappers, initialization and canonical attachment dictionaries | `FramebufferSlot` keys and `FramebufferSelection`; declared routing uses color indices with -1 disabled. |
| Texture/renderbuffer owners, initialization, refs and image metadata | `TextureDimension`, `TextureFormat` and a separate `CubeFace`; surface facts use `SurfaceColorBuffer`. |
| Manager selected-field recovery | Project-owned texture/framebuffer fields and program-block kinds cross the existing private RHI handoff. |
| GpuSupport format cache | One cache keyed by resource kind, texture dimension and semantic format; failures do not publish entries. |
| Prepared image validation and caller composition | Consume semantic canonical facts directly, without native enum conversion or queries. |
| Native dispatch, external observation and acquisition query results | Encode/decode once inside the existing backend; actual external adapters may decode actual native inputs. |

`OpenGLResourceSelectors` and the redundant `ImageStorageTarget` selector are removed. The single image
codec resides under the backend. Ordinary wrappers, manager services and capability caches do not call
native codecs. Attachment.Format reads the now-semantic canonical field directly; its obsolete backend
projection operation was removed. There are no compatibility overloads, aliases or forwarding facades.

The audit covered framebuffer configuration/discovery and attachment construction, texture/renderbuffer
allocation and selected-image metadata, manager texture/framebuffer/program field recovery, format-cache
requests, prepared image validation, native readers/observation, and affected production/test callers.
`RHIContractIsolationTests` checks signatures, canonical image/initialization field schemas and managed
method IL for native codec dependencies. A separate schema check keeps build-time shader dimensions
consistent with runtime logical dimensions. Native enum ordinal casts are not used for semantic encoding.

Earlier 138-case selector receipts establish only the superseded signature check.

Corrective validation uses canonical generators and normal optimization:

- Debug: 396/396 passed, zero skips; `artifacts/semantic-resources-matrix-debug.log`.
- Ordinary Release: 396/396 passed in two fresh-process partitions on the same final binaries:
  92 affected-class cases and 304 complementary cases. Receipts are
  `artifacts/semantic-resources-release-isolated.log` and
  `artifacts/semantic-resources-release-complement.log`.
- Shader contract generator: 157/157 passed; `artifacts/semantic-resources-generator-debug.log`.
- Boundary/schema/format/capability core: 27/27 passed; `artifacts/semantic-resources-core-debug.log`.

The exact broad filter is `artifacts/semantic-resources-test-filter.txt`; matching TRXs are in
`artifacts/TestResults/`. The combined ordinary Release run passed 385/396 and remains recorded in
`artifacts/semantic-resources-matrix-release.log`. Its 11 failures concern observer/fault-injection or
call-counter assertions in GpuProgramMetadataInterceptionTests, GpuFramebufferExecutableCreationTests,
OwnerCreationBoundaryTests, GpuControlledTexturePublicationTests and FramebufferRoutingCacheTests.
All five complete classes passed together in the fresh 92-case process; the complement passed 304.
No assertions were removed, no JIT overrides or instrumented builds were used, and this does not claim
a clean combined-process Release run. Debug exercises that complete selection together.

Installed runtime/package acceptance remains later work. The agent did not stage or commit changes.
Subphases 8d–8i and Phase 9 remain open.


## Semantic executable and buffer boundary (Phase 8d)

### Authority and task traceability

The controlling sources are the RHI proposal's resource creation, shader/pipeline, backend dispatch and
migration sections; the implementation contracts' authority/internal execution, descriptor and executable
sections; Rendering.GpuResources.md's initialization, acquisition, executable and buffer contracts; and
Rendering.ShaderBindingState.md's supplied-metadata preparation and publication contract. The canonical
schema inventory/API coverage and parent resource proposal retain manager-owned snapshots, original
associations, availability and notification ordering. The archived creation/publication proposal at
`3f307365:docs/Rendering.CommandPublication.Proposal.md` governs private initialization and the approved
query policy; `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md` retains query-free consumer values.

| Task group | Before → final route / evidence |
| --- | --- |
| Buffer schema, initialization and public facts | BufferUsageHint/BufferStorageFlags → BufferUsage/StorageAccess in GpuBufferMetadata, GpuBufferInitialization, GpuBufferObject and reference consumers. Named allocation/query services encode/decode only beneath OpenGLBufferValues. Immutable permissions, sparse flags and omission validation are preserved. Undefined is the unavailable default; StaticDraw is valid and cannot collide with discarded metadata. |
| Stage schema and adoption | ShaderType → ShaderStage in GpuShaderMetadata, initialization and GpuShaderModule. Creation encodes at native dispatch; observed native stages decode beneath OpenGLShaderValues. All production/test adoption and initialization callers use semantic stages. |
| Canonical executable reflection | ProgramInterface/ProgramProperty bags and integer type tokens → ProgramResourceKind, ShaderValueType, nullable typed location/extent/block facts and ShaderStageMask. OpenGLBackend.ProgramQueries decodes query results before manager publication. ProgramFields and ProgramUnitAvailability update/read those typed facts while preserving unavailable fields and unrelated data. |
| Prepared/graphics consumers | GpuPreparedBindings, GpuProgramInterface, GraphicsExecutableInterface, ShaderInterfaceShape, ShaderResourceFormatCompatibility and dependent validation consume typed facts. Preparation receives explicit texture/image limits and admits no context or native reads. Fixed shader ownership/revision checks remain unchanged. |
| Authored types and cache identities | ShaderResourceType no longer encodes GL tokens. Explicit semantic matching replaces native ordinal casts. Shared generator/runtime declarations use the same enum; effective-contract-v2 invalidates reusable interface projections. Existing driver-binary identity still describes unchanged stage bytes/settings, and installation revalidates the interface rather than adding another cache authority. |
| Capabilities and validation | Framebuffer slot/multiview/layer checks, uniform-ring admission/alignment and binding-registry limits use the admitted RHICapabilities projection. No additional capability discovery/cache was introduced. |
| Layout reads and external assignments | GetUniformLocation/block resolution, RebuildCache, ValidateContract and active-binding projections read supplied snapshots only. Explicit TryBuild acquires unknown external facts through RHI. ApplyContract routes complete authored assignments through RHICommandContext/IRHIResourceExecution.ApplyProgramBindings; backend resolution skips known-equal values, checks original association/revision across callbacks and publishes through existing observation. OpenGLProgramQueries and layout-owned native assignment helpers, obsolete capability branches and ErrorScope are removed. |
| Texture initialization format access | Resolve validates shape/defaults and admitted global limits without format discovery. Backend ResolveTextureSamples selects from the existing target/format capability cache before allocation; the established narrow sample-count fallback remains intact. Private manager handoff stays private. |

All affected callers were migrated directly. There is no compatibility overload, old-name alias,
owner-side native codec, forwarding shim, second reflection registry or requested/actual metadata pair.
Layout address caches are cleared with snapshot rebuilding so equal native names do not retain stale
locations. Callback changes to the captured interface revision reject remaining stale assignments.

### Validation and review

Canonical Debug and ordinary Release production builds passed, as did both complete test-project
compilations with normal shader fixture generation. Final receipts are under `artifacts/phase8d/`:

- Debug affected selection: **842/842 passed**, zero skips (`debug-affected-3.log` and TRX).
- Ordinary Release combined selection: **840/842 passed**, zero skips (`release-affected.log` and TRX).
  Both FailedMipAllocationRetiresUnpublishedTexture cases missed the injected Harmony throw in that
  process. The exact two cases passed **2/2** in a fresh process on the unchanged optimized binary
  (`release-fault-isolated.log` and TRX). This retains the prior optimized observer limitation; it does
  not claim a clean combined-process Release result. No JIT overrides or instrumented product was used.
- ShaderContractGenerator.Tests: **157/157 passed** (`debug-generator-2.log` and TRX).
- ShaderBuildTool.Tests: **256/256 passed**, including contract identity/cache tests
  (`debug-shader-build-tool.log` and TRX).
- Exact affected filter: `affected-test-filter.txt`. Initial 41-case schema/isolation/preparation checks
  are in `debug-core.log`; the final broad selection includes those checks and new layout regressions.

Review resolved the semantic zero/default buffer-availability collision, retained supplied-metadata
rejection behavior, checked native/semantic decoding and assignment publication, and preserved original
context rejection. LumOn ownership fixtures now allocate the actual prepared block extents; runtime
minimum-size validation was not relaxed. The retired-context test observes both core/legacy status
queries and expects the existing originating-context exception. Source review also checked callback
revision rejection, discarded local snapshots, cached lookup invalidation and obsolete API removal.
Later label/diagnostic, manager guard/context discovery, state-purge, aggregate closure and service-name
work remains assigned to 8e–8i. Integrated package/game acceptance remains in Phase 9. No staging,
commit, game launch or performance qualification was performed.

The second implementation review and independent audit-stage-completion review found no remaining
8d blocker after correcting the proposal's stale OpenTK-valued texture-reference exception. The audit
independently reconstructed the eleven task/source mappings and retained the combined-process Release
limitation above; later 8e–8i work is not represented as completed by this receipt.

## Resource labels and native diagnostics boundary (Phase 8e)

Authority: the five Phase 8e tasks, mandatory migration receipts, proposal outcome/ownership policy,
contract private resource-execution boundary, GPU resource and binding contracts, canonical resource
proposal, inventory/API coverage, and the retained creation/publication and texture-reference proposals.
Optional labels stay outside metadata and do not certify creation success or grant ownership.

| Task | Before and final route | Verification |
| --- | --- | --- |
| Wrapper naming | Twelve `SetDebugName` implementations called native `GlDebug` selectors; each now submits its existing owner through `RHICommandContext.TryLabelResource` → `IRHIResourceExecution.LabelResource` → backend diagnostics. `Shaders/GpuProgram` names its installed program owner. Managed name storage is preserved. | Wrapper IL isolation, real buffer label/error/binding checks, owner regressions. |
| Engine labels | Engine shader/framebuffer/mesh helpers lived in frontend domains with native labels. They now live in backend diagnostics integration; hooks, GBuffer, startup and pool naming call the final helpers directly. No old-name facades remain. | Existing shader/resource label and hook-installation suites. |
| Label implementation | Native diagnostics moved beneath the backend and split by labels/groups/notifications/errors. `GL.Is*` label probes and redundant LumOn label calls are removed; original owner/context admission remains. Owner label dispatch uses OpenGL4; explicit pre-admission error reporting retains the engine binding already loaded during capability capture. | No-probe IL inspection, injected optional label failure, ordinary Debug/Release exclusion checks. |
| Native errors | `GpuProgramLayout` remains query-free and contains no `ErrorScope`. Failed engine shader recovery reports bounded errors through `OpenGLRHIContext.ReportFailedEngineCompilation`; its source restoration/retry policy is unchanged. | Source review, error-attribution and shader-recovery tests. |
| Diagnostic caller closure | Sky groups and existing cloud-volume assertions use semantic RHI diagnostic operations. Backend-specific profiling/discovery and exact engine instrumentation retain explicit backend diagnostics. No automatic error drain is added to resource operations. | Scope/state diagnostics and source dependency review. |

Final delegated validation on the corrected candidate:

- Debug: **182/182 passed**, zero skips; full test-project/production compilation **0 errors**.
- Ordinary Release: **182/182 passed**, zero skips; full test-project/production compilation **0 errors**.
- Exact filter: `artifacts/phase8e/final-filter.txt`.
- Compile logs: `debug-final-error-binding-build.log` and `release-final-error-binding-build.log`.
- Test logs/TRX: `debug-final-error-binding.log/.trx` and `release-final-error-binding.log/.trx`.
  All logs above are under `artifacts/phase8e/`; cached shader outputs were reused.
- All four new owner-label tests ran, including pending-error/binding preservation, optional native
  failure, no validity probes, wrapper isolation and ordinary Release exclusion. Existing engine
  labels/scopes, shader recovery, state diagnostics, owner and capability tests passed.

The initial canonical attempt encountered restore/network issues and then stale qualified label-helper
names, corrected before final compilation. Do not represent that initial attempt as a passing canonical
receipt. Earlier 176-case runs are superseded by the final 182-case candidate above.

Second review and independent stage audit consulted the linked authority and found no remaining 8e
blocker. Final source-order review preserved the already-loaded engine error binding for capability
capture before RHI admission loads OpenGL4; the final runs include that correction. Headless fixtures
preload both binding tables, so startup-order proof also includes source inspection. Native owner label
dispatch remains OpenGL4. Explicit error assertions retain their existing release behavior and are not
an automatic native-success protocol. Subphases 8f–8i and Phase 9 remain open. No JIT overrides,
instrumented Release, staging, commit, game launch or performance qualification was performed.


## Manager operation exclusions and retained ownership (Phase 8f)

### Authority and routes

The nine 8f tasks and mandatory migration contract govern this unit. Controlling sources are the
RHI proposal (ownership, shared operation sequence, exact integration and outcomes), contracts
(internal execution, references/factories), GPU resource and shader binding documents, canonical
resource proposal and inventory/API coverage. The archived creation/publication proposal at
`3f307365:docs/Rendering.CommandPublication.Proposal.md` supplies the shared commit/interruption
rules; the texture-reference proposal at `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md` supplies
published-only access, original dependencies and nonowning consumer retention.

| Task / contract | Before and final route | Evidence scope |
| --- | --- | --- |
| Base resource and manager ownership | Registration, weak associations, liveness, retention counts, replacement, captured queues and original-context retirement remain in GpuResource / existing manager services. No transaction, registration or availability state enters the generic snapshot base. | Lifecycle/retirement, alias/replacement and queue regressions; source review of ResourceLifecycle, WrapperAssociations and deletion drain. |
| MetadataOperation / MetadataPublication | Concrete cache and ResourceOperationGuard fields/parameters become private IRHIResourceOperation. Manager still withdraws, accumulates effects and commits canonical candidates once. RHICommandContext's CPU-only IRHIResourceOperationExecution gate is implemented by the existing cache; only Composition selects it. Token disposal releases the captured cache, not current-context state. | Explicit participation, caught interruption/reentry, failed disposal without recovery, exactly-once alias publication, original-context and deletion/name-reuse tests. |
| Manager caller closure | BufferTextureCreation, FramebufferBackings, MetadataObservation, ImageAcquisition, MetadataAcquisition, NativeObservation, TextureCommandInputs, TextureViewCreation, ProgramSnapshots and Registry use final RHI exclusion entrypoints. No manager cache/backend imports or concrete guard signatures remain. Supplied attachment facts use query-free RHI/manager projection rather than native admission; tracked unavailable facts cannot be overridden. Unknown-fact acquisition remains admitted. | Source/caller inventory plus acquisition/recovery/import and publication suites. |
| Context discovery | Physical fallback and current-resource checks now belong to existing RenderContextRegistry. CaptureCreationContext keeps resource-kind policy; RequireOriginalContext keeps owner rejection. The old static wrapper check is removed and all backend, owner and fixture callers use the registry directly. Transactions recheck owner affinity before withdrawal and participation/commit. | Registered generation/retirement and two-unregistered-physical-context tests. Local exclusions use the existing admitted native context identity; CPU registry tools do not construct a backend. |
| Boundary footprint | EngineBoundaryResources moves from frontend Integration to backend Boundaries, including native BufferRangeTarget data. Only backend BoundaryAdmission, BoundaryBindings and BoundaryEntry construct/consume production footprints. Native fixtures import that owning namespace directly. | Complete construction/consumer search and boundary regression suites. |
| Consumer refs | Texture/buffer/renderbuffer/sampler snapshots and auxiliary usability/revision access remain manager-owned published reads. They retain managed objects without native reads, recovery, mutation or deletion authority. Fixed shader refs retain exact executable identity. | Reference/dependency, unavailable/recovery, view/source replacement and retirement regressions; helper-chain review. |
| Owner helper policy | Buffer queued upload/copy/growth, TextureStreamingManager destination storage certificates, GpuSamplers retirement and shader reload generation checks remain with their owners. Upload execution already enters RHI; prepared storage alignment now uses semantic RHICapabilities and link batches consume ParallelShaderCompilation there. GpuEbo's remaining native IndexType becomes existing IndexFormat, with all fixture callers migrated. | Buffer/EBO, streaming, sampler and shader workflow tests; no duplicate capability discovery or allocation policy. |
| Hierarchy and private recipients | Reviewed all partials of GpuResource, GpuTexture and framebuffer/attachment owners; all buffer subclasses, owned/borrowed/view textures (including CloudBlueNoiseTexture), renderbuffers/samplers, VAOs, queries/fences, transform feedback, semaphores/memory objects and native executable owners, plus typed refs and helper chains. OpenGLPassTarget is a backend-created framebuffer realization owning its configuration lease, not attachment storage. CreateGeometryVertexArray is a private backend recipient handoff. EngineGeometryInputs' private recipients are nonowning exact engine-import admission; ArrayReader proves existing engine realization without native queries and buffer discovery goes through the manager's RHI-backed reader. | Caller/ownership inspection, not directory or internal-visibility exemptions. Stale wrapper/native imports removed. Native capability discovery, binary-cache opaque driver identity and explicit profiling/interception stay at their existing intentional boundaries; broad reset/propagation corrections remain 8g. |
| Direct migration / reconciliation | No old namespace alias, wrapper context-check forwarder, new registry/cache/dispatcher or compatibility facade. Existing backend guard object implements the exclusion contract directly; native backend callers retain the same guard authority. | Final source/diff review, ordinary builds and independent audit; final receipts below. |

### Validation and review

The initial build required the installed pinned shader-compiler cache after the sandbox profile could
not provide it. Cold production/test shader catalogs were rebuilt through the normal repository tooling.
Compilation exposed GpuEbo's remaining native enum and one test enum ambiguity; both were corrected.
The first focused run exposed three CPU-only supplied-image projections incorrectly requiring native
admission; their unchanged tests now exercise the corrected pure supplied-fact route. Expanded lifecycle
coverage then exposed a fixture conflating retained wrapper association with raw numeric selector
scopes, reading retired metadata before selector entry and retaining an old unasserted sampler rejection
expectation. The fixture now checks typed reference/RHI rejection and raw selector-scope restoration
separately, retaining owner-mutation rejection, native-storage and replacement-entry oracles. Raw backend
scopes select the current numeric identity; they cannot infer which retired wrapper a caller once held.
Runtime exception policy was not relaxed. Those preliminary failing receipts do not qualify the final candidate.

Final delegated validation on the corrected candidate:

- Debug: **545/545 passed**, zero skips; full test-project/production build **0 errors**.
- Ordinary Release: **545/545 passed**, zero skips; full test-project/production build **0 errors**.
- Exact selection: `artifacts/phase8f/focused-filter.txt`.
- Final build logs: `debug-final-lifecycle-build-2.log`, `release-final-lifecycle-build-2.log`.
- Final behavior logs/TRX: `debug-qualified-2.log/.trx`, `release-qualified-2.log/.trx`.
  All named receipts are under `artifacts/phase8f/`; earlier failing runs are superseded, not omitted.

Second implementation review and independent audit-stage-completion review directly consulted the
nine-task contract, parent design task and controlling current/archived sources. No required finding
or evidence gap remains. Registered/local publication, interruption, original identity, exactly-once
completion, context retirement and physical local-context isolation passed. The final expanded selection
also covers supplied-only CPU projections, framebuffer discovery/multiview, wrapper/ref lifetime,
EBO, streaming and shader workflows. No production behavior was changed to satisfy obsolete raw selector
expectations. No native optional-feature or live rendering qualification is inferred from these focused
receipts; integrated/package/user evidence remains Phase 9. State propagation/purge, aggregate closure
and service naming remain open in 8g–8i. No JIT override, instrumented Release, benchmark, game launch,
staging or commit was performed. Existing user staging was preserved.


## Persistent intercepted knowledge (Phase 8g)

### Authority and implemented subset

The revised twelve-task 8g contract closes independent state-knowledge cleanup under the mandatory
migration rules and retained resource/binding authority. The archived creation/publication and
texture-reference contracts retain manager publication, original identity and published-only access.
The user assigned native interception and every remaining shared observer correction to the immediately
following 8h, under the approved Rendering.OpenTKBindingInterception.Proposal.md. Its all-supported-caller,
always-forward and completed-handler requirements remain mandatory, but are not 8g completion claims.
No unknown-runtime-mutation compatibility mode, temporary shim or routine-purge substitute is introduced.

| Tasks | Source closure / current disposition | Required evidence |
| --- | --- | --- |
| Frame and resize | GpuUniformRingBeginRenderer already advances only controller.BeginFrame; ScreenResourceManager already invokes ordered callbacks without purging. Removed the unused backend BeginFrame/PurgeCache APIs. Uniform allocation/publication epochs are unchanged. | InterceptedKnowledgePropagationTests, uniform ring and pixel-layout regressions. |
| Startup | StartClientSide registers the current engine context before shader-library/resource use. The cache starts unknown and SelectRHIContext resets knowledge on native/lifetime changes; removed the duplicate caller-owned InvalidateAll. Early EngineStateCalls.CurrentCache context registration remains. | Source order plus context-admission regressions; live startup is not qualified. |
| Framebuffer wrapper publication | OnDetached only releases wrapper references/events; OnBeforeDelete no longer duplicates routing invalidation. Existing RHI publication keeps still-live routes. Fresh backend CreateFramebuffers establishes the specification's initial ColorAttachment0 route; observed deletion removes only its original route. ReadWindowSurface no longer invalidates draw routing merely to inspect surface facts. | Routing, wrapper lifecycle, native name reuse and boundary suites. |
| Pixel-store effects | Exact int/float overloads execute once. Legal alignment/stride/skip/boolean effects update independent known fields in the existing cache. Floats use boolean conversion before nearest-integer conversion; ambiguous ties/out-of-range conversion and thrown/foreign-context return withdraw only the affected field. Defined invalid values preserve prior state and native errors. GetPack/GetUnpack query only initially missing or genuinely uncertain fields. | New direct driver oracle, rejected argument/error and partial-initial-knowledge tests; no new counters. |
| Object-local draw routing | Core/Arb/Ext scalar, array, ref and pointer overloads retain exact dispatch. Established targets publish validated complete routes, including omitted None outputs. Invalid selectors/duplicates preserve prior routes; unknown named targets and window-buffer availability retain acquisition. Token commits recheck original context. Read routes have no mirrored state. | Routing propagation/suppression, exact forwarding, capture/restore and errors. |
| Fixed state | Exact EngineStateCalls overloads now repopulate represented depth/stencil, rasterizer, dynamic viewport/scissor/blend constants, primitive assembly, blending/output and sampling fields. Defined rejection preserves prior facts; undefined conversions, interruption and foreign completion retain affected-field uncertainty. Existing masks remain the single authority. Managed viewport-zero effects use indexed native entrypoints so unrelated viewport indices survive boundary execution/restoration. | Driver state/error oracles, suppression captures, boundary coverage and viewport-one sentinel regression. |
| Established bindings | Generic/indexed UBO/SSBO/atomic buffer selectors, generic aliases, selected-VAO element association, VAO selection, active texture unit, texture/sampler/renderbuffer/image selectors and framebuffer selection propagate where existing identity/metadata/limit facts prove admission. Unknown realization remains acquisition rather than a guessed success. Selector-only invalidation preserves per-object EBO/routes. | Binding aliases, initial acquisition, rejected inputs, context identity and preserved object-local knowledge. |
| Canonical resource effects | Known base/max-level parameters publish directly. Eligible ordinary array attachment selections and buffer-texture backing/ranges publish supplied facts through existing manager entries. Proven rejection retains prior descriptors without renewing retired backing associations. Unknown/ambiguous command outcomes retain targeted readers. | Metadata/dependency, rejected association, legal out-of-depth layer and exactly-once publication suites. |
| Context completion | Captured physical context and existing cache ownership gate success/failure cleanup. Original callbacks cannot clear another admitted namespace's knowledge; program-selector uncertainty cleanup follows the same ownership rule. Legacy texture, sampler, renderbuffer, VAO, pipeline and framebuffer scopes now retain original physical identity and skip foreign restoration. Query-buffer failure cleanup also checks cache ownership. | Foreign-context lifecycle and failure tests; no second context cache. |
| Remaining helper purges | Removed successful EngineFullscreenGeometry admission's unconditional selector purge; failed-upload withdrawal remains because dispatch/certification may have been interrupted. Removed unused InvalidateTextureUnit and InvalidateUniformRange APIs, which had no production or test callers. | Engine geometry, binding and failure suites. |

### Interception evidence and explicit 8h handoff

Complete interception is not yet implemented. EngineStateTargets.Assemblies selects exactly
VintagestoryLib, VSEssentials, VSSurvivalMod and VSCreativeMod. Its metadata recognizer accepts only
OpenTK.Graphics.OpenGL.GL; CallsSupportedOperation recognizes direct call operands, not delegate
construction or arbitrary dynamic code. Existing generic discovery closes observed built-in references,
not every future instantiation. Extending the assembly list alone cannot satisfy the contract.

Independent inspection of installed G:/Vintagestory/Lib/OpenTK.Graphics.dll established that ordinary
DepthMask/PixelStore setters and generic BufferData overloads each load a function pointer from their
own GL.EntryPoints slot and issue calli directly. Generic overloads pin data in their own method body;
there is no common managed dispatcher. Both legacy and OpenGL4 expose LoadBindings and independent
entry-point tables; the headless fixture reloads both tables between owning threads. Inspected methods
have IL implementation flags without NoInlining. Patching non-generic binding wrappers alone therefore
does not establish generic, delegate, late-loaded or already-inlined coverage.

A native entry-point interception implementation would additionally have to prove ABI widths/calling
convention, original pointer/table lifetime and reload behavior, reentry/exactly-once publication,
backend-owned native dispatch and exception containment. Existing manager/immutable/boundary observers
can throw managed exceptions; installing them as unmanaged callbacks without that proof is unsafe.
No speculative native thunks, broader-but-still-incomplete caller filter or temporary interception
facade was installed. The supported-table caller-coverage requirement is assigned to 8h rather than weakened.

### Remaining uncertainty and deferred dependencies

The handler inventory distinguishes implemented propagation from work still needed:

- Fixed-state handlers withdraw only their affected masks while native dispatch is in progress,
  then commit legal defined effects. This temporary withdrawal prevents callbacks from consuming stale
  pre-command state; it no longer implies routine post-command readback. Ambiguous float conversion,
  nonfinite outcomes and interrupted dispatch remain explicitly unknown.
- Established binding aliases and object-local routes reuse existing facts. New/unrealized identities,
  unknown incoming selectors and unavailable initial surface facts retain initial acquisition. Identity
  proof is not inferred merely from a nonthrowing native return.
- NativeVertexMetadata still invalidates layout/configuration revision certificates. The canonical VAO
  schema intentionally contains no full layout mirror; acquiring previously unrepresented configuration
  for certification is distinct from discarding a known binding.
- Texture allocation/mip generation, unrepresented texture-parameter results, unknown framebuffer
  attachment outcomes, ambiguous attachment modes and initial buffer-texture association retain their
  resource-specific readers. Direct propagation now covers represented mip parameters, eligible ordinary
  array selections and established buffer-texture ranges; it does not claim every native configuration
  family is query-free. Allocation failure and driver-derived facts keep the approved outcome policy.
- Successful program/pipeline selectors and transform-feedback activity still lack a complete observed
  acceptance model. Begin/End/Pause/Resume transform-feedback coverage and the program/pipeline/active
  uniform destination variants are explicit 8h implementation tasks. Current verification and selector
  uncertainty remain until that replacement; they are not certified as the final observation design.
- ContextIdentity's full reset is tied to actual native/lifetime identity changes. BoundaryRestoration's
  withdrawal is tied to failed cleanup. Pixel layout scope failure and foreign-context cleanup remain
  targeted, and cannot purge a different admitted namespace.
- ResourceDeletion.Publication remains the single observed per-kind retirement authority. Its outcome
  checks distinguish deferred shader/program deletion and rejected native deletion from actual retirement;
  wrapper detach does not imply deletion. Registry retirement and content/readiness invalidations are
  separate concerns. Complete caller interception must still be qualified before claiming every external
  deletion reaches that authority.

The revised 8g completion audit covers only the independent cleanup above. Native interception is 8h,
combined corrective qualification is now 8i, and management-service naming is now 8j. Their checkboxes
remain open. The evidence below does not qualify their unimplemented behavior.

### Validation of independent cleanup

The user deferred interception-gap discussion while the independent cleanup proceeded. Final delegated
validation of the corrected candidate passed **405/405 tests in Debug and 405/405 in ordinary Release**,
with zero failures or skips. Both canonical shader-enabled builds passed with zero errors. Existing
compiler/analyzer/package warnings remain (536 Debug, 543 Release). Exact selection and receipts are
`artifacts/phase8g/current-filter.txt`, `debug-closure-build.log`, `release-closure-build.log`,
`debug-closure.log/.trx` and `release-closure.log/.trx`.

The selection covers pixel-store conversion/partial knowledge, routing, fixed-state suppression,
known binding aliases, frame/resize epochs, startup generations, metadata/dependency publication,
retirement/name reuse, boundary restoration, viewport-one preservation, auxiliary/query behavior and
physical-context scope disposal. The new six-scope regression proves disposing old texture, sampler,
renderbuffer, VAO, pipeline and framebuffer scopes preserves another admitted context's cached and
actual native selectors. Query-buffer foreign/failure cleanup was source-reviewed and existing query
regressions passed; no additional diagnostic counter was introduced. Original-context restoration
re-admits its namespace before cache comparisons, and actual context changes abandon old tracker stacks.

Preliminary receipts retain test compilation/oracle corrections and the real indexed-range/startup
findings; earlier passing runs predate the final scope correction and do not qualify that correction.
Final builds were checked to contain both the startup capability refresh and all three context-change
tracker resets, avoiding a compilation race with the last source edits.

The original independent review qualified the implemented cleanup while wider interception remained
open. The user subsequently moved native interception, non-vetoing observer guards, remaining handler
propagation and universal supported-table coverage to 8h. Completion of the revised 8g boundary requires
a fresh contract review; earlier green tests do not close 8h. No staging, commit, game launch, JIT override,
instrumented Release, benchmark or live appearance/performance qualification was performed.

### Revised completion boundary and current-source evidence

The approved split retains cleanup in 8g and implements the native interception replacement in 8h.
That next unit owns caller-discovery removal, both binding tables, ABI/loader/context/callback lifetime,
saved-pointer backend dispatch, always-forward observer-failure behavior, external guard conversion,
program/pipeline selectors, transform-feedback activity and remaining handler qualification. Its tests
must prove external calls cannot be vetoed, cancelled, retried or made to throw by the observer.

Second review of the revised cleanup contract retains the source mappings above and the 405-test
Debug/Release receipts. An independently built portable-PDB probe compared current authored sources
against both configurations: 2,303 production and 918 test source checksums matched, PE/PDB identities
matched, and test-output binaries predated the corresponding 405-pass TRX receipts. No source mismatch
was found. This supports reusing the existing validation instead of rebuilding unchanged code.
Exact identity evidence: `artifacts/phase8g/source-identity/results.json`; probe source/build receipt
is retained alongside it. Full native interception remains unimplemented and unchecked in 8h.

Final independent audit against the user-approved split passed all twelve revised 8g requirements.
The implementation, retained behavioral receipts, source-identity proof, second review and linked-document
reconciliation support completing 8g. Its markers are complete. The fifteen native-interception tasks
in 8h, combined qualification in 8i, service naming in 8j and integrated acceptance in Phase 9 remain
unchecked. No additional runtime change or repeated full build was required for this closure. No staging
or commit was performed.


## Native interception replacement: 8h completion receipt

The final source review, contract audit and combined affected-test qualification are complete.
The following task numbers refer to the ordered items under 8h in
[the implementation plan](Rendering.GpuCommands.todo#phase-8h-replace-caller-rewriting-with-native-opentk-binding-interception).
They preserve requirement-to-document-to-evidence traceability without treating earlier green tests as
proof of broader closure.

| Tasks | Controlling documentation and requirements | Current implementation and verification evidence |
| --- | --- | --- |
| H01, H16 | [API coverage](Rendering.GpuResourceHandles.ApiCoverage.md#authority-and-expansion-rules), [canonical inventory](Rendering.GpuResourceHandles.Inventory.md#native-coverage-and-consumer-gates), resource proposal and archived command-publication proposal (`3f307365`): supported desktop families, represented effects, scoped queries and exclusions | `artifacts/phase8h/native-coverage.json` maps 308 selected symbols to cache-owned source declarations and reconciles expanded unselected names. Its registry URL/SHA identifies `gl.xml`; no unresolved name disposition. ES-only EXT pipeline spellings are excluded; next-link inputs, content-only operations and unrepresented read-routing fields retain explicit dispositions. Creation registration and maintained lifecycle facts are separately assigned to 8i, without claiming those facts already exist. |
| H02–H07 | [native dispatch/generation design](Rendering.OpenTKBindingInterception.Proposal.md#generated-callbacks-and-handwritten-observers), [RHI ownership](Rendering.GpuCommands.Contracts.md#authority-and-ownership): exact PE-derived ABI, compiler diagnostics, direct post-call delivery, no paired receipts or duplicate dispatcher | `OpenGLInterceptionGenerator/Bindings`, `CallbackEmitter`, `DriverCallEmitter` and cache-owned `State/Observation/Native` attributes. Generator tests cover invalid declarations/layouts, native widths/output/results, aliases and exact managed overload selection; 18/18 in both configurations (`closure2-generator-*`). All 169 immediate pairs, ten receipt types and nested binding/VAO receipt removed. Controlled vertex configuration/program assignment own native work and cleanup synchronously. |
| H08–H11, H14, H18 | [installation/lifetime](Rendering.OpenTKBindingInterception.Proposal.md#installation-and-lifetime), [failure policy](Rendering.OpenTKBindingInterception.Proposal.md#observation-and-failure-policy), RHI contracts: both tables, retained originals/callbacks, unchanged once-only forwarding, no callback admission veto or error drain | `OpenGLBindingInterception`, `OpenGLBindingLoaderHook`, `OpenGLBindingTable`, `OpenGLNativeSlot`, `OpenGLObservationContext`; generated `OpenGLDriverCalls`. `OpenTKBindingInterceptionTests` covers warm/delegate/emitted callers, both roots, uninstall, late replacement, partial loader failure, loader/uninstall overlap, retained fetched callbacks and uninitialized context/native errors. Generator native tests prove return/output preservation with failing/disabled observation. Backend dispatch uses saved originals with no broad suppression flag. |
| H12–H13, H15, H19 | [resource publication](Rendering.GpuResources.md), [prepared bindings](Rendering.ShaderBindingState.md#prepared-resource-submission), archived command-publication proposal (`3f307365`), unchanged texture reference proposal (`b9fca04c`): one manager/cache, targeted uncertainty, original associations, selectors/activity and non-vetoing external guards | Native observations interrupt affected manager operations only after native dispatch. Program selector/uniform/bindless, buffer/image, deletion and reentry regressions use real native slots rather than Harmony patches on generated bypass methods. `StaticGpuFramebufferTests.ExternalRoutingSupersedesImmutableDeclarationProof` distinguishes immutable managed policy from actual executed routing. `OpenTKFeedbackObservationTests` proves accepted/rejected activity transitions, native errors, binding preservation and removal of redundant recapture. Existing metadata failure/recipient notification tests require contained observation failures while retaining canonical availability/recovery assertions. |
| H17 | [mandatory migration contract](Rendering.GpuCommands.todo#mandatory-migration-contract--applies-to-every-implementation-phase), [source ownership](Rendering.GpuCommands.Contracts.md#steady-state-source-ownership): full caller/dependency removal with no shims | Caller scanner/maps, generic closure machinery, exact metadata adapter catalog and handwritten native-dispatch overload inventory removed. Source search finds no old observation factories/receipts; retained shader-reload lifecycle begin/complete methods bracket real work and are not native observation receipts. Broader shader/render integration hooks retain their separate semantic purpose. |
| H20–H21 | RHI proposal verification/migration rules, parent `Rendering.GpuResourceHandles.todo` design authority, task-linked documents above, completion-audit skill: current documentation, canonical builds, second review and independent completion audit | Final canonical shader-enabled Debug/ordinary Release builds pass; the combined selection passes 662/662 in each configuration, zero skips (`closure7-*`, exact selection `closure7-filter.txt`). Generator tests remain 18/18 in both configurations; their sources predate those receipts and are unchanged. Second source review and the separate requirement-driven completion audit pass for the native interception unit. Earlier full-suite failures remain diagnostic/integration evidence as detailed below; no JIT override, performance gate, package or live-game qualification is claimed. |

The native test fixture captures real loader pointers and reports test-observer faults at the managed
test boundary; it does not alter production callback error policy. Optional bindless opaque-handle
substitutes never submit fabricated handles to the hardware. Native hardware behavior, generated ABI
mapping and static engine-call inventories remain distinct evidence scopes. No installed-game startup,
visual acceptance, other-platform driver qualification, staging or commit is claimed.


### Historical baseline provenance

The resource inventory's earlier static caller inspection and pre-manager measurements remain archived
at `9de4c4ca:docs/Rendering.GpuResourceHandles.Inventory.md`, under "Native coverage and consumer gates"
and "Reproducible baseline". They describe the source baseline `35e1481c9d6f94ef8de4fdded5499bccf6a11867`,
not this implementation. Their local `artifacts/gpu-resource-handles-baseline` files are unavailable in
this checkout and are not used to certify native interception, current test/package identities or
performance. The current family matrix and compiler-derived slot inventory control coverage; current
validation receipts above qualify only their stated scope. Historical source/caller observations do
not create a requirement to restore the old scanner, replay fixture or benchmark gate.


### Second-review corrections and current limits

Source review found that `ObserveDeleteObjectARB` classified an object from `IsShader` after native
deletion. Immediate shader destruction erased that type evidence and could leave its original shader
registration live. Classification now prefers the existing manager registration's kind; no new registry
or pre-call hook is introduced. `NativeShaderDeletionTests` covers both immediate and attachment-deferred
destruction, original association retirement, wrapper unavailability and the native error queue.

Boundary-entry tests now distinguish facts already published by intercepted native setters from fields
made explicitly unknown for acquisition tests. External native calls execute even outside a managed
boundary's declared footprint; managed transitions still enforce that footprint. The failing entry
assertion that leaked a boundary scope now releases unexpected admitted scopes before reporting failure.
These changes restore the intended test contract rather than permitting stale cache knowledge or
reintroducing external vetoes.

The authoritative pre-interception HEAD was built independently under `artifacts/phase8h/head-9de4c4ca`;
`head-isolated-*` and `candidate-isolated-*` compare exact failing classes in fresh test processes.
`ShaderTextureTargetTests` and six `AmbientOcclusionCompositeTests` fail identically on that baseline.
`WorldProbeTraceRoutingTests` passes 11/11 in both isolated candidates, so its earlier full-run failure
is not proven to be an interception regression. Full-suite receipts are diagnostic until the remaining
failures are classified; the focused qualification does not certify unrelated rendering algorithms.


### Remaining acquisition and uncertainty inventory

This records current reader decisions under the retained publication/query policy. It does not claim
the complete creation/lifecycle maintenance scheduled in 8i. Native observers reconcile already executed
external calls; controlled RHI commands retain their supplied/default/derived publication route.

| Observation owner | Established effects and remaining acquisition reason |
| --- | --- |
| `ObserveBufferStorage` | Interrupts the original managed operation and withdraws an existing storage snapshot without reading it. A returned external allocation does not establish its per-object outcome; the next explicit consumer recovers unknown capacity/flags through the existing manager reader. No query is added to controlled buffer allocation. |
| `ObserveTextureMetadata` | Known legal base/max parameters publish arguments; proven rejection retains prior values. Other represented parameter conversions read only that field. Known single-image definitions recover only the selected level/face; unknown descriptors retain initial acquisition. Immutable mip generation is content-only; mutable generation inspects only potentially changed descendants when prior images are available. Storage allocation, view selection and optional renderbuffer association retain outcome/dependency acquisition. |
| `ObserveBufferTexture` / `TryObserveBufferAssociation` | Established destination/backing identity, legal format, capacity and alignment prove allocation-free association/range effects with no query. Otherwise read backing/format and only exactly representable range fields; native-width command facts are never narrowed to GLint. Original backing association renewal remains manager-owned, with a reader only for unknown backing facts. Internal-format query results are decoded to semantic `TextureFormat` in `OpenGLBackend.ImageQueries` before comparison. |
| `ObserveRenderbufferStorage` | Recovers the externally allocated descriptor because native allocation/coverage/sample outcome is not proven by return. Controlled allocation retains supplied dimensions/format plus the approved sample-count/resolved-format exceptions rather than full readback. |
| `ObserveFramebufferMutation` / `PublishObservedFramebuffer` | Known eligible ordinary attachment selections publish supplied arguments; proven rejection preserves the descriptor. Other attachment/default mutations recover only the affected slot/default when the rest is available. Unknown descriptors and ambiguous optional modes retain manager discovery; routing is separate executed knowledge, not a duplicated descriptor. |
| `ObserveProgramMetadata` | Existing represented unit/block assignments use narrow field refresh; unrepresented numeric uniform writes do not refresh metadata. Link/binary completion recovers required executable/interface outputs. Unsupported opaque-handle unit representation is withdrawn, not converted to a guessed integer unit. Next-link inputs do not create an immediate reflected executable snapshot. |
| `ObserveProgramSelection`, `ObserveVertexSelection`, buffer/texture/sampler/framebuffer binding observations | Reuse canonical identity and known selectors for established acceptance/rejection; uncertain identities/selectors remain unknown for explicit acquisition. Program stages, pipeline selection and active uniform destination stay distinct. No registry entry or successful return is treated as proof that a native object is selected. |
| `ObserveFeedbackActivity` | Begin depends on unrepresented output configuration; Resume depends on its retained executable. Read active/paused outcome only where history cannot prove it. Known valid Pause/End and known rejected transitions reuse observed activity; tests count the remaining narrow reads. |
| Native deletion observations / `CompleteResourceDeletion` | Kind-specific native outcome distinguishes rejection, actual retirement and shader/program deferred destruction without draining errors. ARB generic deletion uses the original registered kind before post-deletion native evidence. Duplicate/zero names and original allocation associations retain the single retirement authority. |
| Native VAO configuration observations | Invalidate the existing configuration revision and dependent helper certificates, retaining selected-array binding knowledge separately. The canonical VAO schema remains empty; explicitly requested unrepresented layout facts use manager query services rather than a second full layout mirror. Empty sampler/query schemas likewise remain unchanged. |

Initial pre-installation state, absent creation registration and unknown external resource descriptors are
separate from a valid supplied controlled snapshot. The new creation-tracking unit must replace avoidable
unknowns at their lifecycle source; it is not permission to add unconditional verification to these paths.


### Linked semantic-reference consultation

The API matrix's controlling Khronos references were consulted alongside the current code: immutable
storage and errors in ARB_buffer_storage; EXT_external_buffer and EXT_external_objects allocation/
import semantics; attachment defaults in ARB_framebuffer_no_attachments; OVR_multiview attachment
query fields; EXT_direct_state_access command definitions; ARB_direct_state_access issue 41 for routing
query restrictions; ARB_texture_buffer_range association, detach and rejection rules; and
NV_explicit_multisample's renderbuffer-texture target/query restrictions. The latter defines an allowed
EXT command's target semantics, without adding NV-owner interception. OpenGL 3.1 manual mip generation,
OpenGL 4.3 vertex binding, and OpenGL 4.6 sections 8.9/8.18 govern the retained selected-image/range/view
interpretation. ARB_bindless_texture governs opaque handle versus integer-unit representation. Links
and exact section purposes remain in the API matrix; no caller inventory overrides these definitions.

The binding contract's nested `GpuProgram.md` shared-workflow/reload rules and `PBR.Clouds.md` borrowed
map contract were also checked: retained executable/interface identity, producer readiness, ownership
and content certificates remain separate from native state observation. This unit changes neither
shader algorithms nor those producer/lifecycle contracts.


### Broad-suite comparison and affected fixture closure

The corrected full Debug run (`closure5-full-debug.*`) completed 8,274 cases: 7,725 passed, 500 failed,
49 skipped. Its native boundary-entry/restoration cases pass, eliminating the earlier leaked-scope
cascade. An independently built pre-interception HEAD (`9de4c4ca`) then ran the exact failing-method
selection: 477 of the candidate failures reproduce; 528 cases pass and none skip. Exact case names,
messages and outcomes are retained in `closure5-head-comparison.json` and the compared TRX files.
This is failure provenance, not a green full-suite qualification.

All 23 remaining candidate-only cases were isolated by class (`closure6-isolated-*`): 133 of 137 tests
passed before the final fixture corrections. Controlled texture initialization now injects interruption
after the second actual native image command and relies on existing managed completion checks for
failure/retirement. Foundation transaction fixtures use saved-original dispatch for their explicitly
owned work; a separate real external storage test proves native forwarding interrupts an overlapping
owner's stale completion. Both corrected fixture groups pass their isolated qualification.

The four reproducible residual cases had obsolete test assumptions: external binds contribute to actual
native-call counters; observation does not perform capability discovery; external calls have no managed
wrong-thread veto; and backend native effects bypass OpenTK wrapper instrumentation. Their fixtures
now use the native dispatch boundary or explicit managed-admission checks. PSO tracing preserves
per-operation markers, actual native state fingerprints and independent pixel assertions, including
scalar and indexed native setters; it no longer silently misses saved-original work.

The other fourteen initially candidate-only cases pass in isolated processes: camera exposure, particle
capture/handback, installed depth, sampler-failure cleanup, worker framebuffer disposal and local
exposure. Their full-sequence behavior is not certified by isolation alone. All of these classes now
also pass together with the focused native-interception selection in Debug and ordinary Release:
662/662 per configuration, zero skips. This qualifies the combined operation-family scope, rather than
claiming the full integrated suite is green. The earlier sequence-dependent observations remain explicit
inputs to the later integrated rendering gate.


### Completion audit and retained acceptance boundary

A second source review checked generated ABI dispatch, loader/uninstall ownership, query-free context
selection, cache-owned effects, synchronous controlled execution, canonical publication, original
retirement identity and the migrated test oracles. The five former shared receipt callers are now
`OpenGLBufferBindings.BindBuffer`, `OpenGLBackend.ResolveGeometryVertexArray`, and
`OpenGLProgramAssignments.SetUnit`, `UniformBlockBinding` and `ShaderStorageBlockBinding`; their work
uses the synchronous vertex/program execution owners. No old receipt/factory, caller scanner/catalog,
handwritten bypass inventory, broad suppression flag or callback enter/exit counter remains.

The separate completion audit reapplied `audit-stage-completion` against H01–H21 and their inherited
proposal/contracts/resource/binding authority, including archived controlling proposals and nested
semantic references. The registry inspection input has the exact earlier pinned SHA256
`B9CA2CFA5C676E901C20D34AF3407F1687CDE0F1336A5FF7A8974D04C7494AD3`; missing historical measurement/caller
artifacts are not used as current proof. The current family matrix and regenerated native-symbol
inventory preserve the supported obligations and explicit dispositions. Document links resolve;
source review found only the intentional root install/uninstall calls outside the backend.

Final delegated builds use canonical shader targets, ordinary Debug/Release and no JIT overrides.
`closure7-focused-debug.trx` and `closure7-focused-release.trx` each pass all 662 selected tests with no
skips. This includes the complete focused family selection and all thirteen classes containing the
previously unmatched cases, in one process per configuration. The generator's unchanged code retains
its 18/18 Debug/Release qualification. No required 8h implementation, cleanup, documentation or focused
verification item remains failed or unverified.

The full-suite comparison has a different scope: a full candidate run was compared with an archived
HEAD failing-method selection, not an identically ordered full HEAD run. Its 477 reproduced failures
are confirmed inherited; the other fourteen sequence-sensitive cases are not attributed to a native
interception regression merely because the filtered baseline and isolated/combined selections pass.
All fourteen pass the final combined selection, but the earlier complete-suite sequence remains
unqualified. Phase 9 explicitly retains that integration/order-dependence investigation and all
inherited failures without changing their independent output oracles or tolerances. The 8h gate asks
for canonical builds and focused replacement regressions; this receipt does not close that later gate.

Creation registration/default/configuration maintenance remains the explicitly approved 8i redesign;
combined dependency/schema closure and naming remain 8j/8k. Other-platform/hardware variants, optional
external-memory and bindless native execution where unavailable, installed-game startup, package
identity and live appearance remain outside these headless focused receipts. Generated mapping,
typed test-driver substitutes and real hardware execution are distinct evidence. No staging or commit
was performed for this closure; pre-existing staged changes are preserved.


## Native scalar delivery and relevance corrective unit (8h.1)

Task/source trace: effect classification and delivery follow the interception proposal's generated-callback,
installation and failure contracts, the RHI contracts' authority rules and OpenGL 4.6 core E.2.1/7.6
plus EXT_gpu_shader4. Canonical publication follows GpuResources and ShaderBindingState's supplied
snapshot/revision contracts; archived command-publication ownership remains unchanged.

- Classification/shared delivery: 29 scalar setters declare affected masks, with handwritten native rules
  and assignments. Generated state delivery captures/selects the origin, classifies before withdrawal,
  and performs targeted contained recovery. Indexed/dynamic-selector handlers retain domain-specific
  recovery and explicit absence control flow. LineWidth uses captured forward-compatibility; patch counts
  and viewport limits remain uncertain until the corresponding capability snapshot exists.
- Relevance: unsigned EXT scalar/vector observers are removed; core/named/other numeric spellings stay
  content-only. Signed unit/block and opaque-handle routes remain. Program selector relevance precedes
  interruption/refresh; texture immutable mip content and known rejected target operations do likewise.
  Invalid framebuffer attachment selectors are rejected before certification interruption. A not-yet-acquired
  program interface remains uncertain rather than being classified as numeric; its existing completion
  exclusion still prevents stale publication. A vector count cannot turn a numeric starting location
  into a represented unit assignment.
- Context: TryGet and engine-window TryRegisterCurrent use ordinary absence results without capability
  discovery or error reads. Capability-independent scalar publication needs no unrelated limits.
- Inventory: artifacts/phase8h.1/native-coverage.json names 306 selected symbols and updated dispositions;
  historical 8h evidence is retained unchanged. No runtime dispatch/lifetime owner or secondary metadata
  registry was added. Loader/uninstall/rooting and saved-original backend dispatch remain unchanged.

Verification: delegated canonical shader-enabled Debug and ordinary Release builds passed with zero
errors (205/211 warnings). Both focused native selections passed 666/666, zero skipped, using the saved
`artifacts/phase8h.1/focused-filter.txt` selection. Generator diagnostics, compilation and native forwarding
passed 26/26 in each configuration, including classification/recovery and classifier-triggered retirement.
Receipts are `artifacts/phase8h.1/closure-build-*.log`, `closure-focused-*.log/.trx` and the retained
`review-generator-debug.*` / `final-generator-release.*` logs. Exact commands and the filter reference are retained in `artifacts/phase8h.1/commands.txt`. No shaders were disabled and no JIT overrides or game launch were used.

The new native regressions exercise rejected/accepted line and patch changes on both GL roots,
no-capability independent depth publication, native error retention, ordinary signed/unsigned/named/EXT
uniform content, invalid/ignored locations and active program certification. Numeric writes keep revisions
and reader counts stable; represented unit mutation rejects a stale outer candidate. Existing context,
alias, loader/uninstall, fault recovery, native output, suppression and restoration cases remain in the
combined selection. An order-sensitive buffer failure test now injects zero-name driver output through
the native loader fixture, preserving its no-owner/diagnostics oracle independently of managed inlining.

Second source review and the separate requirement-driven audit-stage-completion pass found no remaining
8h.1 requirement gaps. Source inspection confirms one effect/publication owner, no obsolete CurrentCache
or unsigned numeric metadata observer, no added native query/veto/retry/receipt/lifetime counter, and no
migration shim. The checked contract maps above to the generated delivery/classifiers, original context
checks, program relevance ordering and native behavioral receipts. This qualifies the bounded corrective
unit; 8i's creation/default/query lifecycle redesign and later combined/package/live gates remain open.
No source staging, unstaging or commit was performed.


## Domain-owned withdrawal methods

Approved follow-up replaces knowledge-category properties on InterceptOpenGLAttribute with a required
Withdraw method reference for generated scalar state delivery. All 29 scalar observers now name
handwritten static void methods receiving OpenGlStateCache plus the exact native arguments. The cache
domain chooses affected facts; the generator resolves/signature-checks the method and emits direct calls
for normal withdrawal and contained recovery. Classifiers remain optional, compiler-checked domain rules.
There is no runtime lookup, delegate allocation, cache-category whitelist or alternate old-field route.

Generator regressions cover missing/incompatible withdrawal methods, native argument delivery,
withdrawal faults, known rejection, uncertainty and context retirement. Delegated canonical shader-enabled
Debug/ordinary Release builds passed with zero errors (224/231 warnings). Generator tests passed 34/34
and the focused native suite passed 666/666 in each configuration, with zero skips. Logs, native TRX
results and exact commands are retained under artifacts/withdraw-methods/. Source review confirmed that
all 29 declarations use domain withdrawal methods, obsolete attribute fields and generator category
mapping are removed, and original dispatch/context/recovery semantics are retained. No staging or commit.

## Corrective dependency closure (8j)

Authority: the approved RHI proposal and contracts (ownership, internal execution, semantic descriptors,
shader/PSO generations, outcomes and migration), resource/binding contracts and the corrective tasks
8a–8i including 8h.1. Their original task/source/receipt tables above remain the before-route inventory.
The completed lifecycle design, plan and inventory are retained at
`e013054f:docs/Rendering.MetadataLifecycle.{Proposal.md,todo,Inventory.md}`; current lifecycle/query
contracts live in Rendering.GpuResources.md and the resource inventory/API matrix. The removed live
planning documents are not build inputs or active linked prerequisites.

The combined caller review checks the complete routes below, including named helper/partial occurrences,
not merely namespaces or public signatures. RHI methods admit execution; the same manager commits
canonical facts; the same cache owns execution knowledge. Direct published-fact/lifetime collaboration
with the manager remains intentional and never becomes another execution or acquisition owner.

| Findings / before route | Final admitted route and dependency closure |
| --- | --- |
| GpuFramebuffer.SetAttachmentCore, RemoveAttachmentCore, ResizeCore, Bind; StaticGpuFramebuffer configurationOwner/RegisterImmutableFramebuffer/ReleaseConfiguration/OnContextRetiring | Owner policy calls RHI RequireMutableFramebufferOwner, attachment/configuration/select operations and SealFramebufferConfiguration. The backend's existing configuration lease owns immutable admission and context retirement; backing guards remain owner policy. |
| GpuFramebuffer.OnBeforeDelete, OnDetached, PublishRenderPassMetadata, RefreshWrappedFramebuffer; screen resize and uniform frame lifecycle | Explicit RHI import/retirement owns actual effects. Publication/detach/resize/frame advancement retain valid routing/bindings; native deletion and context identity are the single retirement/reset authorities. Uniform epochs/callback order remain with their original controllers. |
| GpuFramebuffer owning construction, GpuFramebufferAttachment.CreateRenderbufferStorage; GpuVao/GpuProgramObject/GpuProgramPipeline alternative factories | Owner-bearing RHI allocation invokes private backend/manager handoff. Backend geometry/shader realization declares private unpublished recipients; the unused pipeline wrapper factory is gone. No public recipient, provisional identity or recursive public factory is exposed. |
| BorrowedTexture construction/GetOrReplace; attachment construction/NotifyChanged/ValidateSlot; framebuffer DiscoverAttachment/ReconcileAttachmentMetadata/PublishRenderPassMetadata/RefreshWrappedFramebuffer | Private RHI import services admit native work and invoke manager acquisition/projection. Covered facts reuse original entries; initialization supplies local snapshots. Recorded discovery and explicit recovery remain separate. Composition/selection/ownership stay in wrappers, while ordinary validation never repairs metadata. |
| GpuFramebufferAttachmentObservers and allocation NotifyChanged calls; owner/image weak relationships | Canonical entry publication delivers registered projections once. Existing manager OwnerImages covers local/replacement recipients and skips already-delivered entries; one failed dependent does not retire a committed owner or starve others. |
| BufferUsageHint/BufferStorageFlags, ShaderType and ProgramInterface/ProgramProperty bags; GpuPreparedBindings/graphics reflection consumers and ShaderResourceType native ordinal matching | Semantic BufferUsage/StorageAccess, ShaderStage, ProgramResourceKind, nullable ShaderValueType/typed fields and ShaderStageMask cross initialization/snapshots/refs/preparation. Only backend codecs interpret GL values; authored shader types have no native token identity. |
| GpuFramebufferAttachment validation; GpuUniformRingBuffer, GpuBindingRegistry, prepared limits; GpuSupport format-cache keys and GpuTextureInitialization.Resolve | Semantic RHICapabilities or explicit immutable limits supply acceptance. The existing capability authority performs discovery outside callbacks; backend initialization resolves cached formats/samples. No owner-side codec, second cache or hidden validation query is added. |
| GpuProgramLayout location/name/block lookup, Apply*Contract, TryBuild*, RebuildCache and capability/ErrorScope branches; native program/shader adoption and GpuProgramInterface fallback | Supplied snapshots drive ordinary layout/preparation. Explicit native assignment/adoption enters private RHI operations with manager provenance/output completion. Numeric/unrepresented uniforms do not reflect. Fixed RHI shader/PSO generations never retarget after executable replacement. |
| Wrapper SetDebugName, EngineShader/Framebuffer/MeshDebugLabels, GlDebug native implementation and ShaderPatchRecovery.GetErrorsString | Owner labels use RHI diagnostics; raw engine identities remain in restricted native diagnostic integration. Native labels/groups/notifications are DEBUG-only and best-effort; explicit failed-compile diagnostics do not become universal error drains or validity probes. |
| MetadataOperation/MetadataPublication and callers' concrete cache guards; GpuResource GLFW context fallback; EngineBoundaryResources footprint | Private IRHIResourceOperation/Execution retains the same cache exclusion and original identity; manager owns metadata commit. RenderContextRegistry owns physical discovery. Native boundary footprints stay under backend admission/capture/restoration. |
| GpuTextureRef/GpuBufferRef/auxiliary refs; queued upload, TextureStreamingManager, GpuSamplers and shader reload helpers; OpenGLPassTarget/private geometry recipients | Published facts, allocation policy, content certificates, context retirement and original associations stay with existing owners. Execution crosses RHI; backend pass/geometry recipients are constructed only by their owning realization services. Consumer values add no mutation, disposal, registration or discovery authority. |
| BeginFrame/PurgeCache, resize/startup/detach resets and broad recapture chains | Removed. Covered native observations propagate established fields; context changes, actual retirement and targeted uncertainty retain their existing authority. No semantic method simply forwards a routine purge. |
| Caller rewriting/adapters, handwritten native callback/bypass inventories, begin/end observation receipts and attribute knowledge categories | Generated native callbacks and OpenGLDriverCalls derive exact ABI/slots from compiler-selected OpenTK. Handwritten cache-owned post-call effects classify before withdrawal, use domain Withdraw methods, preserve native outputs/errors, and never veto/retry. Saved-original managed work has its own single publication owner. |
| Missing creation/default facts, generic ObserveWrapperMetadata and unused query/validity facades | Native factory/realization/configuration observations maintain original canonical entries. Missing covered identities are tracking defects; only recorded initial discovery or explicit original-entry recovery reads descriptors. Compiler/interface, format/sample/import flag and deletion outputs retain their named tests and narrow purposes. |
| Additional ManagedGraphicsGeometry.Create, EngineFullscreenGeometry.Upload, EngineLiquidPoolGeometry and ClientRenderingLifetime preparation guards | Replaced direct cache admission with existing RHI RequirePreparationAdmission. EngineGeometryInputs uses that same gate. Restricted engine upload/deletion callbacks remain explicit native-owner integration, not portable draw execution. |
| Failed EngineFullscreenGeometry upload finally/ForgetEngineMeshUpload; unused ObserveEngineMeshDraw; deferred mesh cleanup | Removed manual selector purges/heuristics and their unused cache partial. The backend's upload scopes and interception retain restored knowledge; the regression asserts zero recovery queries/equal binds after failure. Deferred original-context cleanup uses the admitted native deletion bridge. |

### Intentional private boundaries and regression scope

RHICommandContext.Composition selects concrete execution and CPU-only exclusion authority; normal
context methods retain only IRHIBackend dispatch. RHIShaderBinary.NativeFormat is internal opaque
executable-cache data: DriverProgramCache persists it unchanged, and backend creation/binary installation
interpret it. Public shader/PSO descriptions contain no native format selector. Platform handles instead
carry semantic handle kind/ownership at the import boundary; native object-name adoption stays private.

RHIBoundaryScope privately retains the existing EngineBoundaryScope restoration owner. That runtime
owner is not a serialized command/initialization/snapshot payload and cannot escape as a public context
parameter/result. EngineContext/RenderContextRegistry and client retirement hooks retain actual context
identity/abandonment duties. Engine fullscreen/liquid/particle adapters own engine membership, authored
streams and content certificates; native selector scopes and rendering execution remain backend-owned.
Native profiling and explicit engine diagnostics retain their narrow implementation contracts. These are
caller/ownership classifications, not exemptions based on a directory name or internal visibility.

RHIContractIsolationTests now traverses nested project-owned instance fields/properties, generic
containers and inherited storage from context/private-execution inputs/results plus all canonical and
initialization schemas. Negative fixtures prove hidden field/property/key/base leaks are detected.
Typed reflection checks reject property-bag/type-token regression. Scoped IL checks reject owner/manager
native execution and non-composition RHI backend selection; exact local getter tokens in the compiled
PE verify opaque binary consumers without loading unrelated engine/UI dependencies. The existing
SemanticResourceSchemaTests independently exercises explicit usage/storage/shader codecs and supplied
preparation. Native behavior/ownership tests cover the declared boundaries beyond reflection inspection.

Verification: delegated canonical shader-enabled Debug and ordinary Release builds passed with zero
errors (225/232 warnings). Each configuration passed 1,297 combined lifecycle, ownership, semantic-schema,
admission, failure/recovery, dependency, label, suppression, local-context, retirement and immediate/recorded
cases, plus 35 generator cases, with zero failures/skips. Exact commands and selection are retained in
`artifacts/rhi-phase8j/commands.txt` and `combined-filter.txt`; final receipts are
`build-{debug,release}-closed.log`, `combined-{debug,release}-closed.log/.trx` and
`generator-current-{debug,release}.log/.trx` in that directory. Fresh generator-test builds verified that
their copied analyzer assemblies match the corresponding production analyzer SHA256; earlier no-build
generator receipts are diagnostic only. Exact commands and hashes are retained in `commands.txt`.
Initial failing receipts remain explicitly superseded:
stale fixture factories/readback/retirement assumptions were corrected without weakening native-output
oracles; the opaque binary check now reads compiled IL without resolving unrelated UI signatures.

Compiler reference resolution selected `G:/Vintagestory/Lib/OpenTK.Graphics.dll`, SHA256
`5B57957CA9E4C5F7BFB7EC495B991E6B88CFAC52863A19E405492AB8C33CB1D2`;
`resolved-references.json` and `opentk-identity.txt` retain that identity. No shader bypass, JIT override,
instrumented Release, benchmark or game launch was used. Second source review is complete; independent
completion review rechecked actual caller routes, linked authorities and the combined receipts. Management
renaming remains in 8k; inherited integrated lighting, optional hardware/interop, package and user-run
visual/runtime qualification remain in 9. No source staging, unstaging or commit was performed.

## Management-service naming (8k)

The rename preserves namespaces, dependency direction, manager publication, allocation identity,
cleanup ordering, engine callback stages and all resource contracts. It changes no native operation.
The before/after source map is:

| Previous name | Current name / responsibility |
| --- | --- |
| GpuResourceManager | RHIResourceManager; the same canonical registry, publication operations and retirement service, including all partial files. |
| GpuResourceManagerSystem | RHIResourceManagerSystem; the same active manager and originating disposal-service publication. |
| GpuResourceDisposalQueue | RHIResourceDisposalQueue; the same managed-owner cleanup queue. |
| GpuResourceManagerModSystem | RHIResourceManagerModSystem; the same engine host and ordered manager shutdown. |
| GpuUniformRingSystem | RHIUniformRingSystem; the same render-thread allocator publication. |
| GpuUniformRingFrameController | RHIUniformRingFrameController; the same frame epochs, fences and allocator retirement. |
| GpuUniformRingModSystem | RHIUniformRingModSystem; the same engine registration owner. |
| GpuUniformRingBeginRenderer / GpuUniformRingEndRenderer | RHIUniformRingBeginRenderer / RHIUniformRingEndRenderer; unchanged Before/Done stages and render order. |

Production/test callers, reflection declarations, matching test names/files, composition and lifecycle
hooks use the current names directly. The empty GpuUniformRingFrameRenderers.cs navigation stub is
removed; the callback files own those responsibilities. There is no alias, forwarding service, duplicate
registry or transitional API. Generated-code inputs/tooling contain no superseded service references.

GpuBindingRegistry retains its name: it defines project shader ABI slots, not execution or runtime
registration. GpuUniformRingBuffer and Allocation retain their storage/consumer contract. Resource wrappers,
references, initialization, canonical metadata and identity/provenance records (GpuResourceRegistration,
GpuResourceMetadataEntry, GpuResourceHandle and GpuInitialDiscovery) are unchanged. GpuSupport remains
the existing native capability snapshot behind RHICapabilities; GpuSamplers remains shared owner policy.
Neither is a renamed RHI management service.

Earlier route inventories, commands and receipts in this document retain their original names and
candidate evidence. Apply the name map above when navigating the current source; it does not rewrite
historical test results. Active proposal/contracts, inventory/API coverage, parent-plan references,
resource/binding documentation and cloud/state/interception descriptions use current service names.

Source review against `3a036f0209e2c2246701fca3df16be62c1a36427` compared all 319 affected source files
with the declared name substitutions. Production differences beyond those substitutions are XML comments
and regions in three uniform-lifecycle files; native dispatch, allocation, registration, cleanup and
callback order are unchanged. All 71 filename replacements are present and superseded source files are
absent. Current engine-state documentation also now describes the installed native-table observation
route rather than removed managed call-site rewriting.

The newly selected deletion-queue integration fixture originally attempted to initialize another manager
after native observation had already installed one. Its seven failing cases now explicitly shut down and
dispose the prior test lifetime before initializing their deliberately fresh, unticked manager. The
live-replacement guard, original-context sentinels, background queues and independent native-deletion
assertions are retained. No production behavior was changed to accommodate the fixture.

Verification: delegated canonical shader-enabled Debug and ordinary Release builds passed with zero
errors (206/232 warnings). Each configuration passed 1,355 focused initialization, registration,
disposal, context-lifetime, binding and inherited corrective cases plus 35 freshly built generator tests,
with zero failures/skips. Exact commands, `focused-filter.txt`, `build-{debug,release}.log`,
`focused-{debug,release}.log/.trx` and `generator-{debug,release}.log/.trx` are retained under
`artifacts/rhi-phase8k/`. Initial Debug fixture failures remain diagnostic-only receipts, superseded
by the closed candidate. `analyzer-identity.txt` verifies current production/test-copy equality in both
configurations; compiler resolution and `opentk-identity.txt` confirm the same installed OpenTK identity
recorded above. No shader bypass, JIT override, instrumented Release, benchmark or game launch was used.
Second source review and independent completion audit passed, including final receipt reconciliation.
Phase 9 integrated lighting, hardware/interop, package and user-run qualification remains open.
No staging, unstaging or commit was performed.

## Integrated qualification (9)

The candidate starts at `016121e9936a789267d473d2be5d5906287423fb`. Qualification is in progress;
this section does not close the implementation plan or establish installed-game acceptance.

| Work | Controlling requirements | Evidence and remaining obligations |
| --- | --- | --- |
| Integrated rendering and fixtures | Plan Phase 9; Proposal representative outcomes, shader/PSO and recording contracts; Contracts submission/failure; resource and prepared-binding contracts | Fresh canonical Debug build passed. The initial diagnostic run was interrupted before completion; its partial failure inventory is not a full-suite receipt. Corrected candidate runs are recorded under `artifacts/rhi-phase9/`. |
| Creation, aliases and retirement | Resource contract initialization/query/retirement; inventory canonical identities and dependencies; archived publication and texture-reference contracts | Retain existing independent output, original-context, name-reuse and native dispatch assertions in the combined candidate. Hardware substitutes remain distinct from actual optional native execution. |
| Storage and diagnostics | Proposal recording/storage and optional diagnostics; Contracts list lifecycle/storage | Second source review confirms direct immediate backend dispatch without command storage, reused typed payload pages/byte arrays and DEBUG-only diagnostic counters. Result tickets retain their separate completion ownership. Existing suppression and query-reuse tests remain required in fresh runs. |
| Package and user evidence | Plan Phase 9; Proposal qualification boundary; canonical `build.ps1` and Cake `Build -> CompileSpirVShaders -> Package` | Compare tested/published/package/ZIP runtime and canonical shader identities. User-run results below remain separate; no agent game launch or benchmark is authorized. |

The fixture corrections submit complete captured resource tables before draws/dispatch, declare all
active MRT outputs, and project binding-only PSO layouts from supplied executable inputs instead of
assuming fullscreen inputs. Integer image fixtures request nearest filtering. Root shutdown adopts the
single installed manager. The exposure fixture dispatches all histogram groups consumed by reduction;
context-abandonment checks use the existing boundary token. Stage-loading tests validate canonical
compiled assets rather than requiring fixture sources in the production source directory.

`ShaderTextureTargetTests` checks semantic reflected dimensions rather than native numeric equality.
The full-lighting arithmetic fixture retains its independent direct-input sum and `0.002` tolerance:
only composite color destinations use RGBA32F, avoiding an additional half-float conversion whose
spacing can exceed that tolerance. Direct-lighting inputs and refraction formats remain unchanged.
This arithmetic test does not qualify production packed-color precision; its existing dedicated tests
retain their own formats and independent output assertions. Production shaders/algorithms are unchanged.

Additional controlled-input corrections follow the current physical contracts in
`LumOn.SurfaceCache.LightingContract.md` (lighting terms/units), `PBR.LightingModes.md`
(standalone/shared response), and `PBR.MaterialColorAndDisplay.md` (microfacet energy model).
World-probe scenario RGB is encoded into a cloned persistent upload at C=1/16 while distance alpha
and caller arrays remain unchanged. Near-field scenarios convert requested physical emission to the
material coefficient using the existing 32-nit calibration. AO contribution capture uses matched
zero visibility and independent diffuse/specular controls to isolate the original material's lobes;
changing roughness is no longer used as a false proxy for removing specular. Existing physical and
visibility assertions, formulas and tolerances are retained. Camera mock corrections reuse the existing
native-world fixture. Draw helpers explicitly complete desired shader selections before creating fixed
pipelines, so option edits cannot submit the old executable interface. These are fixture corrections,
not a change to production lighting or RHI acceptance policy.
The historical `artifacts/phase8h/closure5-head-comparison.json` is absent from this checkout.
Its previously recorded 477 inherited failures and fourteen unmatched sequence observations cannot
be reconciled case-by-case from the historical summary alone. Fresh full-suite results must identify
current failures; neither missing historical artifacts nor isolated passes waive integrated failures.

The corrected targeted Debug selection executed 108 cases: 70 passed, 38 failed, none skipped
(`artifacts/rhi-phase9/bulk2-affected-debug.trx`). The AO contribution, near-field physical emission,
state-cache binding and full-lighting arithmetic checks passed. Thirty-two world-probe gather assertions
still expect irradiance E while the current `lumon_worldprobe_gather.glsl` explicitly publishes E/pi;
unit incident radiance now produces 1 after the corrected persistent upload, against the old pi oracle.
The physical-irradiance debug checks passed. The user subsequently approved correcting only gather
assertions to independently compute E/pi, retaining debug E/tonemapping assertions and all existing
tolerances. Fresh execution is required; earlier failures remain diagnostic evidence, not a waiver.
The other six failures were a mechanical capture-fixture stub signature, corrected before Release
compilation; fresh verification is still required. Interrupted earlier broad runs are diagnostic logs,
not completed suite receipts and not proof of full-sequence closure.

The approved quantity correction passed fresh execution: `approved-affected-debug.trx` qualified
108/108 with zero skips. The same 108 cases passed within `approved-combined-release.trx`;
the complete combined selection passed 1,461/1,463 with zero skips. Its two residual owner-configuration
failure-injection cases did not observe a Harmony postfix on a small forwarding method; the actual
managed attachment command is the appropriate stable injection boundary. The corrected managed-owner
postfix passed in Debug and optimized Release, preserving the original exception, deleted candidate
and live borrowed-image assertions (`nativefix-summary.json`).

The broad Debug run aborted in native `GL.DrawElements`: 7,540 passed, 96 failed and eight skipped
before the crash. An isolated water-capture run reproduced it. Named index upload had never associated
an element buffer with the fixture VAO. The fixture now submits explicit retained vertex/index ranges,
the actual unsigned packed-input layout, both additive output attachments and a real graphics PSO
through RHI `DrawIndexed`; its optical/source/count equations and `0.0005` tolerance are unchanged.
Its optional diagnostic branch uses the same submission route and was not executed as qualification.

`handback-summary.json` records 113/119 passing in each configuration, with no skips or native crash.
All eleven water cases, managed owner failures, explicit solar/terrain bindings, geometry declaration
expiry, compatibility-context handback, camera publication/origin and local-exposure sampler isolation
passed together. The six residual debug-view failures require the fixture's matching frame/calibration
publication. Compatibility fixtures retire services and test-owned shaders on their original context
before switching; they load both OpenTK binding tables and restore the primary service afterward.
The optional-G-buffer debug-view null dereference is corrected: the geometry-only diagnostic supplies
no depth input, as required by `LumOn.WorldProbeLighting.ReproductionTests.md`, Near-field geometry
viewer, and `LumOn.TraceSceneGeometryContract.md`, Consumer inventory. Other modes retain their existing
G-buffer prerequisites. The actual renderer-created shaders still require matching physical publication;
that validation is not relaxed. The broader suite and original fourteen observations remain unqualified.

The next immutable candidate (`fixtures3-summary.json`) passed 146/152 in Debug and 1,601/1,609 in
the enlarged ordinary Release selection, with no skips or native crash. Native framebuffer-format
encoding/selected-image queries and complete HZB/temporal MRT declarations passed. Debug-view physical
publication now passes its identity guard; six failures instead count the obsolete engine rendering
callback, which backend-owned indexed submission intentionally bypasses. Replace that fixture spy with
the existing exact-ABI native capture while retaining pixel and mode-switch assertions. Two Release
PSO trace markers also require investigation after warm-up; narrower receipts do not waive them.

The following candidate (`fixtures5-summary.json`) passed 156/158 Debug cases and 1,613/1,615
ordinary Release cases, with zero skips or native crashes. All six full scene-runtime tests now pass
reload image/mask/UBO retirement and external-resource survival assertions: both light-shaft occlusion
and ambient occlusion reload through their existing PSO-before-images retirement owners, as required
by `PBR.Postprocessing.md`, Resources, settings and limits, and the ambient-visibility sidecar lifetime.
The PSO fixture observes actual direct/indexed native dispatch and reads existing execution/restoration
markers solely to classify the trace; native state queries still independently check active blend
factors and exact handback. This introduces no production instrumentation or JIT override.

The two residual world-probe diagnostic failures identify a fixture feedback hazard: its AfterBlit
destination was the same primary framebuffer holding sampled G-buffer images. The fixture now uses
a separate presentation destination after Before/Opaque callbacks, retaining native draw counts,
original pixel values/tolerances and all mode-switch assertions. This last input correction requires
fresh execution; no feedback guard was relaxed. Other broad-suite failures still require reconciliation
before the next full-sequence run, and the final package and user-run acceptance remain outstanding.

`fixtures6d-summary.json` records 200/204 Debug and 1,657/1,661 Release cases passing, with two
failures and two pre-existing skips per configuration. The presentation target, complete indexed liquid
draws, actual shadow-depth inputs, texture-reference equality and physical persistent-RGB decoding pass.
The two fixture descriptions are corrected for fresh verification: triangulation capture modes have
one RGBA8 output, and failed atlas executable preparation must allocate no scratch framebuffer. Candidate
retirement, destination pixels, numerical tolerances and successful retry assertions remain intact.
The skipped legacy atlas-trace hit-distance falloff and indirect-tint fixtures explicitly lack reliable
controlled geometry; this receipt does not qualify those observations.

The original zero-notification assertion in `LumOnBufferOwnershipTests` is retained. Source review
traced repeated identical setup to unconditional framebuffer-dependent publication, not a legitimate
image change or a routing event. Canonical snapshot assignment is preserved, but managed composition
and native observations now compare complete selection/default facts and original backing identities
before invalidating dependents. Actual changes, recovery, retirement and explicit external refresh
retain their notifications. New native/managed publication tests cover unchanged revisions, changed
images and forced explicit publication. The subsequent `fixtures7b-summary.json` records 562/568
Debug and 1,987/1,993 mixed Release cases passing, with four failures and the same two existing skips.
Framebuffer mutation/recovery/deletion, temporal and selected inventory checks passed; freshly rebuilt
shader-build tool tests passed 256/256 in each configuration.

The remaining repeated surface notifications came from reapplying an already-known mip range.
`SetMipRange` now retains context, ownership, availability and argument validation before suppressing
an identical request; actual changes retain image-dependent publication. The publication fixture
explicitly initializes its tested 0/0 range. Atlas failure coverage distinguishes an empty input rejected
before admission from a retired nonempty reference rejected during the bake boundary, retaining hostile
state restoration, unchanged output and successful retry checks.

Native particle fixtures now provide and restore their intended Add/Add equations for outputs 0/1;
the capture scope continues preserving caller equations. Native final-program capability assertions
follow the owned final path's deliberate exclusion of native final shader patches. No particle pixel
oracle or tolerance changed. `batch8b-focused-{debug,release}.trx` each passed 55/55 cases with zero
skips after canonical shader-enabled builds. Earlier batch8 setup failures remain diagnostic receipts.
The subsequent broad Debug diagnostic completed without a crash: 8,152 passed, 253 failed and nine
skipped (`batch8b-broad-debug-summary.json`). Its sources, original HEAD, runtime/PDB files and 1,889
input/runtime asset hashes are preserved in `batch8b-source-snapshot.zip` and its identity receipts.
After archiving the known-failing candidate, C# corrections proceeded while its immutable binaries
finished; source-inspection observations after that point are diagnostic only. The completed run
does not qualify the newer worktree. All final package identities require regeneration.

The next bulk correction uses canonical program metadata during atmosphere registration; supplies the
installed mesh's no-glow generic input; measures actual sky restoration scopes rather than singleton
context identity; retires fixture upload services before their original manager; and gives controlled
runtime hosts an explicit native-settings service. Water fixtures supply matching physical-light
publications, complete shader outputs and retained RHI indexed geometry. Capture output zero is
explicitly discarded, and storage-mutation tests distinguish active pass protection from idle retained
intentions. Existing numerical inputs, comparisons and tolerances remain unchanged. Delegated
Debug/Release validation is pending for this candidate.

The standalone GGX expectations and legacy material-energy assertions remain unchanged pending human
agreement. Source review links the material differences to the approved hemispherical interface-energy
reserve before diffuse transport; an independent expected-value integration is required before changing
those oracles. Producer progress and dark/convergence observations still require separate diagnosis.
These focused and diagnostic receipts do not qualify the full integration.

Fresh tool suites passed in both configurations: OpenGL interception generator 35, shader-contract
generator 157, and shader-build tool 256 cases per configuration (448 total each), zero failures/skips.
The earlier canonical Release package completed. That candidate's runtime build, test copy, publish, package and ZIP all have
SHA256 `3097D51609C7A1C59BFBF92525277ADE4EC143292C63B967641D29CD101384EC`;
all 761 packaged/ZIP shader variants match the canonical producer. ZIP SHA256 is
`D68FC02BBD4299B60AF71F376A44B8DB981E6936A7374523A9D1B8960367ED9C`.
Exact package results are in `artifacts/rhi-phase9/package-identity.json` and its log.
These receipts establish artifact identity for that earlier candidate, not installed-game or visual
acceptance. The subsequent debug-view runtime correction supersedes its runtime identity; rebuild and
reverify the final package before acceptance.
The later batch10b candidate passed shader-enabled Debug and ordinary Release builds. Its focused
Debug selection passed 28/38, with zero skips; mixed Release passed 2,012/2,026, with two existing
probe-atlas hit-geometry skips. Source, both configurations' DLL/PDB files and runtime inputs were
archived without drift (`batch10b-summary.json`, source ZIP SHA256
`8389F92291C47AFEB85F5AE05B7A76F3E644B26AEABF4C724635CA11E749CED2`).

Batch11 replaces the remaining managed draw spies with actual native indexed submission observation.
The water capture rejection fixture now supplies a real undersized frame-lighting buffer, asserts the
minimum-extent validator's exact `ArgumentException`, verifies that no native draw occurred and restores
every original override in `finally`. Publication, hostile-state restoration and recovery assertions
remain intact. Water flow inputs author one nit of emission, and the scattering point source authors
10,000 candela through isotropic lumens rather than passing that number as an engine-scaled payload.
The original numerical expectations and tolerances are unchanged. These input corrections follow
`PBR.MaterialColorAndDisplay.md` and the physical source definitions in `LightingCalibration`.

Both batch11 builds passed. Its focused Debug selection passed 119/135 with zero skips; the consumer,
temporal and capture-state cases pass. Remaining failures include four surface-refresh cases, two
particle observation cases, a wait test reading unpublished filtered history and nine water cases.
The archived candidate is `batch11-source-snapshot.zip`, SHA256
`884CBFF6C0DD12923A850205BE01332EB3E4433B9DE0864FDD803D9C6955848B`;
`batch11-debug-cases.json` retains exact case-level outcomes. Mixed Release completed with 2,105 passed,
sixteen failed and two existing skips. Source-only thaw permits subsequent corrections without qualifying them from
that older binary run; no final integrated or package claim follows from these partial results.

The next source corrections preserve those numerical assertions: particle draw observation starts
after native link setup finishes reloading core bindings; the wait fixture first requires real indirect
publication before scanning intermediate images; per-frame final-output validation likewise observes
only published output. All five settled-boundary finite checks and explicit NaN injection remain.
`RuntimeWaterReceiver` supplies physical storage matching its own alternate-view camera identity.
The non-production surface-cache fixture explicitly fits its authored full block field to 32 lux,
matching the existing enclosure fixture and leaving production-owned calibration unchanged. The fog
fixture authors its unit-reference radiance as inverse-sRGB(decode(original RGB)/32) through the
documented compatibility input; its original expected RGB and tolerance remain unchanged. These
fixture corrections have a separate source review and require fresh compiled verification.

Fresh batch12 Debug verifies that correction set: 129/135 passed with zero skips. Particle publication,
all four direct refresh checks, wait-boundary finite/NaN validation, consumer/temporal/capture-state
cases, alternate-view receiver publication and the unit-reference fog case pass. The remaining six
failures are the unchanged fixed-world water interface-energy assertions; their smooth Fresnel factor
differs from the documented GGX transmission model (`PBR.Liquids.md`, shared material response).
Changing that factor is awaiting explicit approval, independently of the earlier E/pi approval.
The archived source ZIP SHA256 is
`3B012422D9CBCE7E1E36572D0D1D4CD9EDB39444C5C36CC76C4C5DED2AFEB7CE`;
the matching mixed Release run passed 2,115/2,123, with the same six failures and two existing skips.
Post-run checks found no drift across 3,692 compiled runtime/input files. Subsequent counter guards
and failure-only shallow-water replay are a separate candidate.

The diagnostic-counter gate also identified `LumOnWorldProbeClipmapGpuResources.InvalidationDispatchCount`,
whose only consumers are two test classes. Its declaration and increment are now `DEBUG`-only, as are
those tests' counter snapshots/assertions. Operational `PendingInvalidationCount`, queue behavior and
all Release pixel, readiness, clearing and lifecycle assertions are retained. This subsequent source
candidate requires Debug/ordinary Release compilation, focused effects checks and confirmation that
the property/backing field are absent from the Release assembly.

Batch13 verifies that counter correction: both shader-enabled builds pass, and all thirteen clearing,
queue/readiness and partial-world-probe cases pass in both configurations with zero skips. Cecil
inspection confirms one counter property/backing field and two accessors in Debug, and none in
ordinary Release (`batch13-counter-metadata.json`). The separate Debug shallow-water sweep passes
21/24; replay of each failing adjacent pair shows triangle reconstruction switching to weighted
sampling despite smooth UV motion and smooth direct filtering. Its original numerical assertions
remain unchanged. All 104 source and 3,692 compiled-input identities remained stable.

That replay justifies a bounded production correction under the integrated regression task's
runtime-defect obligation. Task traceability is `Phase 9 integrated regressions` ->
`PBR.WaterRefraction.md`, Background resolution and bilateral receivers / UV distortion -> actual
coverage, foreground rejection, coherent RGB/position and bounded work. The archived governing UV
tasks were also consulted at `e918f189^:docs/PBR.WaterRefraction.todo`; shared units/output/OIT
constraints remain in `PBR.WaterMedium.md`, `PBR.Liquids.md`, `PBR.LiquidRenderer.Proposal.md`,
`PBR.SharedDisplay.md`, `PBR.OutputDithering.md` and `PBR.MaterialColorAndDisplay.md`.
Pixel-normal selection now tries the existing adjacent-patch correction only for reduced backgrounds
when its initial source triangle lacks coverage. It preserves ray/UV lookup limits and adds at most
three missing-neighbor depth reads, with original foreground/layer checks and no extrapolation.
The coherent-input shallow fixture additionally checks the two-lookup/fifteen-depth/four-color source bounds.
This changes no interface-energy model, expected value or numerical tolerance. Source review and
fresh shader/build/GPU verification are required before accepting the correction.

Batch14 builds pass both configurations. Its focused Debug selection passes 221/222 with zero skips:
pixel-normal 14/14, filter 31/31, budget 32/32, diagnostic 72/72, reduction 6/6, UV 43/43 and shallow
continuity 23/24. The two non-shoreline reconstruction jumps are fixed. The half-resolution shoreline
case still jumps by `0.0371108055` against the unchanged `0.01` limit; its failed sample records thirteen
depth reads and weighted fallback after the adjacent attempt. Do not infer permission to accept a
foreground neighbor from this failure. Release GPU/tool checks and packaging are held. Source and
compiled inputs remained unchanged throughout the run (`batch14-debug-summary.json`); source ZIP
SHA256 `4889B61C49A6F1C7BBC60A96F21A57D4500919E9C03D3BBA11E0D72900C5C358`.
A full Debug diagnostic against that same candidate is running to expose remaining failures and
full-sequence observations while the shoreline and numerical-reference decisions are unresolved.

The remaining shoreline replay lies just outside represented submerged coverage. The last eligible
source corner projects to `u = 64.5/128`; the needed neighboring reduced texel retains a source at
`u = 66.5/128` that is above the water interface. Rejecting that neighbor is correct. Weighted fallback
moves back toward the covered quad's center and causes the observed jump. A proposed quality-zero
alternative would clamp to the nearest point inside validated coverage before associated triangle
interpolation. This would change edge continuation, so implementation awaits an explicit decision.
Do not loosen foreground eligibility, extrapolate unsupported coverage or relax the `0.01` assertion.

The full Debug diagnostic additionally exposed `GpuProfilerConfigurationTests.ScopeMeasuresOnlyInDebug`
collecting zero samples from an empty scope. Source review identifies an independent valid-zero defect:
the Debug profiler rejected equal completed timestamps even after ticket readiness and origin/revision
checks. Subsequent C# source accepts zero-duration samples and retains rejection of reversed/nonfinite
durations. A native-result fixture forwards actual reads once, retains original timestamp pairs and
can supply deterministic equal/reversed completed values without replacing availability. The original
sample-count assertion remains unchanged; new cases require two distinct completed queries and verify
zero-duration statistics or rejection, plus Release measurement unavailability. This is correctness
validation with no performance threshold. Source review passes; compilation/fresh execution must wait
for the immutable broad run to finish. The original empty-scope failure's exact returned pair was not
retained, so that specific attribution still requires fresh evidence.

The immutable broad Debug run then completed without a crash: 8,367 passed, 38 failed and nine
skipped out of 8,414. All 3,692 compiled inputs retained their archived hashes. The named full-lighting
integration case passes; exposure display/shader, particle capture/handback, installed depth,
sampler and worker families also pass in full ordering. Exact individual outcomes are retained in
`batch14-broad-debug-summary.json`, `batch14-named-family-outcomes.json` and
`batch14-earlier-sequence-outcomes.json`. This is current Debug sequence evidence, not a reconstruction
of the missing historical 477-case artifact or final Release qualification. The nine skips are fixture,
obsolete-path or explicit opt-in gaps, not reported hardware failures.

Follow-up source-only corrections preserve the expected values and tolerances. Identity observation
decodes persistent RGB before its physical-32 assertion. The world-probe restoration predicate now
selects a neutral floor from the same accessor/fallback decoder as its coefficient. The controlled
runtime lighting publication authors its declared 32-lux full block field before freezing storage;
it changes only the two block coefficients/floors and preserves the real owner's already captured
point calibration and all sky fields. Actual frame and generation publication remain authoritative.
Source review checked that ordering and the affected host's temporal consumers. Production engine
calibration is unchanged. These C# changes and the profiler correction require a fresh compiled
candidate; old Debug results cannot qualify them. Legacy material response and water interface-energy
oracles, and the shoreline continuation policy, remain pending explicit decisions.

Batch15 compiles both configurations and passes 62/66 focused cases each, with zero skips. The four
failures are the unchanged display/source material-energy expectations. Profiler configuration passes
4/4 each: deterministic equal/reversed completed queries take two reads in Debug, and no reads or
measurement state in Release. The ordinary captured Debug pair is positive; it does not recover the
discarded pair from the earlier failing full run. Identity decoding and world-probe restoration pass.
The current counter-member inspection again confirms Debug presence and ordinary Release absence.
ShaderBuildTool passes 256/256 per configuration. All 111 source and 3,692 compiled-input hashes
remain stable (`batch15-summary.json`, `batch15-counter-metadata.json`).

The subsequent full ordinary Release diagnostic completes without a crash: 8,368 passed, 39 failed
and nine skipped out of 8,416. All 3,692 compiled inputs retain their captured hashes. Named exposure,
particle, installed-depth, sampler and full-lighting checks, and the named original-sequence worker
checks, pass in full ordering; exact
results are in `batch15-broad-release-summary.json`. This is failed broad correctness evidence, not
final qualification. In addition to the known material/water/shoreline requirements, it exposes two
worker-progress deadlines, a missing routing fault injection and a cloud direct-channel result of
zero where the unchanged test expects `.25/.75`.

The next source-only fixture changes keep the original deadlines and output oracles. Routing injection
targets the EH-owning managed routing operation rather than an inlinable forwarding method. Cloud
failure diagnostics retain the original filtered result before separately reading exact source and
destination texels, avoiding a new prepublication wait. Worker diagnostics report pending/credit/pool
state after the unchanged startup/progress guard fails; those samples alone establish no causal
starvation or product defect. No production cloud algorithm, eligibility rule or worker scheduling
policy changes. Independent second review passes.

Batch16 executes the same warmed sixty-case union in both configurations: Debug passes 60/60;
ordinary Release passes 59/60. The managed routing fault seam now observes the intended failure and
retains its cleanup assertions. The worker classes pass this selection, which does not resolve their
earlier full-sequence deadlines. Cloud publication still fails. Failure-only exact readbacks show that
the source RG16f direct-light texture already contains the wrong values; publication is not yet shown
to be their origin (`batch16-summary.json`).

Batch17 captures the actual native texture-clear inputs while forwarding every call once. Debug
passes 60/60 and Release 59/60. Both configurations receive the intended full 16x66x16 extent and
RG/Float values (0.25,0.75), but Release subsequently reads zero from both source and destination.
The capture reloads binding tables and changes stack/timing behavior; its evidence localizes inputs,
not a transparent proof of unchanged execution. Original output assertions remain intact.

The next standalone checks compare complete half-float volume contents through the wrapper and
semantic entry points. Release batch18 passes 64/66: RG16f wrapper clearing fails independently of
publication, while semantic RG16f clearing and both R16f/RGBA16f paths pass. Batch19 adds stack-backed
semantic values and passes 67/69: semantic RG16f clears pass with both heap and stack storage;
the wrapper and cloud cases still fail. A failure-only repeated wrapper clear, performed after saving
the original sample and exact readbacks, also produces wrong source data. This rules out treating
cloud publication as a necessary reproducer, but neither a driver/compiler defect nor a production
fix is established. Per-case ordering/state remains relevant when comparing separate theory cases.
All nine standalone checks preserve exact, half-representable constants; cloud assertions and worker
deadlines are unchanged. Independent second review found no must-fix diagnostic/test issue. Batch18
and batch19 rebuild ordinary Release only; no fresh Debug qualification is claimed for these additions.
All captured source and compiled-input hashes remain unchanged within each execution. Exact cases,
clear inputs and readbacks are retained in `batch17-summary.json`, `batch18-summary.json` and
`batch19-summary.json`. These are diagnostic receipts, not integrated or package acceptance.

Batch20 controls object and native-state ordering within one test: alternating semantic clears with
wrapper and direct `ClearImmediate` calls on the same live RG16f object retains every readback before
asserting. All three semantic steps pass, while all four wrapper/direct-wrapper steps fail despite
distinct half-exact input pairs. Release passes 67/70 overall; exact bytes and mismatches remain in
`batch20-alternating-failure.txt`. This strengthens the entry-point distinction without proving its
underlying cause. A subsequent allocation-free named-value representation candidate passes Debug
70/70 and Release 68/70; the original standalone and cloud cases still fail. That production candidate
was reverted, rather than retained as an unproven fix. `batch21-summary.json` describes only that
rejected candidate; current binaries must be rebuilt before further current-source qualification.
The original clear implementation and every numerical oracle remain intact. Diagnostic tests remain
available for continuing the investigation; there is no cloud or integrated completion claim.

Batch22 verifies the driver allocation format and copies client bytes before and after native clear
dispatch. Both are correct; the additional descriptor query changes reproduction, so its passing
decorated path cannot qualify the original path. A whole-image clear specialization also fails the
short-lived full and partial payload checks in batch23. It was removed, including its admission change;
the separately reviewed heterogeneous mutable cube-face edge is therefore not introduced.

The retained correction is confined to native clear-value lowering: RG/Float inputs expand to the
equivalent RGBA/Float texel with blue zero and alpha one, preserving both supplied float components.
OpenGL core sections 8.4.4.4 and 8.21 define those defaults and clear conversion. There is no new
query, wait, heap allocation, lifetime owner or metadata publication. Independent source review passes.
Batch24 passes 110/110 checks in both Debug and ordinary Release, including the original cloud outputs,
undecorated wrapper clears, same-object alternation, short-lived full/partial-region payloads and all
34 RHI image cases. No compiler/driver root-cause attribution is claimed from these results.

Temporary native capture, descriptor probes and cloud readback/re-clear diagnostics have now been
removed; the original cloud test is restored exactly. Permanent clear regressions retain all numerical
checks. New 32-bit format cases use non-half-representable float inputs to detect precision loss;
the half-float case retains exactly representable values. This cleaned candidate requires fresh
verification; `batch24-summary.json` qualifies only its archived predecessor. All final phase markers
remain unchecked.

The cleaned candidate passes fresh batch25 verification: canonical shader-enabled Debug and ordinary
Release builds have zero errors, and the expanded cloud/image/texture/view/recording/readback selection
passes 348/348 in each with identical case sets and zero skips. This includes all fifteen permanent
clear cases and the original, undecorated cloud publication test. The additional ordinary Release
unit selection passes 195/195, including the supersession worker case and all fourteen CPU-fallback
cases. This is broader unit evidence, not a substitute for the earlier full-suite ordering or a cause
for its deadline failures. The current `InvalidationDispatchCount` assembly-member gate passes:
Debug retains the property/field/accessors and ordinary Release has none. All 116 captured source and
3,692 compiled-input hashes remain unchanged during execution (`batch25-summary.json`,
`batch25-counter-metadata.json`). There is no new full-suite, final package or live-runtime receipt.

Independent source review and affected-requirement audit confirm exact clear regions, float precision,
component defaults, unchanged caller ownership and allocation/query behavior. No temporary native
capture or failure-only cloud readback/re-clear remains. The missing historical comparison artifact,
material/water oracle decisions, shoreline policy, fully passing integrated sequence, final package
identity and actual user-run acceptance continue to block phase completion.

The same frozen candidate subsequently completes the full ordinary Release selection without a crash:
8,431 total, comprising 8,387 passed, 36 failed and eight skipped. Source and compiled-input hashes
remain unchanged. The original cloud publication test, all fifteen clear cases and both earlier worker
deadline cases pass in this full order. All 36 failures also occurred in the prior full Release run;
there are no new failure cases. The eight current skips have no hardware cause. The former ninth skip,
`ProfilerEventSourceTests.BeginScope_EmitsStartAndStop_WhenEnabled`, now passes; its previous listener
observation skip is not a hardware qualification. Exact records are in `batch25-broad-release-summary.json`,
`batch25-broad-release-comparison15.json` and `batch25-broad-release-skip-transition15.json`.
This verifies those worker cases at this revision without assigning a cause to their earlier deadlines.

Recorded tool results recover the original 23 candidate-only case identities and the specifically
classified five transaction-fixture plus four obsolete-assumption cases. The remaining fourteen
sequence-sensitive names, messages and recorded stack lines are therefore recoverable without using
family-level substitutes. `historical-recovery-original14.json` and its provenance retain exact source
call identifiers, timestamps and lines. `historical-recovery-current25.json` matches all fourteen by
identical full name and theory parameters to fourteen unique passing cases in the terminal full run;
there are no missing, renamed, duplicated, failed or skipped matches. This is current full-sequence
evidence for that exact set, not a retrospective causal explanation or recovery of the 477 inherited
case rows. The full historical baseline mapping still requires recovery or an explicit scope decision.

The remaining routing-fixture failure is its missing injected exception; the full run does not establish
whether managed patching, observation or another condition caused that miss. Its replacement removes
the Harmony failure patch. The fixture forwards actual named submissions, then reports one simulated
`InvalidOperation` through a clean native error query after the selected target's second submission.
Existing managed error attribution must throw outside the unmanaged callback, while every real error
is returned unchanged. Tests require two actual submissions, one delivered simulated status, no real
driver error in the cleanup scenario, and unchanged independent owner/lifetime cleanup. A separate
negative control checks that a real `InvalidValue` is returned before the pending simulated status.
This exercises handling of simulated native status after valid routing; it does not claim driver rejection.
The fixture-only changes require fresh review and compiled/full-order qualification.

Batch26 compiles both configurations with zero errors and passes the expanded selection 349/349 in
each, with no skips and identical case sets. The cleanup case observes two actual selected-target
submissions, exactly one delivered simulated error and zero real driver errors; independent owner
restoration and released-image checks remain unchanged. The negative control preserves a genuine
`InvalidValue` before delivering the pending simulated `InvalidOperation`, followed by `NoError`.
Source review confirms the no-argument unsigned error-query ABI, real-call forwarding on every path,
post-dispatch arming, preservation of real errors and deactivation before native-table restoration.
There is no Harmony fault patch or unmanaged exception injection. Retained callbacks cannot inject
errors after disposal; this fixture does not promise that its older capture paths stop recording calls.
All 117 captured source and 3,692 compiled-input hashes remain unchanged during execution
(`batch26-summary.json`). These focused results do not replace full-order qualification of the
previously sequence-sensitive injection case.

Independent numerical preparation is retained only in ignored `oracle-preparation.js` and
`oracle-preparation-results.json`. General-view double-precision light-hemisphere quadrature converges
independently of the production sampler/table, agrees with refined normal-view Simpson integration,
and predicts the cache's projected diffuse reserve without copying GPU output. Local lookup/storage
analysis distinguishes the ideal model, raw asset approximation and conditional precision assumptions.
Fixtures inherit shading quality, so an approved oracle migration must make that input explicit.
No expected values, inputs or tolerances have changed. User decisions on material/water expectations
and shoreline policy remain pending; analytical preparation is not approval or acceptance.

The independent `audit-stage-completion` review concludes that the phase is not complete. Reusable
typed recording pages and direct immediate dispatch satisfy the reviewed storage model, and the
Release diagnostic-counter gate has current assembly evidence. Failed broad correctness, unresolved
quantity/shoreline decisions, exact historical 477-case mapping, final package identity and actual
user-run acceptance remain required. The missing historical mapping needs recovery or an explicit
replacement/removal decision; fresh narrow selections cannot silently waive it.

### Current full-run and package reconciliation

The immutable batch27 candidate completed full ordinary Release with **8,389 passed, 35 failed,
8 skipped** and full Debug with **8,387 passed, 36 failed, 9 skipped** (8,432 cases each).
The 35 common failed cases have identical messages. Debug additionally failed
`SurfaceFallbackWorkerTests.CompletionPreservesExplicitTraceOutcomes(outcome: 1, complete: True)`
at its unchanged three-second worker-completion wait. Full-run summaries, exact failure/message
comparisons and original-fourteen maps are retained under `artifacts/rhi-phase9/batch27-*` and
`historical-recovery-current27-*.json`. All fourteen recovered cases pass uniquely in each run.
Both routing injection/control cases, the original cloud outputs and all fifteen clear regressions
pass. Neither run establishes why the earlier synchronous worker wait expired.

Both `RecreatedDarkCacheRejectsRetainedFinalLighting` variants fail their initial
`CompleteConsumers` physical-lighting assertion, before atlas recreation: expected 2.5564654,
decoded 2.3964844. Debug and Release messages match. This is not a readiness timeout; later
recreation/restoration assertions were not reached and remain unqualified.

Canonical `./build.ps1 --configuration=Release --spirv=true` passed. Before cleanup, the generated
workspace `Releases` directory was checked for path/link safety and its 1,131 existing files were
backed up with matching hashes. Runtime, full-run tested copy, publish, packaged directory and ZIP
all contain DLL SHA256 `6960E7024A3F7CCC873B653FC210F6EA050FF0EEE801434E068A5FE35A8BEF21`.
All 761 SPIR-V binaries and the digest manifest match their producer; no extra/missing shaders
or test artifacts were packaged. The optimized Release metadata retains no inspected diagnostic
counter. ZIP SHA256 is `A7B01A70C1138FEA5BE341AAC05F4DED8551D12C9F0955BBB5BC700D889CDE5E`.
Exact receipts: `package-identity-batch27.json`, `package27-cleanup-safety-backup.json`,
`canonical-package-release-batch27.log` and `batch27-pipeline-closeout.json` under
`artifacts/rhi-phase9/`. Source/input comparisons show zero drift during both full runs and packaging.
These receipts qualify this candidate package only; installed-game and final acceptance remain open.

After those runs and packaging completed, the Surface Cache worker test harness was changed to
asynchronous polling. The original predicates, three-second deadlines, 50ms negative observation,
submission counts and output assertions remain. One monotonic deadline is checked before accepting
progress after every yield; no late drain, retry, production scheduling or global pool tuning is added.
The cancellation test releases its blocking terrain gate even if the entry assertion fails.
Failure-only pool/busy diagnostics preserve the failed guard without claiming a timeout cause.

Traceability: phase 9 integration/fixture qualification ->
`LumOn.SurfaceCache.GeometryFallback.md`, Backend and completion contract / Bounded ownership and work;
`LumOn.SurfaceCache.TraversalBudget.md`, Explicit finite segment;
`LumOn.SurfaceCache.HitLightingRetries.md`, Work and storage bounds; and
`LumOn.SurfaceCache.UpdateBudgets.md`, Selection and bounds -> immutable bounded completion,
frame-credit/cancellation ownership and retained results remain unchanged. Fresh focused validation
and independent source review were required for this test-only correction. An isolated pass will not
waive the failed full-order receipt. Production package identity remains the batch27 product identity;
the changed test assembly must be tracked separately. The other numerical/policy decisions and
unavailable historical baseline mapping remain unresolved; no completion markers are checked.

Second source review and the independent affected-requirement audit pass the harness change.
Batch28 canonical Debug/Release builds each passed with zero errors. The full unit namespace passed
**1,133/1,133 in each configuration**, zero failures/skips and identical case sets: Surface Cache
worker 20/20, World Probe CPU fallback 14/14 and the previously observed chunk-worker case 1/1.
Release runtime identity remains the verified package27 identity; Debug runtime identity likewise
matches its full-run archive. The changed test binaries and 118-source/3,692-input archive have
separate batch28 receipts and zero pre/post drift. Full27 and package27 receipts remain intact.
The earlier failed full-order receipt remains retained; broader integration, numerical/policy decisions,
historical-baseline reconciliation and user-run acceptance remain open. Exact commands, per-case
worker outcomes, identities and counts are in `artifacts/rhi-phase9/batch28-*`.

The subsequent immutable full Debug28 run completed **8,389 passed, 35 failed, 8 skipped**
(8,432 total), without a crash. All 35 remaining cases and their messages match full Release27 exactly;
no new failure appeared. All twenty Surface Cache worker cases pass, including the previously failed
outcome-1 completion; the fourteen World Probe CPU worker cases and chunk-worker case also pass.
The exact original fourteen sequence-sensitive cases each match one passing result. The skip count
fell from nine to eight because `VgeProfilingCountersTests.Provider_Emits_ChunkProcessing_Counters_WhenEnabled`
passed instead of taking its existing EventListener-not-observed skip; no skip was added or removed
from source. Thus the corrected worker harness has current full-order evidence, while its earlier
timeout cause remains unproven. The 35 numerical/policy failures remain failures, not waived results.

All 118 source files and 3,692 inputs match the archive before/after execution; runtime and package
ZIP hashes remain unchanged. Exact proof is in `batch28-broad-debug-final-reconciliation.json`,
the original `batch28-broad-debug.trx` and `historical-recovery-current28-debug.json` under
`artifacts/rhi-phase9/`. Historical27 and current28 maps identify their respective original TRX runs;
the original raw receipts remain intact. No additional Release, build or package rerun was needed
for this test-only correction. The stage remains incomplete pending the numerical/policy decisions,
the unavailable 477-case historical mapping and user-run visual/runtime acceptance.

### Approved independent GGX oracle correction

The user approved updating the legacy material-energy and water-refraction expectations while
retaining authored inputs and numerical tolerances. `GgxHemisphereReference` integrates the
height-correlated Smith GGX model over light directions with double Gaussian quadrature, independently
of production importance samples, lookup assets and GPU observations. It applies the documented
compensation after integrating E/Ef. Its projected-view average supplies the source diffuse budget
before RGBA8 quantization; directional reflectance supplies receiver response and water transmission.
The normal-view reference now uses a log-slope change of variables to resolve minimum roughness.

Traceability: phase 9 retained numerical-oracle requirements and this explicit exception ->
`PBR.MaterialColorAndDisplay.md`, Microfacet energy model; `PBR.LightingModes.md`, Shared material
response; `PBR.Liquids.md`, Material and optics; `LumOn.SurfaceCache.LightingContract.md`, Lighting
terms and units -> independent W/R, complementary diffuse/transmission budgets and quantization.
Source color, emitter calibration, roughness, material inputs, ray/Snell geometry, confidence,
display conversion, tolerances and lifetime assertions remain at their existing values.
The old full-run failures remain diagnostic receipts; fresh affected validation is recorded below.

The user separately approved correcting `CappedHistoryPreservesConstantIrradiance` to seed from
one completed, representable sample. It keeps weight 1024 and exact RGB equality, checked across
three repeated identical-lighting updates. The absolute incoming GGX value remains constrained by
the separate enclosure tests. Its old RGB-32 seed differed from its current incoming reflection
and therefore did not represent the constant-illumination property being tested.
The shoreline policy, unavailable historical baseline and user-run acceptance remain open.

Canonical Debug and ordinary Release builds passed with zero errors. The final combined seventeen-class
selection passed **249/249 in each configuration**, zero failures/skips and matching case sets.
Every one of the 34 previously failed numerical cases has exactly one passing full-name/theory-input
match in each run; the unchanged shoreline case was excluded from this selection, not waived.
The twelve runtime scenarios and six PBR lifetime cases complete, including assertions previously
blocked by the early material mismatch. Capped history completes all three exact RGB/1024-alpha
checks. The selection also includes 95 water cases, 23 energy-table cases and direct/shadow regressions.

The 131-source/3,692-input archive has zero pre/post drift. Product Debug/Release runtime hashes and
the verified package ZIP hash remain unchanged. Receipts are `batch30-results-summary.json`,
`batch30-prior34-exact-mapping.json`, `batch30-postrun-identity.json` and both original TRX/logs under
`artifacts/rhi-phase9/`. No full-suite rerun or new package was needed for this affected test-only
qualification. A new complete integration receipt and final stage acceptance remain open with the
shoreline policy, historical mapping and user-run evidence. No runtime/shader change, tolerance
widening, source-light change, staging or commit accompanies these oracle corrections.
Second source review and the independent affected-requirement completion audit passed against
these original receipts and controlling material/lighting contracts. The approved oracle/history
correction is satisfied; this does not mark the broader stage or user-run acceptance complete.

### Approved bounded shoreline continuation

The user approved quality-zero continuation at the nearest point inside already validated submerged
coverage. The reduced-background selector first retains its existing source/adjacent triangle attempts,
then finds the nearest projected point within its eligible source triangles and reuses the existing
associated geometry/RGB interpolation. No foreground verification neighbor becomes a receiver,
and no source triangle is extrapolated. Degenerate, missing or invalid color/geometry remains rejected.
The new geometry arithmetic adds no depth/neighbor reads; existing two-lookup/fifteen-depth/four-color
coherent-source budgets and the unchanged 0.01 moving-continuity oracle remain controlling.
The shallow sweep additionally asserts that quality-zero selected positions stay behind the interface.

Traceability: phase 9 actual-runtime-defect resolution -> `PBR.WaterRefraction.md`, Background
resolution and bilateral receivers / UV distortion, plus the separately approved bounded-continuation
decision -> source eligibility, coherent position/RGB, bounded work and unchanged optical/display
contracts. Earlier linked liquid/medium/display/dither/material contracts remain unchanged.
Shader-enabled builds, focused water/receiver/budget tests and independent source review are required.
The first shader compile exposed the liquid `origin` macro colliding with a local segment variable;
renaming that variable to `edgeOrigin` fixes expansion without altering the geometry calculation.
A later Release build exposed MSBuild misclassifying an informational `Windows error 5` rename-retry
line even though publication completed and the tool returned zero. Retry reporting now uses
`Windows status` while exhausted retries and genuine `SPIRV` failures retain their normal failure path.
Canonical builds and the local shader-tool qualification must cover this diagnostic correction.
The earlier candidate31 full run was intentionally interrupted after this new approval; its partial
2,422 passing/5 skipped cases are diagnostic only, never a completed full-suite receipt. Only its owned
test processes were stopped; the unrelated VanillaBuildingExpanded testhost was untouched.

Candidate34 canonical shader-enabled Debug and ordinary Release builds passed with zero errors.
The expanded GPU selection passed **472/472 in each configuration**, zero failures/skips. Exact
full-name/theory-input matching confirms all 35 former failures pass uniquely in each run; all
24 shallow-water sweeps complete their unchanged continuity/association/budget assertions and the
additional submerged-position check. The full ShaderBuildTool suite passed **256/256 in each**,
including real transient-lock success, persistent failure, cancellation and publication recovery.
The MSBuild probe reproduces the old informational false failure, accepts the new wording, and
still rejects a genuine SPIRV002 diagnostic and nonzero child exit; all four controls match expectations.

The 132-source archive and 3,692 compiled binary/dependency/asset inputs have zero drift. Current
changed/untracked code paths exactly match the archived source set, the index remains empty, and
the deletion of `VanillaGraphicsExpanded.Tests/GPU/Fixtures/NativeDrawObserver.cs` is recorded for
reconstruction against HEAD `016121e9936a789267d473d2be5d5906287423fb`. Test copies match the
refreshed production/test shader receipts (762/843 files including each manifest, per configuration).
Managed runtime hashes remain unchanged, but the earlier package27 shader payload is stale and
must be refreshed after current complete-suite qualification.

Exact receipts under `artifacts/rhi-phase9/`: `batch34-results-summary.json`, both original TRX/logs,
`batch34-prior35-exact-mapping.json`, `batch34-debug-shallow24.json`,
`batch34-publication-tool-case-outcomes.json`, `batch34-exec-classification-probe.json`,
`batch34-shader-receipt-output-proof.json` and
`batch34-postrun-identity-and-source-reconstruction.json`. Source reviews pass the supported-coverage
geometry and informational-log correction. Current complete-suite receipts, refreshed package identity,
documentation closeout, final stage audit and actual user-run acceptance remain required.
Second source review and the independent affected-requirement audit passed against the original
focused/tool/probe receipts, source-reconstruction supplement and controlling contracts. This qualifies
the approved corrections while leaving the complete-suite, package and live-acceptance gates open.

### Approved current-suite qualification scope

The user approved replacing the exact-row reconciliation requirement for the unavailable historical
477-case artifact with current full-suite, per-case qualification. Its absence remains explicit:
`artifacts/phase8h/closure5-head-comparison.json` was not recovered, and the original 477 rows are
not claimed reconstructed or retrospectively reconciled. This approval does not waive any current
failure or authorize labeling an unmatched current failure inherited. Final current-candidate full
suite outcomes, immutable source/product identities and explicit coverage gaps remain required.
The recovered original fourteen sequence-sensitive regressions remain individually required and
must each match one current passing case. User-run visual/runtime acceptance remains separate.

### User-run visual and runtime checklist

Use the exact Release package identified by the final package receipt, and record its runtime DLL hash,
game version, GPU/driver, tested settings and result for each item. No timing measurement is required.
Do not mark an item accepted from headless tests or from an older installed DLL.

- [ ] Enter a world and exercise normal terrain, entities, sky, liquids and postprocessing; check for
  missing outputs, corrupt geometry, binding errors and incorrect lighting.
- [ ] With lowest refraction quality and half-resolution backgrounds, move the camera along a shallow
  shoreline; check for abrupt color steps or foreground geometry leaking into the water response.
- [ ] Exercise configured compute consumers (atmosphere, exposure, depth hierarchy and enabled lighting
  work); check that their dependent rendering receives current results.
- [ ] Exercise the configured cloud mode and terrain/liquid occlusion, including enable/disable and
  movement; check that cloud/depth content is current and no stale frame is sampled.
- [ ] Resize the window and change render resolution; check target replacement, aspect ratio and continued
  drawing, including cloud/depth and postprocessing consumers.
- [ ] Reload shaders and change shader-dependent settings; check that the new compatible set appears,
  dependent passes continue and failures are reported without corrupting the installed set.
- [ ] Leave/reenter a world, then shut down normally; check for stale resources, disposal/context errors,
  crashes and callbacks that outlive their owner.

User evidence: pending. Record observed failures as failures with logs and reproduction steps; optional
hardware/import cases not executed remain explicitly unqualified. This checklist is preparation for
acceptance, not an acceptance result.
