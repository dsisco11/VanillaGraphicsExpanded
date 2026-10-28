# RHI migration units

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
  `GpuTextureMetadataReader`, `GpuTextureImageReader`, `GpuTextureFormatReader`,
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
