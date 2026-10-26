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
and their explicit exclusions continue to define the full exact-adapter obligation. Consult the expanded
command names, retained `artifacts/gpu-resource-handles-baseline/api-command-matrix.csv` and pinned
`gl.xml` when replacing each family. Current engine usage never narrows supported overloads.
Content-only operations still migrate execution but do not gain metadata refresh adapters.

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

## G — Final cache split and qualification (Phases 7–8)

Audit A–F removals first. Native execution remaining in a completed family reopens that unit. Then rename
the residual cache type/files/callers directly to `OpenGlStateCache`; no old alias. Reconcile architecture
documents to implemented state, not this planning history. Run delegated integrated and canonical package
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
