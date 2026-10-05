# Authoritative pipeline state implementation evidence

## Engine-boundary restoration

Inventory and implementation contracts established on 2026-10-05. Runtime implementation
and fix acceptance remain pending. This section implements the inventory contract in
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

Repository search found 14 invocations plus the declaration. Each retained family needs its own
effect inventory and regressions before migration; presence of observed engine calls is insufficient.

| Source and invocation lines at inventory | Disposition / dependency |
| --- | --- |
| `PBR/Liquids/WaterRefractionCapture.cs:100` | Replace by pre-overlay boundary; remove duplicate CapturePipeline after child draws establish state. |
| `PBR/DirectLightingRenderer.cs:98` | Redundant inside capture; ordinary callback still requires its independent adapter. Separate adapter from shared draw implementation first. |
| `PBR/PBRCompositeRenderer.cs:189` | Redundant inside capture; ordinary adapter must include preparation, SSAO restoration and reduction before removal. |
| `DebugView/Views/VgeWorldCellBoundsDebugView.cs:285` | Retain independent debug boundary; line geometry, shader and binding policy require parent debug-consumer migration. |
| `DebugView/Views/VgeGBufferOverlayDebugView.cs:157` | Retain independent overlay boundary; indexed outputs and engine blit ownership require debug adapter. |
| `LumOn/LumOnDebugRenderer.cs:730` | OIT debug group contains calls into bounds/rays/orbs; possible nested scopes. Retain until shared outer debug contract and child effects are established. |
| `LumOn/LumOnDebugRenderer.cs:893` | Independent AfterBlit/debug fullscreen path; manual viewport/scissor and active-texture restoration must migrate together. |
| `LumOn/LumOnDebugRenderer.cs:1345` | Bounds helper can run under OIT group; retain until parent adapter removes nested preservation and covers line width/VAO/raw mutations. |
| `LumOn/LumOnDebugRenderer.cs:2562` | Independent normal-depth atlas overlay, engine blit program and sampler inputs; retain pending debug shader/binding contract. |
| `LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs:229` | Resolve operation with multiple draws; retain until upload/resolve adapter covers target/viewport/geometry and shader cleanup. |
| `PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs:146,508,592` | Three offscreen bake/solver entry scopes; retain until allocation/viewport/scissor/program/binding and iterative solver contracts are independently migrated. |
| `PBR/Liquids/WaterVolumeRenderer.cs:108` | Independent liquid pool boundary; viewport, indexed additive blending, UseSsbo bookkeeping and terrain helper effects require volume-specific adapter. |

No retained caller is approved for blanket removal. The legacy helper cannot be retired while
these consumers require it. Their current scopes are compatibility fallbacks, not proof of complete
indexed preservation or context safety.

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

At renderer initialization register the actual owner/current handle through a focused provider;
headless fixtures register their own NativeWindow owner. Retire registration on owner disposal,
and allocate a new generation when initialization supplies a different owner/context. At boundary
entry and exit require the registered live owner and current handle to match; missing registration,
zero handle, changed handle or disposed owner rejects entry. A context switch makes cache knowledge
unknown; returning does not revive old knowledge. Context identity changes also reset cached
capabilities, without resetting diagnostic totals. Mismatch at exit consumes the snapshot without
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
| Problem and Architectural contract | Inventory and entry/effect matrix above; storage/coverage work and adapter integration follow. |
| Categorized state representation / Source organization | Complete field table and layout decisions above; categorized migration and regression gate follow. |
| Proposed boundary mechanism (entry/application/global-indexed) | API, alias closure and failure decisions above; snapshot and restoration implementation gates follow. |
| Dynamic state, bindings, shader ownership | Effect matrix and cleanup ordering above; cache coverage and integration tests follow. |
| Authority, invalidation, lifetime | Context/recovery and external exclusions above; context and selective restoration tests follow. |
| Refraction integration and compatibility | Entry/caller inventory above; single capture adapter plus ordinary adapters and retained-family reconciliation follow. |
| Relationship to approved PSO work | Parent exception governs this bounded work; future command context reuses cache mechanism, complete pipeline adoption remains parent work. |
| Verification and acceptance | Deterministic cases above define later tests and final evidence gate; current capture cannot validate a future fix. |

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

Context changes withdraw mutable knowledge and reset draw-buffer, viewport, patch and buffer
alignment capabilities without resetting diagnostic totals. Returning to an earlier context starts
with unknown state. Missing registration also prevents reuse of authoritative knowledge; future
boundary entry must reject it. Resource names are neither deleted nor recreated by this mechanism.
Scalar setters reject invalid enum/size inputs before native mutation and publish knowledge only
after the native call returns. Native restoration failure handling remains part of the upcoming
boundary implementation, not a guarantee supplied by the retained legacy scopes.

The installed 1.22.7 engine and OpenTK 4.9.4 metadata were checked again for the public platform/window
fields and the protected `NativeWindow.Dispose(bool)` signature. Automated evidence does not launch
Vintage Story or establish live refraction correctness. Boundary declaration, capture, restoration,
and refraction adapter integration remain pending.

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
found no remaining foundational coverage issues; snapshot/restoration and live acceptance remain open.
