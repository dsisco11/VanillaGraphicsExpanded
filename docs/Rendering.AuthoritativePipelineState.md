# Authoritative pipeline state implementation evidence

## Engine-boundary restoration

Inventory and implementation contracts established on 2026-10-05. Categorized cache storage
and declared boundary entry/restoration are implemented. Refraction and independent fullscreen
callbacks are integrated with headless correction evidence. Caller reconciliation and user live
acceptance after the startup correction are complete. Earlier pending statements below describe
the evidence available at those implementation checkpoints; Consolidated acceptance status records
the final disposition. This section records implementation against
[the restoration plan](Rendering.EngineBoundaryRestoration.todo), under
[the approved proposal](Rendering.EngineBoundaryRestoration.Proposal.md) and the
[parent sequencing exception](Rendering.AuthoritativePipelineState.todo).
Paths below are relative to the repository root. References to existing source names locate
the compatibility implementation; proposed names describe responsibilities to implement.
Second source review and independent audit-stage-completion review passed on 2026-10-05;
the audit confirmed complete inventory/contracts with no unresolved findings. The second review
corrected the caller count and distinguished explicit framebuffer blend application from Bind.

### Entry points and interruption

`HarmonyPatches/WaterRefractionCaptureHook.cs` prefixes
`EntityPlayerShapeRenderer.DoRender3DOpaque(float,bool)` and delegates to
`PBR/Liquids/WaterRefractionCapture.BeforeOverlay`. Eligibility is enabled refraction,
Opaque stage, non-shadow, local player, FirstPerson mode, and no previous attempt this frame.
The Before callback clears attempt/publication; dry geometry and nonpositive dimensions exit
before GPU work. Capture sets attempted and withdraws publication before checking these guards.

Installed assemblies inspected read-only with Mono.Cecil on 2026-10-05:
`G:/Vintagestory/VintagestoryLib.dll` and `G:/Vintagestory/Mods/VSEssentials.dll`,
assembly version 1.22.7.0. `SystemRenderEntities.OnRenderOpaque3D` calls disable-cull at
IL_001c, toggle-blend at IL_002e and enable-depth at IL_003e before invoking
`DoRender3DOpaque` at IL_007a. Player rendering sets hand projection at IL_00c2,
then invokes held-item and batched entity rendering. It does not replay all fullscreen state
on return from the prefix. These calls establish incoming state before interruption, not
a post-capture restoration contract. Preserve actual incoming values, including depth writes;
do not infer them from defaults. Later shader/texture/geometry setup is a separate contract.

The ordinary `DirectLightingRenderer.OnRenderFrame` (Opaque order 9) and
`PBRCompositeRenderer.OnRenderFrame` (Opaque order 11) are independent engine entry points.
They each need their own boundary. Their shared internal draw implementations run inside the
capture adapter's single boundary when invoked by capture. An internal draw must require an
active compatible boundary, rather than silently opening a nested one.

### Complete operation and effect matrix

Classification: **observed** means an exact-signature engine adapter or cache backend tracks
the mutation; **preserved** means the supported adapter explicitly restores it; **reestablished**
means a verified helper or resumed engine operation establishes it before consumption;
**excluded** means the supported operation cannot use that path without another contract.
Observation alone does not imply preservation.

| Operation/source | Effects and incoming-state disposition |
| --- | --- |
| Capture entry / `WaterRefractionCapture.Capture` | Existing outer fixed scope, framebuffer scope, blanket invalidation and duplicate CapturePipeline. Replace with one boundary before allocation. Pipeline disables depth test, blending, cull, scissor, enables all color channels and disables depth writes. Preserve each affected field. |
| `DirectLightingTargets`, `DirectLightingBufferManager.EnsureBuffers` | First use, resize, partial allocation and disposal can bind textures/FBOs and retire resource names. Existing collection and binding scopes own resources; outer independent read/draw scope covers legacy combined-FBO restoration. No viewport mutation until target binding. Preserve binding associations, not deleted names. |
| `DirectLightingRenderer.RenderLighting` | Missing mesh/dimensions/primary returns before scope; failed buffers or shader readiness returns after scope. Matrix/input preparation does not change projection ownership. LightingPipeline has the same six intents as capture. BindWithViewport changes viewport; Clear changes clear color and writes the owned MRT. Shader UseScope and RenderMesh follow; success returns true. Exceptions unwind all scopes. |
| `PBRCompositeRenderer.RenderComposite` capture branch | Withdraws publication; checks stage/mesh/dimensions/primary and isolated lighting. Owns FBO scope before preparation. No ordinary scratch/display target or current-frame GI. Shader readiness can fail. Pipeline and BeginCapture precede target bind; allocation failure is caught and null target exits without a receiver draw. Writes coherent color/depth, publishes only after completed draw, then returns. |
| Ordinary composite target preparation | `PrepareTargets` creates/resizes owned scratch and borrowed primary resolve FBO. Withdraw borrowers before resizing, refresh borrowed source identity. Allocation and shader failures can occur before the current legacy fixed scope; the new adapter must cover the whole operation. |
| Ordinary composite draw and cleanup | Same CompositePipeline; BeginFrame may create publication/reduction targets; shader inputs and UseScope, then primary resolve and display UseScope. `SceneColorParticleCapture.RestoreSsao` binds its owned target, sets viewport and the same six pipeline intents, then draws. `PublishRefractionScene` either publishes directly or runs reduction with CompositePipeline, viewport, shader activation and RenderMesh; catches optional publication failure. Include all these helpers in the callback boundary. |
| `Rendering/GpuFramebuffer.cs` Bind / BindWithViewport / Clear | Binding tracked by StateCache; Bind refreshes dirty attachments but does not apply blend policy. Explicit ApplyAttachmentBlendState (`GpuFramebuffer.Blending.cs`) changes indexed state and needs declared effects if invoked. Viewport currently raw: migrate to declared cache-backed dynamic application. Global ColorMask affects all draw outputs, not only index zero. Clear(r,g,b,a) mutates clear-color helper state: preserve through StateCache clear-operation storage, outside static PSO identity. Clear masks/scissor established by declared draw/clear state. |
| `GpuFramebuffer.Creation.cs` routing/attachments | DrawBuffers/ReadBuffer are FBO-local; creation changes owned FBO routing, not incoming engine FBO routing. Preserve read/draw bindings with existing scope. Mutation of routing on a borrowed engine FBO is excluded from this boundary. No framebuffer blend-policy reapplication is inferred from rebinding. |
| Texture creation / retirement | Existing texture scopes preserve allocation bindings/active unit. Deletion uses existing targeted lifecycle invalidation and deferred retirement. Do not fold bindings into fixed-function structs. |
| `Rendering/Shaders/GpuProgram.cs` preparation, UseScope, disposal | Retain readiness, failed-activation cleanup, prepared input publication and engine CurrentShaderProgram ownership. Existing UseScope stops/restores the previous owner (or compute/raw program fallback). Preserve it, including exceptions; restoration of prior owner may bind its resource inputs. Restore borrowed binding scope after shader cleanup, then pipeline snapshot last. |
| Installed `ClientPlatformWindows.RenderMesh(MeshRef)` | IL binds VAO and element buffer, draws, unbinds element buffer and VAO. No fixed-function or viewport mutation. Exact-signature mappings observe these calls. Engine rendering reestablishes its geometry before drawing; adapter additionally preserves incoming VAO and generic array-buffer binding for strict handoff, without rewriting VAO-owned EBO associations. No SSBO multi-draw overload is used by the fullscreen quad. |
| Shader resources / resumed engine inputs | ShaderProgramBase.Use binds program, default uniforms and selected textures; BindTexture2D mutates active unit/texture/sampler. VGE prepared input publication also changes resource slots. Preserve the active unit and touched texture/sampler/UBO/SSBO/image slots through existing cache binding APIs around the entire operation, after preparing executable resource footprints and before activation. Resolve/query missing incoming slots at entry. Prior owner activation runs first on cleanup. Do not assume every slot is rebound by held-item code or create another binding cache. |
| Unchanged state / unsupported helpers | Blend equations, cull mode/winding, scissor rectangle, depth range, stencil, sampling, clipping and patch/provoking state are not changed by these fullscreen descriptors/helpers. Leave untouched; new helper effects require expanded coverage before mutation. Arbitrary mod/raw GL, external shader callbacks with undeclared effects, context migration mid-operation and engine-FBO routing changes are excluded; fail entry or suspend authority, never assert warm-cache authority over them. |

Resource preservation footprint is the union of prepared shader assignments (including previous
owner restoration), allocation scopes and documented quad helper bindings. Preparation that can
mutate these bindings must itself use existing scoped APIs; otherwise its effects must be included
before it executes. A missing/unbounded foreign shader footprint causes optional capture to skip,
not an assumption that shader reactivation restores everything. Ordinary callbacks surface the
unsupported contract. This is an adapter declaration and existing binding-owner composition,
not a new resource hierarchy or resource cache.

### Legacy scope inventory and disposition

Initial repository search found 14 invocations plus the declaration. Three have since migrated;
11 source invocations remain, including one dormant helper. Each retained family needs its own
effect inventory and regressions before migration; presence of observed engine calls is insufficient.

| Source and invocation lines at inventory | Disposition / dependency |
| --- | --- |
| `PBR/Liquids/WaterRefractionCapture.cs` | Migrated to one pre-overlay boundary; duplicate CapturePipeline removed, actual child draw descriptors retained. |
| `PBR/DirectLightingRenderer.cs` | Migrated to independent callback adapter and shared in-boundary draw implementation. |
| `PBR/PBRCompositeRenderer.cs` | Migrated to independent callback adapter covering preparation, SSAO restoration and reduction. |
| `DebugView/Views/VgeWorldCellBoundsDebugView.cs:285` | Retain independent debug boundary; line geometry, shader and binding policy require parent debug-consumer migration. |
| `DebugView/Views/VgeGBufferOverlayDebugView.cs:157` | Retain independent overlay boundary; indexed outputs and engine blit ownership require debug adapter. |
| `LumOn/LumOnDebugRenderer.cs:731` | Retain OIT outer scope around live bounds/rays/orbs until shared debug contract and child effects are established. It does not call the frozen helper below. |
| `LumOn/LumOnDebugRenderer.cs:894` | Independent AfterBlit/debug fullscreen path; manual viewport/scissor and active-texture restoration must migrate together. |
| `LumOn/LumOnDebugRenderer.cs:1346` | Dormant private RenderWorldProbeClipmapBoundsFrozen helper; search finds only its declaration. Retain its protective scope with the method. Parent debug migration must remove the entire dead helper or validate an independent line/VAO/shader adapter before reuse. This is not nested live-bounds preservation. |
| `LumOn/LumOnDebugRenderer.cs:2563` | Independent normal-depth atlas overlay, engine blit program and sampler inputs; retain pending debug shader/binding contract. |
| `LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs:230` | UploadCpu performs two resolve draws; retain until upload/resolve adapter covers both targets/viewports, point geometry and shader cleanup. |
| `PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs:147,509,593` | BakePerTexture, ClearAtlasPage and BakePerRect are separate public entries. Retain each until allocation/clear/viewport/scissor/program/binding and iterative solver contracts are independently migrated. ClearAtlasPage is called independently, not a redundant nested scope. |
| `PBR/Liquids/WaterVolumeRenderer.cs:108` | Independent liquid pool boundary; viewport, indexed additive blending, UseSsbo bookkeeping and terrain helper effects require volume-specific adapter. |

No retained caller is approved for blanket removal. The legacy helper cannot be retired while
these consumers require it. Their current scopes are compatibility fallbacks, not proof of complete
indexed preservation or context safety.

All retained families are assigned to the [parent plan](Rendering.AuthoritativePipelineState.todo),
remaining-consumer migration and compatibility cleanup. Consumer inventory, cache/boundary coverage,
prepared pipelines, target/pass and submission/geometry contracts remain prerequisites. Existing
manual cleanup in those consumers is migration work, not permission to add it to migrated passes.
No remaining scope has validated replacement coverage. Removing only the dormant helper's scope
would leave unsafe code if reconnected; track whole-method disposition with its debug consumer.

### State representation and source layout decisions

Value types go individually in `Rendering/Pipeline/State/`, namespace
`VanillaGraphicsExpanded.Rendering.Pipeline.State`. StateCache knowledge, query, transition and
snapshot partials remain in `Rendering/`. Engine boundary declarations/adapters go in
`Rendering/Integration/`; context identity provider is separate from adapter policy.
Existing partial descriptor masks retain their stable numbering. No complete submission or
pipeline registry is introduced by the bounded restoration work.

| Existing authoritative field (all fixed-function partials) | Concrete value category / policy |
| --- | --- |
| depthTestEnabled, depthFunc, depthWriteMask (`StateCache.cs`) | DepthState: enable, comparison, write; static. |
| blendEnabled, blendFunc, blendEnabledIndexed, blendFuncIndexed (`StateCache.cs`) | BlendState: effective per-output enables and independent RGB/alpha factors; static. Global values become derived uniform assertions, never independently restored state. |
| colorMask (`StateCache.cs`) | BlendState: per-output color write masks; static. Add indexed tracking/query/application because global ColorMask overwrites every supported output. |
| cullFaceEnabled, scissorTestEnabled, lineWidth, pointSize (`StateCache.cs`) | RasterizerState: cull/scissor enables and static line width/point size. No present consumer needs a dynamic declaration for the latter two. |
| provokingVertex (`StateCache.Patches.cs`) | RasterizerState: static provoking convention, following parent rasterizer contract. |
| patchVertices (`StateCache.Patches.cs`) | PrimitiveAssemblyState: static control-point count. |
| New viewport | DynamicDrawState: one integer rectangle, supplied by target/pass dimensions; excluded from pipeline identity. Scissor rectangle is dynamic when a later consumer requires it. |

Corresponding cache knowledge is separate field flags and per-output flags; a known comparison
does not imply known depth enable/write. Blend uniform assertions require every supported index
known and equal. Arrays belong to the cache; snapshot/descriptor publication deep-copies them.
Invalidation clears only knowledge, not snapshots or native state. Existing legacy snapshot fields
and scissor restore descriptor are copies, not additional authoritative cache fields; migrate them
to category values/coverage when their scopes migrate. Capability limits, counters, PixelPackState,
program/VAO/FBO/texture/sampler/buffer/image/feedback bindings and retirement associations remain
with their existing owners. Clear-color helper state gets a focused resource-operation value/known
flag in StateCache, not a static graphics category. No unsupported stencil/sampling state is added.

### Boundary API, coverage and failures

Chosen contract: immutable `EngineBoundaryDeclaration` composed from
`PipelineStateCoverage.From(desc)`, dynamic declarations and named helper effects;
`StateCache.TryBeginEngineBoundary(declaration, out scope)` resolves required incoming values
before drawing-state mutations. `EngineBoundaryScope` is a sealed, exactly-once disposable owner
of an internal `PipelineStateSnapshot` (category values, explicit coverage, context token).
It is not a PSO, has no pipeline key and cannot alias mutable indexed cache payloads.
Public VGE draw code uses PSO Apply or declared dynamic application. StateCache is the sole
native query/transition/restoration owner. No renderer stores a parallel list of pipeline values.

Effective coverage is the union of participating descriptor intents and helper effects, closed
over aliases: global blend enable/factors and color masks expand to every MaxDrawBuffers index,
not the temporary target's attachments. FBO-stored blend-policy effects are included when present.
Ordinary composite additionally unions ResolvePipeline/reduction declarations. The capture's
first required set is depth enable/write, cull/scissor enable, per-output blend enables and masks,
viewport and clear color; factors are included whenever helper/cleanup declarations can modify
them. Unchanged depth comparison, equations and static widths do not require preservation just
because the old helper captured them. Every managed mutation checks its affected set against
active coverage before issuing GL, including clear helpers and engine adapters during cleanup.
An unsupported managed mutation fails before native execution. Entry query failure returns no
scope/no draw; it may update truthful knowledge but cannot change native state or publish capture.

Non-nesting is enforced per active context/cache. A second Begin fails before mutation. Dispose
marks the scope consumed before cleanup so repeated disposal cannot replay restoration. Cleanup
attempts all independent owners even if one fails: shader ownership, resource binding scopes,
independent read/draw FBO bindings, final pipeline/dynamic/helper restoration. Aggregate cleanup
failures with the operation error; do not let a later error erase an earlier restoration failure.
Native transition failure makes affected knowledge unknown and throws `EngineBoundaryRestoreException`.
The optional BeforeOverlay catch may swallow operation failure only after successful cleanup;
it must propagate restoration/context failure (including inside aggregates). Publication is withdrawn
on either failure. Ordinary callback restoration failures also propagate. No catch may label a
failed restore safe continuation. Unknown current cache values are restored without draw-time
queries; warm equal values suppress native calls. Saved snapshots survive selective invalidation.

Viewport observation adds the exact `GL.Viewport(int,int,int,int)` signature used by installed
`ClientPlatformWindows.GlViewport`; rectangle convenience overloads delegate to the same cache
owner in VGE. Unsupported float/indexed viewport overloads are excluded until inventoried;
if discovered in supported engine targets, require an exact adapter or explicit invalidation
before relying on cached viewport. Indexed color masks require `GL.ColorMask(int,bool,bool,bool,bool)`
and matching observation where engine IL uses it. ClearColor's four-float signature also routes
through cache for warm helper-state knowledge. Queries occur at boundaries only for missing values.

### Context identity and recovery

Use OpenTK's GLFW current-window pointer (`GLFW.GetCurrentContext()`) plus the reference identity
of the engine's `GameWindowNative` owner and a monotonically allocated registration generation.
Installed `ClientPlatformWindows.window` owns GameWindowNative (OpenTK GameWindow); its constructor
uses NativeWindow.Context. Installed OpenTK 4.9.4 exposes GetCurrentContext, MakeContextCurrent
and DestroyWindow. Do not use `GlExtensions.TryGetContextKey`: vendor/version/renderer strings
are device characteristics and can coincide across replacement contexts.

Before the first routed engine state call, register the actual owner/current handle through the
engine adapter's focused provider. Hooks installed in StartPre can run during menu rendering before
StartClientSide; renderer initialization alone is too late. Later initialization reuses the registration;
headless fixtures register their own NativeWindow owner. Retire registration on owner disposal,
and allocate a new generation when initialization supplies a different owner/context. At boundary
entry and exit require the registered live owner and current handle to match; missing registration,
zero handle, changed handle or disposed owner rejects entry. A context switch makes cache knowledge
unknown; returning does not revive old knowledge. GpuSupport refreshes immutable capabilities when its
registered context identity changes, without resetting diagnostic totals. Mismatch at exit consumes the snapshot without
issuing GL into the replacement context, invalidates old knowledge and propagates restore failure.
Resources must follow existing shutdown/reinitialization ownership; this mechanism never deletes
old-context names in a new context or attempts automatic resource recreation. Same-owner context
recreation is unsupported unless explicit retirement/re-registration advances generation first.
World leave, resize and shader reload are not context replacement and must not advance generation.
This conservative policy defines recovery without claiming arbitrary context-switch support.

### Deterministic reference cases and evidence boundaries

For subsequent unit/headless verification use engine-like depth enable/write and mixed output
blending (0–2 enabled; metadata outputs disabled), nonuniform masks/factors and hostile viewport.
Record actual native state before/after, cache agreement, boundary queries/native call counts,
and compare lighting/composite images with identical inputs on the compatibility reference and
migrated paths. Fix camera matrices, textures, options and dimensions. Compare floating outputs
with format-appropriate tolerances and exact publication/depth/marker decisions; retain clean
world pair and follow capture with the real negative-marker first-person draw. Exercise cold,
warm, partially unknown, invalidated, first-use, repeat, odd-size resize, reload, disabled/dry,
failed allocation/readiness/activation, thrown draw, cleanup failure, nesting, double Dispose and
context mismatch/re-registration. Test ordinary composite with both SSAO restoration and reduction
enabled; isolated capture must not run ordinary display/GI paths. Prove no draw after failed entry,
no aliasing snapshot arrays, truthful unknown state after failures and unaffected binding retirement.

Existing supplied RenderDoc investigation is defect evidence only: events 5989 versus 6244/6343
show metadata blending changed; normal alpha 5 covers 81,320 held-item pixels. Numeric outputs
remain in `artifacts/refraction-cutoff/numeric.txt`; capture source is
`C:/Users/Sisco/Desktop/refraction-cutoff.rdc`. Fresh read-only installed IL inspection confirms
the supported entry/helper signatures. No build or graphics test is needed to establish this
inventory; no runtime code changed. All later builds/tests must be delegated. Live fix acceptance
and performance measurements remain pending; never launch the game. UV cutoff remains separate.

### Proposal traceability

| Restoration proposal section | Plan work / inventory evidence |
| --- | --- |
| Problem and Architectural contract | Historical capture establishes the defect; FullscreenBoundary and actual capture/callback marker tests establish automated correction, not live appearance. |
| Categorized state representation / Source organization | Complete field table; categorized values and separate knowledge partials; CategorizedStateCacheTests and PipelineStateCoverageTests. Resource owners remain separate. |
| Proposed boundary mechanism (entry/application/global-indexed) | BoundaryEntry, PipelineStateSnapshot and BoundaryRestoration; EngineBoundaryEntryTests and EngineBoundaryRestorationTests cover resolved values, alias closure, invalidation, context and failures. |
| Dynamic state, bindings, shader ownership | StateCache.Dynamic, EngineBoundaryScope and BoundaryBindings; EngineBoundaryBindingTests, GpuProgramUseScopeTests and real callback/viewport/FBO regressions. |
| Authority, invalidation, lifetime | RenderContextRegistry, boundary context validation, ExecuteExternal and targeted resource retirement; context, external-mutation, deletion and unaffected-binding regressions. |
| Refraction integration and compatibility | One capture boundary and independent lighting/composite adapters; actual marker/publication regressions; all 11 retained source invocations assigned parent prerequisites above. |
| Relationship to approved PSO work | Parent exception governs this bounded work; future command context reuses cache mechanism, complete pipeline adoption remains parent work. |
| Verification and acceptance | Baseline 213 passing tests plus 97 fresh affected tests after the startup correction; delegated builds and measured fixture counters below. User live confirmation received on 2026-10-05; no new RenderDoc inspection claimed. |

The approved parent architecture's ownership, static/dynamic policy, engine restoration and context
generation requirements govern these decisions. Complete descriptors, target signatures, preparation
and full submission are future parent-plan obligations, not inventory completion criteria here.

### Categorized storage and cache-backed coverage

The fixed-function foundation now lives in `Rendering/Pipeline/State/`: `DepthState`,
`BlendState` (one draw output), `RasterizerState`, `PrimitiveAssemblyState` and
`DynamicDrawState`. `StateCache.FixedFunctionStorage.cs` owns their values and separate
field/index knowledge, represented by byte-backed `[Flags]` enums. Knowledge checks use .NET
`HasFlag`; setting and clearing use enum bitwise operators, preserving unrelated flags. The former nullable fixed-function fields are removed, including the
patch/provoking fields and the scissor preservation reader. Bindings, VAO element associations,
resource retirement and shader ownership retain their existing implementations.

`StateCache.Blending.cs` uses the current context's `MaxDrawBuffers`, independently of bound
framebuffer routing. Global operations establish every output; indexed operations update only
that output. A global operation is suppressed only when every affected output is known and equal.
Enables, factors and masks have independent validity. `CopyBlendValues` copies an array of value
structs, so subsequent live cache mutation cannot alter the copy. It does not implicitly resolve
unknown fields or provide a usable boundary snapshot; explicit snapshot coverage remains required.

`StateCache.Dynamic.cs` applies integer viewports outside static pipeline identity, normalizing
sizes to cached implementation limits. `GpuFramebuffer.BindWithViewport`, existing VGE viewport
calls and legacy viewport cleanup use this owner. Routing the remaining known VGE callers is
necessary to prevent the newly cached state from becoming stale; it does not migrate their
restoration contracts. `StateCache.ClearOperations.cs` tracks the native clear-color value as
resource-operation state. Existing VGE clear-color calls and exact engine signatures now use it.
`EngineStateCalls` adds the integer viewport, indexed mask and four-float clear-color mappings;
the existing startup discovery performs observation without a second native call or recursive hook.

`RenderContextRegistry` combines the current GLFW pointer, weak owner identity and monotonically
allocated registration generation. `EngineRenderContext` registers the actual
`ScreenManager.Platform.window` at renderer initialization after checking its pointer is current.
`RenderContextLifetimeHook` retires that owner before `NativeWindow.Dispose(bool)`; owner liveness
is checked as an additional fallback. World leave, resize and shader reload do not retire it.
The existing headless fixture owns a raw GLFW window rather than an OpenTK NativeWindow wrapper,
so it registers the fixture as that window's owner and explicitly retires before destruction.
This preserves the same pointer/owner/generation contract without replacing the established fixture.

Context changes withdraw mutable knowledge. GpuSupport owns draw-buffer, viewport, patch, binding-slot
and buffer-alignment limits and refreshes them when its registered context generation changes. A
temporary detach and return to the same live context can reuse immutable capabilities; mutable state
remains unknown. Missing registration also prevents reuse of authoritative knowledge; future
boundary entry must reject it. Resource names are neither deleted nor recreated by this mechanism.
Scalar setters reject invalid enum/size inputs before native mutation and publish knowledge only
after the native call returns. Native restoration failure handling is supplied by the new scoped
mechanism below, not by the retained legacy scopes.

The installed 1.22.7 engine and OpenTK 4.9.4 metadata were checked again for the public platform/window
fields and the protected `NativeWindow.Dispose(bool)` signature. Automated evidence does not launch
Vintage Story or establish live refraction correctness. Declared boundary entry is recorded below;
restoration is documented below and production refraction adapter integration remains pending.

| Restoration plan task group | Controlling source | Implementation and verification |
| --- | --- | --- |
| Complete categorized storage, separate knowledge, copies | Restoration proposal / Categorized state representation; inventory / State representation and source layout decisions | Category value files, StateCache storage and scalar/patch/scissor partials; categorized native-state and invalidation tests. |
| Global/indexed aliases and supported output extent | Restoration proposal / Global and indexed state | StateCache.Blending; native mixed enables/factors/masks, highest supported index, independent copies, suppression and MRT rendered output tests. |
| Viewport and clear helper coverage | Restoration proposal / Dynamic state, bindings, and shader ownership; inventory effect matrix | StateCache dynamic/clear owners, engine mappings, framebuffer and existing caller routing; native viewport/clear values, clamping, repeated calls and selective validity tests. |
| Context knowledge and capability lifetime | Parent architecture / Engine integration and cache authority; inventory / Context identity and recovery | Context registry, engine initialization/retirement and fixture lifecycle; actual context switch/replacement, missing/dead registration, re-registration and capability refresh tests. |
| Unaffected resource ownership and transition behavior | Restoration proposal / Authority, invalidation, and lifetime; parent architecture / resource retirement | Existing engine-state, resource-deletion, VAO/buffer, framebuffer, unbind and scissor regressions retained. |

Delegated Release validation passed on 2026-10-05: 59 tests, zero failures or skips; build
completed with zero errors and 101 warnings. The run includes the water-refraction
compatibility regression and successful installation/removal of the native-window disposal hook.
Shader build receipts remained enabled. Commands used the installed package cache via
`NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests' --logger 'trx;LogFileName=phase2-state-validation.trx'`

Receipts: `artifacts/phase2-build-validation.log` and
`VanillaGraphicsExpanded.Tests/TestResults/phase2-state-validation.trx`.
Tests measure zero additional native calls for repeated known scalar/global/indexed/dynamic/helper
operations, and reapplication only for selectively invalidated scalar fields. Capability resolution
is repeated after a context switch. These are operation-count checks, not CPU/GPU speedup claims.

The second source review retained Debug PSO labels, migrated the old scissor reader, narrowed legacy
invalidation to its existing footprint, routed remaining viewport/clear mutations, and added invalid
input checks. The separate audit-stage-completion pass reconciled storage, transitions, context
lifetime, exact engine signatures, copy independence and resource regression evidence against the
restoration proposal and parent architecture. Actual boundary capture/cleanup guarantees belong to
the subsequent snapshot/restoration work; the plan now states that dependency explicitly for clear
color. No scope has been removed and the original mixed-state refraction restoration defect is not
claimed fixed by this foundation.

Delegated Debug compilation also passed (zero errors, 106 warnings), including the retained PSO
debug-group branch: `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj
-c Debug --no-restore -v quiet`, with the same package-cache environment. Receipt:
`artifacts/phase2-debug-build-validation.log`. Final source review and the independent contract audit
found no remaining foundational coverage issues. The later entry work is recorded below;
production consumer migration and live acceptance remain open.

### Declared boundary entry and resolved snapshots

`Rendering/Pipeline/PipelineStateCoverage.cs` derives field coverage from both descriptor intent
masks and copies indexed output identifiers. Declarations union that coverage with explicit dynamic
and helper effects in `Rendering/Integration/EngineBoundaryDeclaration.cs`. They retain no pipeline
values, executable identity or resource ownership. Global blend enable/factors and write-mask effects
cover all supported draw outputs. Indexed declarations are validated against the context limit,
independently of framebuffer attachments. Category flag enums identify covered fields here; their
values describe the contract, not a copy of the cache's current validity flags.

`StateCache.BoundaryEntry.cs` resolves missing covered state before publishing a token. It requires
a live registered context and checks the handle/generation again after resolution. Nested entry,
including reentrant entry during resolution, is rejected. Missing registration, unsupported output
indices and query failures return no scope, retain a diagnostic exception and issue no drawing-state
transition. Earlier successful queries may leave truthful partial knowledge; no default is substituted
for a failed read. Native error status is checked around cold reads and may be consumed on rejected
entry; this does not change native drawing state.

`StateCache.BoundaryQueries.cs` owns those reads. Depth, rasterizer, assembly, viewport, clear color,
and each output's enable/factors/mask are independently resolved only when required and unknown.
Factor knowledge is published after all four component queries succeed. Viewport and patch limits
are resolved at entry when needed, preventing first-use capability queries in managed draws.
`PipelineStateSnapshot` copies category values, retains explicit immutable coverage and the context
token, and privately clones indexed storage. Its output accessor returns a value copy. Uncovered
fields are not captured values even when they share a category struct with covered fields.

`StateCache.BoundaryValidation.cs` enforces coverage before setters issue native operations or
suppress identical known requests. `Apply` checks the whole descriptor before its first transition;
an undeclared later field cannot leave earlier pipeline fields changed. Scalar/global/indexed backend
setters, dynamic application, clear-color helpers and the engine adapters use the same guard.
Unsupported capability/patch forwarding is rejected while a boundary is active. Legacy capture and
unknown patch/provoking getters cannot introduce nested capture or draw-time state queries.
Resource-binding owners remain separate; this work does not claim a complete borrowed-binding handoff.

The initial `EngineBoundaryScope` entry token is now extended by the disposable restoration mechanism
documented below. `ReleaseEngineBoundary` remains a cache-owner primitive which only releases active
registration; entry-only tests use it, while restoration owners call it after cleanup. Production
renderers had not migrated when entry validation was recorded. The subsequent integration and
headless refraction correction are documented below; unrelated legacy consumers remain in place.

| Restoration plan task group | Controlling source | Implementation and evidence |
| --- | --- | --- |
| Descriptor/dynamic/helper coverage and global aliases | Restoration proposal / Boundary declaration and entry; Global and indexed state; inventory / Boundary API, coverage and failures | PipelineStateCoverage, EngineBoundaryDeclaration; PipelineStateCoverageTests verify both intent masks, all descriptor fields, alias containment and copied declaration payloads. |
| Complete resolved incoming snapshot, selective queries and no defaults | Restoration proposal / Categorized state representation; Boundary declaration and entry | BoundaryEntry/BoundaryQueries and PipelineStateSnapshot; EngineBoundaryEntryTests verify cold/warm/partial depth entry, all categories, mixed indexed values, query-error rejection and native-state agreement. |
| Independent indexed ownership and explicit coverage | Restoration proposal / Categorized state representation; parent architecture / Complete descriptions and partial overrides | Private cloned snapshot payload; tests mutate live arrays through global setters, invalidate knowledge and verify retained mixed values; narrow coverage leaves unrelated depth fields unknown. |
| Context-bound entry and nesting | Restoration proposal / Authority, invalidation, and lifetime; inventory / Context identity and recovery | Registered-context checks and active/resolving guards; tests cover missing current context, absent registration, replacement generation and nested entry. |
| Validate every supported drawing-state mutation before native work | Restoration proposal / Architectural contract; Authority, invalidation, and lifetime; parent architecture / Engine integration and cache authority | BoundaryValidation and shared setter guards; tests reject whole PSOs, global aliases, unlisted outputs, unsupported capabilities and identical known out-of-contract requests without native transitions. |

Operation counts distinguish state/capability value reads from the native error-status checks around
each cold read. With output capability already cached, complete cold depth entry performs three value
reads, warm re-entry performs zero, and invalidating depth then reestablishing only comparison leaves
two reads. Cold enable/factors/mask capture performs six value reads per supported output. Entry issues
zero drawing-state transitions. Covered viewport/patch application adds no capability or state reads.
These are deterministic operation-count assertions, not CPU/GPU performance measurements.

Delegated validation passed on 2026-10-05: Release build completed with zero errors and 101 warnings;
76 focused tests passed with zero failures or skips. This includes the 17 new coverage/entry cases
and the existing categorized state, invalidation, engine mapping, framebuffer/blending, resource
retirement, unbinding, scissor and refraction compatibility regressions. The real failed-query case
checks rejection of a GL query's default return when it also reports a native error; the separate
entry-failure case proves no token or drawing-state transition is published.

Commands used `NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`, with shader receipts enabled:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EngineBoundaryEntryTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests' --logger 'trx;LogFileName=phase3-state-validation.trx'`
- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet`

Receipts: `artifacts/phase3-build-validation.log`, `artifacts/phase3-test-validation.log`, and
`VanillaGraphicsExpanded.Tests/TestResults/phase3-state-validation.trx`. The initial Debug build
encountered a shader-cache atomic-write access failure, recorded in
`artifacts/phase3-debug-build-validation.log`. A retry passed with zero errors and 106 warnings,
without source or shader-setting changes: `artifacts/phase3-debug-build-retry.log`.

The second source review checked all native mutation sites, separated validation from entry
orchestration, and added explicit registration/replacement and real native query-failure evidence.
The independent audit-stage-completion pass reconsulted the restoration contract, parent authority
rules and inventory decisions, then reconciled each entry requirement against source and final
receipts. No required entry/snapshot finding remains. Restoration evidence follows below; production
consumer adapters and live acceptance remain pending. No game was launched.

### Scoped restoration, borrowed bindings and failure guarantees

`EngineBoundaryScope` now implements exactly-once disposal. Its `Run` method retains the operation
exception, consumes the scope before cleanup, and attempts registered shader scopes, borrowed bindings,
independent framebuffer scopes, then fixed-function/dynamic/helper restoration in that order. Cleanup
within each owner group is reversed. Operation code cannot dispose the boundary early or nest `Run`.
Adapters register existing `GpuProgram.UseScope()` owners with `AddCleanup`; registration transfers
cleanup responsibility to the boundary, not ownership of the underlying shader or resources.

`StateCache.BoundaryRestoration.cs` restores only snapshot coverage through existing setters. It
compares valid current knowledge, reapplies unknown values without queries, and restores mixed enables,
factors and masks by output index without a trailing global overwrite. It never uses blanket
invalidation to force restoration. Native errors and thrown transitions leave the affected field
unknown and are collected while independent fields continue. Context identity/generation is checked
before operation and cleanup owners. A mismatch consumes the boundary, discards obsolete knowledge
through the existing context owner, and issues no old-context cleanup into the replacement context.

`EngineBoundaryRestoreException` distinguishes an unsafe handoff from an ordinary operation failure.
`Run` aggregates both when cleanup fails; it rethrows the original operation exception when cleanup
succeeds. The existing shader `UseScope` owner now also retains failed activation and failed rollback
together. `ShaderOwnershipRestoreException` identifies that failure even when it occurs before a
shader scope can be registered. `IsRestorationFailure` recognizes both kinds inside aggregates.
Binding APIs retain compatibility behavior outside boundaries but no longer swallow native exceptions
under active entry/restoration authority. Failed shader cleanup withdraws program knowledge.

`Rendering/Integration/EngineBoundaryResources.cs` derives active texture/sampler, image and indexed
buffer footprints from `GpuPreparedBindings`, unions participating/helper effects and copies payloads.
`EngineBoundaryExecution.TryRun` includes the incoming prepared VGE shader footprint. It rejects
foreign engine owners, unowned raw/compute programs and incoming owners requiring preparation before
optional work; those paths need an independently verified adapter before they can be supported.
Shader preparation and footprint declaration precede entry. Arbitrary callbacks or shader reloads
which expand the declared footprint mid-operation are not supported.

`StateCache.BoundaryBindings.cs` resolves missing slots in the existing binding cache. It captures all
image-view parameters and indexed buffer offsets/sizes, preserves generic buffer aliases after indexed
restoration, restores the active texture unit, incoming VAO and generic array buffer, and reuses separate
read/draw `FramebufferScope` owners. VAO-owned element-buffer associations are not rewritten. Texture
queries may temporarily select another unit; entry restores that selector in a checked finally block.
A selector-restoration failure is surfaced as a failed handoff, never reported as unchanged optional
entry. Unsupported slot limits fail before cache-array allocation or native selection, using the
GpuSupport capability snapshot rather than a separate StateCache limit cache. Unknown incoming
buffer bindings preserve the queried effective range; resizing borrowed storage during the interruption
is outside the supported resource contract.

Borrowed snapshots hold copied binding values, not a second live cache or resource ownership. Existing
tracked deletion paths record retired names for the active boundary. Cleanup rejects those names even
if a numeric name could subsequently be reused. Resource deletion, deferred retirement and unrelated
binding knowledge retain their existing owners. No renderer performs native capture or manual restore.
`ExecuteExternal` requires managed boundaries to have ended and invalidates only its declared affected
categories in a finally block, including when external work throws.

| Restoration contract item | Controlling source | Implementation and verification |
| --- | --- | --- |
| Effective-set restoration, mixed aliases, invalidation and no-op suppression | Restoration proposal / Application and restoration; Global and indexed state | BoundaryRestoration; native mixed output and all-category restoration tests, with and without invalidated knowledge. |
| Ordered, exactly-once cleanup and retained operation errors | Restoration proposal / Dynamic state, bindings, and shader ownership; inventory / Boundary API, coverage and failures | EngineBoundaryScope, cleanup ordering, real UseScope ownership, draw/setup failure, multiple-owner failure and repeat-disposal tests. |
| Declared prepared footprint, bindings and retirement | Inventory / Complete operation and effect matrix; parent architecture / resource ownership and engine integration | EngineBoundaryResources, BoundaryBindings and existing binding/deletion owners; native texture/sampler/image/UBO/SSBO, active unit, geometry, independent FBO and retired-name tests. |
| Context mismatch and truthful failure state | Restoration proposal / Authority, invalidation, and lifetime; inventory / Context identity and recovery | Per-owner context checks, checked transitions and targeted unknown flags; context replacement, native error and real shader rollback failure tests. |
| Explicit unknown/external boundaries | Restoration proposal / Authority, invalidation, and lifetime; parent architecture / Submission contract | ExecuteExternal and conservative foreign-owner rejection; active-boundary rejection, targeted finally invalidation and no-operation tests. |

Delegated validation passed on 2026-10-05: Release build had zero errors and 107 warnings; Debug had
zero errors and 106 warnings. All 105 focused cases passed, with zero failures or skips. The suite
includes entry, coverage, restoration/binding tests and existing cache, engine mapping, resource
retirement, framebuffer, shader ownership, generated resource/image binding and refraction regressions.
Shader build receipts stayed enabled and no game was launched. Commands used
`NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EngineBoundary|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests|FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests' --logger 'trx;LogFileName=phase4-state-validation.trx'`
- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet`

Receipts: `artifacts/phase4-build-validation.log`, `artifacts/phase4-test-validation.log`,
`artifacts/phase4-debug-build-validation.log`, and
`VanillaGraphicsExpanded.Tests/TestResults/phase4-state-validation.trx`.

Measured counters distinguish state/capability value reads, state transitions, resource binds and
error-status polling. Cold depth entry uses three value reads; partially known entry uses two;
unchanged warm entry/restoration uses zero value reads and zero fixed-function transitions, with six
native error-status checks for its three restored fields. Warm borrowed-slot restoration adds no
texture/image/indexed-buffer binds in the tested footprint. These counts do not establish CPU/GPU
speedups or live appearance. The second source review addressed failed shader activation/rollback
exception preservation and late context invalidation. The independent completion audit reconciled
all restoration tasks against the linked proposal, parent architecture, inventory and final receipts;
no required mechanism finding remains. Production integration is recorded below; live acceptance
remains pending.

### Shared capability ownership

StateCache retains mutable GPU state only. All implementation limits it consumes come from
GpuSupport: output counts, viewport dimensions, patch size, texture/image/indexed-buffer slot counts
and shader-storage buffer range alignment. GpuSupport.EnsureCurrentContext uses the registered
handle/generation for warm reads without native string or limit polling. Replacement generations
refresh the shared snapshot; state invalidation does not. Capability capture counts belong to
GpuSupport.CaptureCount, while StateCache.BoundaryQueries counts mutable state reads.

Image limits use GL_MAX_IMAGE_UNITS and GL_MAX_COMBINED_IMAGE_UNIFORMS rather than texture-unit
limits. GpuSupportLimitsTests checks the shared values against native queries and verifies warm
reuse without consuming pending native errors. CategorizedStateCacheTests verifies same-lifetime
reuse and capability refresh after explicit context retirement/re-registration.

### Refraction and fullscreen callback integration

FullscreenBoundary composes descriptor-derived coverage with viewport/clear helper effects and
prepared shader footprints, including texture unit zero used by allocation helpers. The capture
adapter resolves one boundary before target allocation and runs direct lighting and pre-overlay
composition inside it. The shared draw methods require the active scope. Their independently
registered callbacks establish separate boundaries; ordinary composite includes display resolve,
SSAO restoration and receiver reduction. Shader readiness is established before footprint capture;
the shared draw methods do not compile variants inside the boundary.

The duplicate CapturePipeline and the three legacy snapshots have been removed from these entry
points. Capture and composite no longer invalidate all cache knowledge: supported native changes
are observed by the existing owners and engine adapters, while boundary snapshots restore the
actual mixed indexed state. Other legacy consumers retain their existing contracts for their own
migration review. Resource allocation, resize, borrowing and disposal remain with existing target
owners. Standalone preparation and standalone SSAO callers retain their distinct binding scopes.

EngineBoundaryScope.Activate registers the existing UseScope owner for ordered cleanup after all
sequential passes. It does not introduce another shader activation implementation. This preserves
both operation and cleanup exceptions before borrowed slots/framebuffers and drawing state are
restored. Capture and ordinary composite withdraw publication on an escaping error; optional
allocation/reduction and BeforeOverlay catches cannot swallow classified restoration failures.
Entry rejection skips optional capture without drawing; ordinary callbacks surface unsupported
entry contracts. Shared work and outer callbacks reject nested boundaries.

Incoming active foreign engine owners and unowned raw/compute programs remain rejected because
this adapter cannot establish their resource reactivation footprint. Headless fixtures model engine
Stop observation with targeted program-cache invalidation; they do not force an extra native unbind
to satisfy boundary entry.

| Integration task group | Controlling source | Implementation and evidence |
| --- | --- | --- |
| One capture boundary and independent callbacks | Restoration proposal / Refraction integration and compatibility; inventory / Entry points and Complete operation and effect matrix | FullscreenBoundary, shared lighting/composite methods and capture adapter; actual callback/capture GPU regressions. |
| Preparation, helpers, shader cleanup and coverage | Restoration proposal / Boundary declaration and entry; Dynamic state, bindings, and shader ownership; parent proposal / Submission contract | Prepared resource unions, descriptor-derived coverage, deferred UseScope ownership, SSAO and reduction composition; native viewport and independent framebuffer checks. |
| Publication, failure and resource lifetime | Restoration proposal / Application and restoration; Authority, invalidation, and lifetime; inventory / Boundary API, coverage and failures | Capture/composite withdrawal and restoration-failure filters; existing scene/target owners; rejection, draw failure, resize, reload, toggle and teardown regressions. |
| Mixed-index corruption and coherent world publication | Restoration proposal / Problem and evidence; Verification and acceptance; inventory / Deterministic reference cases | Legacy negative control and real MRT marker rasterization followed by actual capture/ordinary callbacks; negative normal-alpha and clean world color/depth assertions. |

Final delegated validation passed on 2026-10-05. Release build: zero errors and 107 warnings.
Final Debug incremental build: zero errors and zero warnings (the preceding full Debug build
reported 106 warnings). TRX inspection confirms 213 distinct tests passed, with no failures or skips:
38 boundary/capture/SSAO cases, 71 cache/resource/shader cases and 104 relevant rendering cases.
The marker regression first reproduces nonnegative alpha with the legacy scope, then verifies -1
after the actual capture and restores the coherent world pair through ordinary composition. Cases
also cover first use, repeated frames, actual 3x3 resize, reload, disabled/dry capture, failed entry,
failed draws and retiring the incoming shader during cleanup. SSAO restoration and receiver
reduction execute together through the real ordinary composite callback.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages, with shader receipts enabled:

- dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet
- dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet
- dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter '<selection below>' --logger 'trx;LogFileName=phase5-<group>-validation.trx'

Selections (separate runs, with distinct test IDs):

~~~text
boundary: FullyQualifiedName~EngineBoundary|FullyQualifiedName~WaterRefractionCaptureStateTests|FullyQualifiedName~SceneColorParticlePublicationTests
cache: FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests|FullyQualifiedName~GpuSupportLimitsTests
render: FullyQualifiedName~WaterRefractionLifecycleTests|FullyQualifiedName~WaterRefractionOverlayCompositionTests|FullyQualifiedName~WaterRefractionReductionTests|FullyQualifiedName~WaterRefractionResolutionTests|FullyQualifiedName~PbrCompositeHdrTests|FullyQualifiedName~PbrDirectLighting|FullyQualifiedName~DirectLightingBufferOwnershipTests|FullyQualifiedName~SceneColorParticle&FullyQualifiedName!~SceneColorParticlePublicationTests
~~~

Receipts: artifacts/phase5-build-validation.log, artifacts/phase5-debug-build-validation.log,
artifacts/phase5-{boundary,cache,render}-tests.log and
VanillaGraphicsExpanded.Tests/TestResults/phase5-{boundary,cache,render}-validation.trx.
Earlier commands that tested stale binaries after a failed build were withdrawn; cancelled broader
runs are not completion evidence. Final builds succeeded before their corresponding test runs.

The second source review corrected helper-scope validation, kept disabled-option retirement inside
the callback boundary and verified that real GlPipelineDesc application remains with every draw.
The independent completion audit reconsulted the selected contract, proposal, parent submission and
ownership rules, entry/helper inventory and final source/receipts. All required integration items
are satisfied. Legacy-caller reconciliation and broader PSO adoption retain their own plan gates.
Live appearance, first-person source cleanliness in a new capture and the separate screen-bottom
UV fallback remain outside the automated acceptance claim. No game is launched by this work.

### Legacy reconciliation and future submission ownership

Reconciliation on 2026-10-05 changes documentation only. Actual GlPipelineDesc applications remain
with the draws. GraphicsCommandContext will coordinate the existing boundary mechanism around
sequential complete passes, preserving engine-aware shader cleanup, borrowed bindings/framebuffers,
then StateCache drawing-state restoration. Unknown external mutations require an explicit handoff
and affected-category invalidation; engine hooks do not prove arbitrary raw GL coverage.

Direct-lighting adoption owns removal of its callback compatibility wiring. Composite/capture
adoption owns theirs and the FullscreenBoundary unions once complete submission covers the same
operation. Keep any still-used adapter until its final caller migrates. Partial descriptors remain
restricted to declared compatibility operations outside complete submission or at explicit boundaries.
Viewport remains dynamic; framebuffer blend policy moves with complete consumer pipelines, with
one policy owner throughout. No renderer restore PSO or second cache is introduced.

| Reconciliation task | Controlling source | Evidence |
| --- | --- | --- |
| Caller dispositions and helper retirement | Restoration proposal / Refraction integration and compatibility; original caller inventory | Updated table accounts for three migrated and 11 retained source invocations; parent remaining-consumer tasks own every family and final helper retirement. |
| State ownership, cleanup, unknown mutations and category separation | Restoration proposal / Dynamic state, bindings, and shader ownership; Authority, invalidation, and lifetime; Source organization | StateCache category/knowledge partials, BoundaryValidation, EngineBoundaryScope and FullscreenBoundary source review; existing boundary/cache/callback regressions above. |
| Future adoption and bounded scope | Parent proposal / Submission contract, Engine integration and cache authority, Adoption strategy | Parent submission, direct-lighting and remaining-consumer tasks explicitly own reuse and compatibility removal; proposal and project index agree. |

Second source review reconfirmed the complete fixed-function category inventory: depth, effective
per-output blend/masks, rasterizer, primitive assembly and dynamic viewport. Knowledge stays separate
from reusable values; clear-operation state has its separate owner. Binding caches, capabilities,
context identity and resource lifetimes remain outside fixed-function value structs. The review
corrected the frozen/live bounds inventory discrepancy and checked retained entry mutations and
caller context. No remaining scope has evidence supporting isolated deletion or fullscreen substitution.

No runtime files changed, so the 213 passing tests and Debug/Release receipts above remain applicable.
No new build/test run or live acceptance is claimed for this reconciliation. The completion audit
reconsulted the restoration contract and linked proposal/parent adoption requirements; retained
consumers are explicitly allowed here and remain incomplete under the parent plan. Supplied live
validation and the broader parent completion gates remain outstanding.

### Consolidated acceptance status

Automated acceptance evidence was reconciled on 2026-10-05 against the restoration proposal's
Verification and acceptance items 1–8 and the section mapping above. The delegated receipt review
confirmed that commit 421e64c9's runtime implementation is unchanged by the documentation-only
reconciliation in 79394b44. The three retained TRX receipts contain 213 distinct passing test IDs,
zero failures and zero not-executed tests. Their runs followed the successful Release build.
Release reported zero errors/107 warnings; the final Debug incremental build reported zero errors/
zero warnings. No rerun was needed or claimed. Commands, filters and receipt paths remain recorded
under Refraction and fullscreen callback integration.

Coverage includes mixed global/indexed state and masks, partial knowledge, snapshot independence,
context replacement, unsupported mutation rejection, exactly-once cleanup, setup/draw/shader failure,
actual marker rasterization and publication, independent callbacks, resize/reload, disabled capture,
engine mappings, shader ownership, resource retirement and unaffected borrowed bindings. This is
bounded unit/headless evidence, not an engine-wide authority or installed-build claim.

| Measured fixture operation | Value queries | Native transitions / other calls | Evidence |
| --- | --- | --- | --- |
| Cold depth entry, unchanged restoration | 3 | 0 fixed-function transitions | EngineBoundaryRestorationTests |
| Depth entry with comparison already known | 2 | Queries resolve only missing enable/write values | EngineBoundaryEntryTests, EngineBoundaryRestorationTests |
| Unchanged warm depth entry/restoration | 0 | 0 fixed-function transitions; 6 native error-status checks during restoration | EngineBoundaryRestorationTests |
| Cold enable/factors/write-mask coverage | 6 per supported draw output | Entry emits no fixed-function mutation | EngineBoundaryEntryTests |
| Unchanged warm borrowed-slot footprint | 0 additional | 0 additional texture/resource-slot binds | EngineBoundaryBindingTests |

These are passing counter assertions for named fixtures, not timings or whole-frame totals.
Capability reads have their separate GpuSupport counter. Error polling is not a state-value query
and is not hidden by the zero-query claim. CPU/GPU speedup, representative frame cost and full
submission/preparation measurements have not been established; broader measurements remain in
the parent plan.

The user supplied a startup crash at 11:49:47 PDT on 2026-10-05 in
Vintage Story 1.22.7: the engine's LoadFrameBuffer viewport call reached GpuSupport before context
registration. After the startup correction below, the user confirmed on 2026-10-05: "okay, its all
working". This supplies live acceptance of the corrected build in the current rendering task.
No new RenderDoc capture was supplied: indexed state, negative markers and clean world publication
retain the native/headless evidence recorded above rather than a claimed live texture inspection.
No remaining bottom-cutoff problem was separately reported in this confirmation; its UV-fallback
cause was not isolated and no UV-fallback fix is claimed. Vintage Story was not launched by the agent.

Second review and completion audit reconciled the source/test evidence, proposal section mapping,
caller dispositions and parent prerequisites. Automated consolidation is satisfied. The live-evidence
task and final acceptance gate are now satisfied by the supplied confirmation together with the
automated evidence. This completes the bounded restoration plan, not the parent PSO migration.

### Early engine context registration correction

StartPre installs engine state hooks before StartClientSide registers the render context. Menu
rendering can call LoadFrameBuffer between these events. Previously its routed Viewport command
requested shared viewport limits with no registered context and threw before rendering.

EngineStateCalls now obtains its cache through one engine-adapter accessor. If current registration
is absent, it registers the actual ScreenManager platform window through EngineRenderContext before
using the cache. The provider still verifies the current native handle and live window owner. Existing
registrations are reused; StartClientSide registration stays idempotent and disposal retains the
existing retirement hook. GpuSupport remains the capability owner, and direct boundary entry still
rejects unregistered contexts. No device-string identity, fabricated owner or unchecked fallback is added.

EngineStartupContextTests reproduces the reported exception through the former direct ApplyDynamic
path, then invokes the real engine Viewport adapter against an unregistered native fixture context
and an engine window owner. It checks native viewport values, idempotent registration, warm capability
and transition suppression, retirement/new generation, wrong-window rejection and existing context
operation with no engine platform. Test-owned wrappers borrow the fixture context and cannot destroy it.

The second source review checked every engine adapter's cache access, provider handle/liveness guards,
disposal retirement, unchanged direct boundary rejection and test cleanup. The correction adds no
native drawing-state restoration outside StateCache. The earlier 213-test receipts remain historical
baseline evidence; fresh affected validation is recorded below. Subsequent user confirmation supplies
successful live acceptance as recorded above.

Fresh delegated validation passed: Debug test-project build, zero errors/101 warnings; Release
production build with shader compilation enabled, zero errors/6 warnings. The three new TRX files
contain 97 distinct passing test IDs, zero failures/skips: 84 startup/engine/cache/boundary/publication
cases, four capture cases and nine shader ownership/generated-resource/image-binding cases.
Receipts: artifacts/startup-context-debug-build.log, artifacts/startup-context-release-build.log,
artifacts/startup-context-{focused,refraction,resources}.log and
TestResults/startup-context-{focused,refraction,resources}.trx. The final Debug build includes the
completed test cleanup changes; preliminary receipts are not substituted for these results.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages with no shader-build overrides:

~~~text
dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore
dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Release --no-restore
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter '<selection>' --logger 'trx;LogFileName=startup-context-<group>.trx' --results-directory TestResults

focused: FullyQualifiedName~EngineStartupContextTests|FullyQualifiedName~EngineState|FullyQualifiedName~EngineBoundary|FullyQualifiedName~Categorized|FullyQualifiedName~GpuSupport|FullyQualifiedName~StateCacheResourceDeletion|FullyQualifiedName~ShaderScope|FullyQualifiedName~ShaderUniformState|FullyQualifiedName~GlStateCacheInvalidation|FullyQualifiedName~SceneColorParticlePublication
refraction: FullyQualifiedName~WaterRefractionCaptureStateTests
resources: FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests
~~~
