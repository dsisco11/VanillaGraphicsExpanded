# RHI migration units

Planning inventory for [Phase 1](Rendering.GpuCommands.todo), implementing the final
[contracts](Rendering.GpuCommands.Contracts.md) under the [approved proposal](Rendering.GpuCommands.Proposal.md).
Every unit below is outstanding runtime work. This document defines replacement boundaries, not a
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
  `GpuTransformFeedback.Create/BindBufferBase/BindBufferRange/Begin/End/Pause/Resume`.
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
