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
