# VGE shader ownership and engine separation proposal

Status: design contracts and source dependency audit resolved; no runtime implementation or migration validation has been performed.

## Objective and bounded outcome

Make VGE graphics programs independent of the game's shader inheritance, registry, and executable lifetime. Establish explicit VGE ownership of declaration, preparation, activation, retained inputs, reload invalidation, and retirement. Keep the remaining engine interaction in integration adapters that can be removed as VGE takes ownership of more rendering.

The completion boundary is that a VGE program can prepare, render, reload, and retire without constructing an engine Shader, implementing IShaderProgram, entering ShaderRegistry, or becoming ShaderProgramBase.CurrentShaderProgram. A narrow engine adapter may still translate inputs for borrowed mesh-pool traversal. Native shader patching and native resource borrowing remain supported integration responsibilities.

This work does not require replacing terrain rendering, world traversal/culling, framebuffer allocation, shader compilation tools, generated bindings, or the existing graphics pipeline/state cache. Those owners already supply the necessary contracts.

## Evidence and current dependencies

The October 10 crash session records missing vertex/fragment stages followed by a NullReferenceException during engine registration of pbr_final. GpuProgram.Spirv installs null engine stage slots for cached executable hits; CompletePreparation subsequently registers the program with the engine. This registration failure is converted into false readiness and the mandatory HDR check aborts the frame.

Current sources were inspected together with IL from the installed G:/Vintagestory/VintagestoryLib.dll. Inspection read method bodies without launching the game or executing rendering. The installed engine and API assemblies confirm:

- ShaderAPI.RegisterMemoryShaderProgram casts IShaderProgram to the concrete engine ShaderProgram. An interface-only VGE implementation cannot be registered through that API.
- ShaderRegistry.RegisterShaderProgram stores the program and assigns PassId/PassName before calling LoadShaderProgram. Registration is not a passive name lookup operation.
- ShaderProgramBase.Use sets CurrentShaderProgram, binds the executable, and performs engine include-driven uniforms, texture bindings, and UBO binding.
- ShaderProgramBase.Stop binds zero, releases its sampler/UBO bindings, and clears CurrentShaderProgram.
- ShaderProgramBase.Dispose detaches/deletes every non-null engine stage and deletes the program.
- RenderAPIBase.CurrentActiveShader returns the engine's CurrentShaderProgram directly. Installed MeshDataPoolManager.Render IL confirms the origin, transform, transparency and finally-restoration call sites described below.
- ShaderRegistry.ReloadShaders refreshes assets, disposes and clears registered programs, and rebuilds native programs. ShaderAPI.ReloadShaders calls the public reload event afterward; its returned result comes from the registry, not the event handlers' readiness.

| Existing owner | Relevant responsibility and separation work |
| --- | --- |
| [GpuProgram](../VanillaGraphicsExpanded/Rendering/Shaders/GpuProgram.cs) and its Preparation/Spirv partials | Inherits engine fields, activation, uniform APIs and disposal. Replace these dependencies while preserving settings, preparation, generated Submit and pipeline revision contracts. |
| [GpuShaderPrograms](../VanillaGraphicsExpanded/Rendering/Shaders/GpuShaderPrograms.cs) | Already owns declarations, typed lookup, selected preload, removal and application cleanup. Its per-API table and RegisterOnPreparation coupling need a concrete lifetime owner. |
| [GpuComputeShader](../VanillaGraphicsExpanded/Rendering/GpuComputeShader.cs) | Already avoids engine inheritance, but stops/restores graphics owners through CurrentShaderProgram and retains a thread-local numeric-ID lookup. It must participate in the same VGE activation ownership as graphics. |
| [GraphicsCommandContext](../VanillaGraphicsExpanded/Rendering/Pipeline/GraphicsCommandContext.cs) | Owns shader scopes and per-draw submission. Preserve its validation and cleanup order; redirect activation ownership. |
| [EngineBoundaryExecution](../VanillaGraphicsExpanded/Rendering/Integration/EngineBoundaryExecution.cs) | Identifies incoming VGE owners through an engine-base type check. Replace this with explicit VGE owner state and keep unsupported incoming engine/raw footprints rejected. |
| [LiquidGraphicsSubmission](../VanillaGraphicsExpanded/PBR/Liquids/LiquidGraphicsSubmission.cs) | Borrows engine pool traversal and relies on CurrentActiveShader for input staging. This is the main functional engine-shader compatibility requirement. |
| [OwnedShaderSubmissionHook](../VanillaGraphicsExpanded/HarmonyPatches/OwnedShaderSubmissionHook.cs), [GpuProgramStopHook](../VanillaGraphicsExpanded/HarmonyPatches/GpuProgramStopHook.cs) | Compensate for engine-base calls bypassing owned submission and cleanup. Their VGE-specific behavior moves into VGE activation/stop; the hooks become unnecessary. |
| [ShaderDigestReloadHook](../VanillaGraphicsExpanded/HarmonyPatches/ShaderDigestReloadHook.cs), [VanillaGraphicsExpandedModSystem](../VanillaGraphicsExpanded/VanillaGraphicsExpandedModSystem.cs) | Coordinate asset refresh, native shader configuration and delayed VGE re-declaration. Replace delayed re-registration with an explicit reload transaction. |
| [EngineRenderScopes](../VanillaGraphicsExpanded/Rendering/Profiling/EngineRenderScopes.cs) | Gets fullscreen labels through engine current-program state. Labels need to consult explicit VGE ownership where appropriate. |

Production VGE shader lookup already uses GpuShaderPrograms. The engine registration result is not used to drive VGE passes. Native terrain, particle, atmosphere and material hooks still consume real engine shaders; these remain engine integration code.

## Proposed ownership model

Introduce an engine-independent IGpuProgram interface with default implementation methods for common program behavior. GpuProgram implements it as the graphics family; rename GpuComputeShader to GpuComputeProgram and have that compute family implement it as well. No shared GpuShader base class is proposed. Feature programs continue deriving from their respective graphics or compute family.

### Shared interface and default implementation

IGpuProgram defines the common preparation, activation, scoped use and terminal lifetime contract. Its default methods implement the shared sequencing and guards through explicitly supplied owner state and family-specific hooks. Keep IShaderSubmissionTarget as the narrow installed-executable/layout view used by numeric input validation; binding-only consumers do not need disposal or preparation authority. IGpuProgram should reuse that contract where accessibility permits rather than introduce a second independent source of executable/layout state.

The proposed relationship is interface implementation, not common class inheritance:

```text
                    IGpuProgram
                  /             \
          GpuProgram         GpuComputeProgram
               |                    |
       graphics programs     compute programs
```

| Default interface behavior | Concrete family responsibility |
| --- | --- |
| Check retirement and reject input mutation or recursive activation during submission | Store per-owner lifecycle state and retain generated input values |
| Guarded preparation entry invoking the family's preparation hook | Graphics settings/replacement policy or compute eager/deferred preparation policy |
| Ensure readiness, bind through StateCache, publish generated Submit, and unwind failed publication | Expose the installed executable/layout and implement the generated submission hook |
| Register owned CPU uniform blocks, attach write guards and coordinate terminal release | Own the block collection and release family-specific resources through disposal hooks |
| Participate in the existing scoped restoration contract | Graphics identity/revision validation, compute dispatch and diagnostic wrappers |

C# interfaces cannot hold instance fields. Keep submitting/retired flags and the owned CPU-block collection in each program owner, preferably in one small composed lifecycle-state object with a stable identity per owner. Default methods operate on that state through a restricted implementation contract. That object holds lifecycle data only; it is not a registry, binding cache or activation stack, and it does not own a second copy of the native executable. Settings, installed generations and generated retained properties stay with their existing family or concrete owner.

Default interface methods are invoked through an interface reference and are not inherited as callable concrete-class members. Route shared scope/registry operations through IGpuProgram. Preserve concrete APIs needed by existing callers and generated properties using deliberate family facade methods where necessary. Such facades must invoke distinct default workflow methods rather than cast and call the same interface slot they implement, which would recursively dispatch back to the facade. Keep preparation/submission/disposal hooks distinct from workflow entry points. Retain the protected Submit override contract on each family and bridge it explicitly to the interface workflow so generated concrete overrides remain supported.

Choose interface accessibility together with its implementation hooks: the existing GpuProgram and GpuProgramLayout are public while IShaderSubmissionTarget is internal. An internal IGpuProgram can serve the rendering implementation without expanding those types' visibility. If IGpuProgram is made public, separate its public contract from internal layout/state hooks; a public interface cannot inherit a less-accessible interface. Do not expose mutable lifecycle state as a public API merely to support default methods.

The common activation workflow must deliberately reconcile today's differences: graphics prepares before entering its input-publication guard, while compute currently calls pipeline.Use inside its guard. Define the shared order and failure cleanup explicitly. Guard checks must precede cleanup-owning activation so a rejected recursive call cannot tear down the enclosing submission. Terminal disposal must be idempotent, reject disposal during publication, and release owned CPU blocks and family resources exactly once. Migrate existing compute Dispose overrides to the chosen disposal hooks, preserving extra resources and cleanup on failure. Preserve legitimate Dispatch overrides such as LumonSceneCaptureVoxelComputeShader's diagnostic wrapper. Verify concrete calls, IGpuProgram calls and IDisposable calls all reach the intended common workflow.

Shared preparation entry does not mean preparation is already interchangeable. GpuProgram tracks requested and installed settings and can replace an executable. GpuComputePipeline.EnsureReady transfers one candidate and clears its preparation factory; RetryPreparation only permits another failed attempt and does not invalidate a successfully installed executable. Initially keep these policies behind their family implementation. Before promising common asset invalidation or settings mutation, explicitly extend compute to retain a rebuild description and publish validated replacement generations, or keep feature-owned reconstruction as its stated reload policy. Never expose a common invalidation method that silently leaves compute's valid executable unchanged.

Keep native ownership composed. Initially GpuComputePipeline remains the compute executable owner, while graphics adopts GpuProgramObject as described below. The common interface workflow must not also delete the compute program handle. Sharing the entire executable preparation backend requires reconciliation of eager factories, deferred preparation, graphics batch linking and settings replacement; it is not required for sharing lifecycle behavior or removing engine registration.

This interface introduces no activation stack, ambient current-owner dictionary or state cache. Consolidate existing scope behavior using the established program scopes and command-context lifetimes described below. Those lifetimes may borrow IGpuProgram references for shared operations; StateCache remains the native binding authority.

### Compute family rename and migration

Rename the GpuComputeShader type and its source file to GpuComputeProgram. Update derived declarations, constructors, type references, restoration paths, tests, fixtures and documentation. This rename concerns the common compute family; it does not require renaming every feature-specific shader class or GpuComputePipeline, whose native executable ownership remains distinct.

Update generator owner recognition and interface-binding assumptions with the rename. RuntimeSubmissionEmitter and BindingReader currently recognize GpuProgram and GpuComputeShader, and InterfaceBindingReader also assumes an owning compute field named pipeline. Recognize the new family name or the IGpuProgram contract while preserving eligibility rules for concrete generated owners and authored Submit overrides. Verify runtime and offline generated shells, intermediate abstract families, retained property mutation guards and interface dispatch. Do not assume a default interface method becomes a concrete member visible to generated code.

The benefit is shared lifecycle policy without imposing common class inheritance. The cost is explicit per-owner state access, carefully separated default-method dispatch and family hooks, and coordinated generator/caller migration. The original crash is resolved by eliminating engine inheritance/registration and its stage-object requirements; IGpuProgram supplies the reusable VGE behavior around that fix.

Use composition for the native executable. GpuProgramObject.Adopt and the existing GpuResource deletion policy already provide native program ownership. A prepared generation combines that owner with its immutable settings/load plan, prepared interface, graphics identity, and successful-installation revision. GpuProgram owns this generation and its retained desired inputs; GraphicsPipeline borrows it and continues to reject stale revisions.

There are three distinct identities:

1. The declaration/owner identity within one registry lifetime.
2. The successfully installed executable revision and the asset generation used to prepare it.
3. The native render-context handle and registration generation, using the existing RenderContextRegistry.

A numeric GL program name is never sufficient to identify an owner after deletion, replacement, or context reuse. Registry membership and scoped activation are separate concerns: the registry owns declared programs, while existing command contexts and binding scopes own their use within a rendering context.

## Executable preparation and retirement

Preserve the existing SPIR-V stage loader, driver binary cache, ShaderLinkBatch, numeric interface preparation, generated binding contracts, and requested-versus-installed settings.

For both cached and uncached paths, prepare and validate a complete candidate before publication. Successful installation publishes the executable, interface, settings, asset generation and revision together. Failure or superseded settings discard the candidate and retain the previous installed generation without allowing it to satisfy incompatible current requests. Preparation diagnostics should remain available to the caller so mandatory HDR failures identify the failing program and actual cause.

Engine Shader objects, VertexShader/FragmentShader/GeometryShader fields, EngineDisposed reflection, PassId, and inherited uniformLocations cease to be runtime ownership requirements. Shader stages become temporary compiler/linker resources; after successful linking, detach and release them through their existing owners. A cached executable naturally has no stage objects. PendingShaderProgram ownership transfer must be updated so synchronous, batched, failed and cancelled paths release every handle exactly once.

Terminal owner disposal and executable invalidation are different operations. Reload preserves declarations, settings and owned CPU input blocks; terminal disposal releases those blocks and prevents revival. Reuse GpuProgramObject/GpuResource and StateCache.DeleteProgram for deletion and targeted retirement tracking, rather than retaining raw GL deletion paths for installed generations.

Replacement or terminal retirement while the program is actively borrowed by submission must be rejected or deferred to a safe boundary. Restoration must never revive a disposed owner or bind a recycled program ID. Context retirement must withdraw owner knowledge before native context reuse; deletion must occur on the originating live context or be abandoned with the destroyed context, not executed against the next context merely because the integer matches. Existing resource-manager context/deletion ordering needs explicit verification here.

## Concrete registry responsibilities

Evolve GpuShaderPrograms into a registry instance owned by the client rendering lifetime. The engine composition root may associate that instance with ICoreClientAPI, but the registry's ownership model and program contract should not require the game API as their identity.

The registry should own:

- Declaration and lookup without binary reads or GL work; an absent or mismatched lookup produces a clear VGE diagnostic.
- Stable program identity and duplicate-declaration policy. Repeating the same declaration must not silently discard incompatible type/contract/settings. Asset reload should not construct replacement candidates merely to dispose them and invalidate existing entries.
- Explicit selected preload after configuration, preserving demand loading and failed-input suppression.
- Asset-generation invalidation and controlled executable retirement.
- Removal and terminal registry shutdown, including invalidation of queued callbacks so an old world cannot recreate its registry.
- Observable readiness/failure details, without conflating engine reload success with VGE executable readiness.

Move asset reads and logging behind the actual narrow dependencies already present in the compilation pipeline: ShaderAssetReader, digest access and diagnostic sinks. The integration composition root adapts the engine asset manager. Preserve batching's shared asset-source identity and generation; do not duplicate import, digest or binary-cache services. ShaderLinkBatch currently accepts the engine asset manager, so its entry boundary must be included if the core registry/program runtime is to be engine-type-independent.

Compute owners remain feature-owned where that matches current lifetime. Sharing activation and lifetime behavior does not require moving every compute declaration into a global registry or merging GpuComputePipeline with graphics pipeline descriptions. Their reload policy must remain explicit until compute supports executable replacement.

## Existing scope ownership and engine handoff

Reuse the existing scope and pipeline machinery. No separate ambient current-shader registry or parallel activation stack is proposed. StateCache remains the authority for native binding state; GraphicsCommandContext and its selected GraphicsPipeline already retain the shader object and executable revision needed for managed draws.

The existing mechanisms have different responsibilities:

- StateCache.UseProgramScope uses ProgramScopeTracker/BindScopeTracker to save and restore numeric program bindings. Its current stack is per-thread and stores integers, not shader owners, executable revisions or context generations. It is used by low-level compute and layout preparation.
- GraphicsCommandContext retains ShaderActivation for the selected pipeline, disposes it on pipeline changes or EndPass, and registers it with EngineBoundaryScope for exceptional cleanup. It already owns managed activation lifetime.
- ShaderActivation currently calls GpuProgram.UseScope, whose restoration still captures ShaderProgramBase.CurrentShaderProgram and optionally a compute owner. GpuComputeShader.UseScope has a corresponding engine-dependent path. These owner-aware scopes are not implemented through the numeric ProgramScopeTracker today.

The migration should replace the engine-dependent contents of those existing scopes and consolidate their restoration responsibility with the established binding scopes where appropriate. Carry a borrowed owner/generation reference in the existing program-scope or command-context lifetime only where resubmission and retirement validation require it. Do not recover an owner through another global dictionary or duplicate the command context's selected shader state.

Preserve the distinction between simple binding restoration and shader input restoration. Rebinding program A after a compute operation does not restore texture/UBO slots overwritten by that operation. The enclosing command context can submit A's retained inputs at its next draw; a supported immediate scope that promises restoration before returning must perform that restoration itself. Existing complete boundaries also restore declared resource bindings. Assign each transition to one existing owner and avoid redundant replay where that boundary already restores the required state.

Every draw/dispatch must still publish its intended inputs even when the numeric program binding does not change. VGE stop/cleanup releases the required sampler bindings through ProgramLayout and cannot unbind another owner's program through a stale token. Scope restoration must validate terminal retirement, executable generation and context identity where applicable; extend the program scope using the existing RenderContextRegistry rather than adding a second context registry. The generic per-thread binding tracker is not already sufficient for these stronger lifetime guarantees.

Move engine shader capture/Stop/Use into Rendering/Integration at the outer supported boundary. VGE core programs must not reference ShaderProgramBase.CurrentShaderProgram. Restoration can execute engine bindings and hooks, so it belongs in the declared footprint and ordered boundary cleanup. Preserve existing rejection of incoming native/raw/compute states whose complete restoration footprint cannot be established; supporting arbitrary engine-shader interruption is separate work.

Audit direct Use, TryUse and UseScope consumers as well as GraphicsCommandContext entry. The PSO path already owns graphics activation, but low-level compute and standalone uses have not all been unified under it. Route those operations through existing command/boundary scopes or extend the existing program scope for their proven restoration requirements. Headless owned rendering must not require a game singleton.

EngineStateSwitching already routes native GL mutations through StateCache. Keep that single cache and its existing explicit external boundary. No per-frame native scans, parallel current-owner tracking system or blanket invalidation is needed for the separation.

## Liquid mesh-pool input bridge

The API source MeshDataPoolManager.Render reads CurrentActiveShader to write origin and mini-dimension modelViewMatrix/forcedTransparency. Its finally block restores transform and transparency. Both LiquidShaderProgram and LiquidDepthShaderProgram currently implement those IShaderProgram methods and retain values in LiquidDrawParamsUbo. LiquidGraphicsSubmission activates the shader before entering the engine traversal for precisely this reason.

The preferred bounded bridge is a narrow interception at those pool-input call sites while LiquidGraphicsSubmission is active. Translate the engine's existing origin/matrix/transparency operations into a typed VGE pool-input sink. Keep culling, dimension offsets, mini-dimension interpolation, visibility groups and the engine's finally/restoration control flow in the engine owner. The existing intercepted pool draw continues through GraphicsCommandContext.Draw.

An implementation option is to replace the selected CurrentActiveShader getter calls inside MeshDataPoolManager.Render with a helper returning an engine-interface input adapter only for an active VGE liquid submission; otherwise return the real engine shader. That adapter is an input translator, has no GPU executable or registry membership, and never occupies CurrentShaderProgram. It must not silently accept rendering/compile/dispose operations as if it were an executable. A more direct translation of the small uniform-call set can avoid the broad interface entirely, but requires a more involved transpiler. Verify the installed method's call sites before choosing the exact interception.

Do not patch CurrentActiveShader globally or introduce an engine ShaderProgram proxy owning/aliasing the VGE executable. Either would recreate ambient coupling. Remove the liquid engine-interface implementations from shader classes after moving their behavior into the bridge. Unknown input names should be handled explicitly rather than falling through to inherited engine Uniform calls.

Required behavioral coverage includes ordinary pools, culled/empty pools, dimension offsets, mini-dimension transforms, preview transparency, engine restoration writes, visible/depth passes, exceptions, and unrelated native pool rendering. Borrowed engine geometry lifetime and draw validation remain with EngineLiquidPoolGeometry and LiquidGraphicsSubmission.

## Reload and lifecycle sequencing

Coordinate reload as an explicit engine-integration operation:

1. At a safe boundary, mark reload in progress and withdraw old-generation readiness. Clear the existing digest/import caches and capture the coherent shader configuration snapshot.
2. Let the engine refresh assets and rebuild its own shaders.
3. Complete VGE asset invalidation synchronously at the appropriate completion boundary, permit demand preparation again, and withdraw dependent render publications. Handle native exceptions, suppressed reloads and failed native compilation explicitly.
4. Preload only the existing selected mandatory sets when their owners have the required settings. Report lazy readiness separately from the engine API's reload return value.

The current queued RegisterAll callback should not remain the mechanism for asset invalidation: after engine registration is removed, the engine will no longer dispose VGE programs to make them stale before that callback runs. A delayed invalidation would otherwise leave a window where old executables appear current.

Distinguish configuration changes, asset reload, leave-world, registry disposal and native-context retirement. World leave/re-entry does not create a new native context generation. Terminal shutdown should stop submissions and dispose renderer/PSO borrowers before registry executable retirement, then drain deletion while the owning context is live. Current disposal is spread across mod systems, so verify the actual ordering rather than assuming the existing single registry Dispose call establishes it.

## Compatibility, generators and documentation

Generated contracts currently recognize GpuProgram and GpuComputeShader by their VGE types, not by the engine base class. Migrate compute recognition to GpuComputeProgram and account for IGpuProgram default-method dispatch as described above; verify runtime and offline generated shells. Preserve numeric prepared bindings, desired-state retention, successful-publication history and CPU UBO ownership.

Audit inherited API consumers explicitly. Production engine Uniform/HasUniform adapters are concentrated in liquid input translation; tests and diagnostic helpers also use inherited uniform/texture APIs. Migrate VGE fixtures to generated properties or preparation-owned numeric helpers. Do not recreate every engine overload on the independent class. Keep GpuProgramInterface's name/location projection where diagnostics or remaining fixture adapters still consume it; removing engine uniformLocations does not authorize deleting useful prepared metadata.

Remove VGE-specific engine Use/Stop patches once no VGE executable can enter the engine hierarchy. Reconcile unreachable type checks such as SceneColorProgramBindings' GpuProgram exclusion, LiquidPoolSubmissionHook's engine-current type switch, EngineBoundaryExecution's owner test, and graphics/compute restoration branches. Keep actual native terrain, atmosphere, material, shader-patch recovery, frame/light and particle hooks under their engine ownership.

Audit automatic Use-hook effects before removal: VGE owners must obtain every required shared frame/light input through explicit generated contracts rather than accidentally depending on an engine postfix. Preserve profiling labels through the VGE owner and keep native labels in the engine adapter.

Update GpuProgram.md, GPU.ShaderDemandLoading.md, Rendering.ShaderBindingState.md and the engine/pipeline boundary documentation. GpuProgram.md currently describes older source compilation and memory registration, so it needs reconciliation with the current binary pipeline and proposed ownership. Public GpuProgram/VgeShaderProgram consumers will lose engine assignability and inherited APIs; treat this as an intentional API change and document the supported replacement surface.

## Resolved migration contracts

These decisions are based on the current source and installed-engine inspection on 2026-10-10. They specify implementation work; they do not claim that the independent runtime already exists.

### Interface visibility, dispatch and retained state

Use an internal IGpuProgram extending IShaderSubmissionTarget and IDisposable. Public GpuProgram retains its supported concrete facade; GpuComputeProgram remains internal. GpuProgramLayout is already public (the earlier description of it as internal was incorrect); IShaderSubmissionTarget is internal. No visibility expansion is needed for this migration.

Use distinct default workflow names: Prepare, Activate, BeginUse and Retire. Concrete EnsureReady, Use, UseScope and Dispose delegate to the corresponding workflow, never back to their own interface slot. IDisposable.Dispose must reach Retire through the same concrete lifetime facade. Family hooks PrepareExecutable, PublishInputs and ReleaseExecutable supply specialized work; an explicit PublishInputs implementation calls the family's existing protected abstract Submit so authored and generated overrides keep their contract. Exact return types for scope tokens must follow the existing scope implementation. Verify these dispatch routes in the compiler/generator tests before migrating callers.

One composed lifecycle-state instance per owner holds submission/retirement flags, owned CPU blocks and outstanding scope-borrow accounting. It does not hold a second native handle, binding cache or current-owner lookup. The internal interface exposes it only to VGE implementation code. Generated property guards and OwnUniformBuffer use this same state. Family-specific additional resources use a disposal hook; retirement marks the owner terminal once and attempts all independent cleanup, preserving failures rather than skipping subsequent owners. An unused declaration can retire without a GL context.

The workflow order is guard, prepare, establish binding ownership, bind through StateCache, then guarded Submit. Recursive entry is rejected before an inner call owns cleanup. Submit failures clear only the failing activation, restore the enclosing scope when applicable, and preserve activation and restoration errors together. Preparation is not allowed to replace an executable during publication or while an existing scope borrows that generation. A same-owner nested scope may reuse its prepared generation, but pending replacement is rejected until the outer borrow ends.

### Scope and executable identity

A declaration is identified by its owning registry instance plus an immutable declaration key (domain/program contract identity) and its owner object. An executable generation combines that owner, monotonically increasing successful-installation revision, installed settings/load plan, asset epoch and the existing RenderContextRegistry (Handle, Generation) pair. ProgramId is a native binding value only. Failed/superseded preparation does not advance successful revision or make a mismatched old generation ready.

Reject replacement, detachment and terminal retirement while a program generation has outstanding scope borrows, including when it is suspended beneath a nested scope. Settings may become pending outside publication, but preparation waits until those borrows end. This uses explicit rejection rather than another deferred-work queue. Reload requests are admitted at an outer safe boundary and cannot partially invalidate an in-flight submission.

Extend the existing program-scope machinery with owner/generation metadata and context validation where required; do not add another stack. GraphicsCommandContext keeps its selected pipeline and exactly-once ShaderActivation lifetime. Standalone program scopes use the same program-scope owner contract. Direct Use/Stop facades participate in that machinery; raw UseProgramScope remains a binding-only operation and never invents an owner from an integer. A direct activation must establish an explicit lifetime in that machinery before it can be captured for managed restoration. Remove the compute weak numeric-ID lookup once these callers are migrated.

Immediate owner-aware nested scopes replay the enclosing owner's retained inputs before returning. Command-context draws publish intended inputs on every draw even for an unchanged executable. Complete boundaries restore their declared resource snapshots; implement one ordered restoration path so replay does not conflict with that snapshot. Sampler release follows ProgramLayout, and stale tokens cannot unbind a later owner. Reject wrong-context restoration before any GL call; use the existing boundary failure path for same-context cleanup errors.

Keep the current EngineBoundaryExecution admission limits: no active native engine shader, and no nonzero raw/unowned compute program whose complete reactivation footprint is unknown. A known VGE graphics owner is eligible only with a current prepared footprint. Managed graphics/compute nesting inside supported VGE scopes remains required; admitting arbitrary compute/native programs into the outer engine boundary is not required. Native capture/Stop/Use, if a supported adapter needs it, belongs solely to Rendering/Integration and its declared footprint.

### Compute reload policy

Keep feature-owned reconstruction of the compute executable; do not put compute into the graphics declaration registry or add graphics-style editable settings to every compute owner. Keep the GpuComputeProgram wrapper, retained inputs and owned CPU blocks alive during asset reload. The feature supplies a retained rebuild description/factory for its immutable settings and asset source; it prepares a new GpuComputePipeline candidate and publishes the candidate/interface/revision together through its program owner, then retires the old pipeline. The pipeline remains the single native compute owner. This requires replacing the currently readonly pipeline reference at a safe boundary, not rewriting both compilation backends.

For production asset-backed creation paths, retain the rebuild description even after successful eager or deferred preparation. Known-failed requests are suppressed per settings/asset epoch; changed assets permit another attempt. A failed candidate preserves the previous allocation but cannot dispatch it as the new epoch. Feature reload participants withdraw dependent publications and invoke this policy synchronously with the common reload transaction. Low-level raw/eager fixtures without a rebuild description remain explicitly caller-lifetime resources, not silently reloadable programs. RetryPreparation continues to mean failed-attempt retry only. This is required before claiming successful compute executable reload; existing one-time preparation alone does not satisfy it.

### Liquid bridge choice

Use the narrow getter-call adapter inside MeshDataPoolManager.Render. Replace only its verified IRenderAPI.CurrentActiveShader calls with a helper that returns the active LiquidGraphicsSubmission input adapter or the real native shader outside that submission. The adapter implements the engine interface only in integration code, owns no executable and never becomes CurrentShaderProgram or a registry member. Preserve argument evaluation, branch labels and exception regions; validate the installed call-site shape before applying the interception.

The input sink supports origin Vec3f, modelViewMatrix float[] and forcedTransparency float for both visible and depth owners. HasUniform returns true for those logical inputs and false for mvpMatrix, preserving the model-view branch selected today; unknown names return false for queries and throw for writes. Executable operations throw explicitly. The engine still computes culling, dimension offsets, mini-dimension transforms and preview transparency. Its finally writes traverse the same adapter. EngineLiquidPoolGeometry and GraphicsCommandContext.Draw retain geometry validation and submission. Remove the fallback engine-current type switch in LiquidPoolSubmissionHook when the bridge is active and validated.

### Registry and shutdown policy

Use one explicitly created registry instance per client rendering lifetime, with a closed state and monotonically changing callback/world epoch. World leave withdraws world publications and invalidates queued world work; it does not change native context generation or automatically retire reusable declarations. Client rendering teardown closes the registry permanently; a later client lifetime requires explicit composition-root creation. Static API adapters may locate an existing instance, but a late lookup/declaration callback must not implicitly recreate a closed registry through ConditionalWeakTable.GetValue.

Declare by immutable descriptor plus factory so an identical repeated declaration returns the existing owner without constructing a throwaway candidate or invalidating assets. Match domain, identity, concrete family/type, contract/schema and declaration-time settings. Incompatible duplicates throw before GL work or mutation; configuration changes use explicit settings operations rather than another declaration. Current requested settings can differ from declaration-time defaults without making repeated catalog declaration silently reset them. Missing/mismatched typed lookup reports a VGE diagnostic. Retired entries require explicit removal/redeclaration, not implicit revival.

Current source ordering is insufficient to promise coordinated shutdown: VanillaGraphicsExpandedModSystem.Dispose retires GpuShaderPrograms before disposing its frame/light renderers; PbrModSystem, LumOnModSystem, WorldProbeModSystem and AtmosphereModSystem independently dispose their own borrowers/resources. GpuResourceManagerModSystem disposes shared samplers, closes/drains its renderer, disposes streaming, then shuts down the manager. GpuResource.DeleteOrEnqueue currently routes by current manager/thread and carries no originating context generation. RenderContextLifetimeHook only withdraws the window registration. These are concrete migration sites, not already-proven lifetime guarantees.

The client rendering lifetime must explicitly close submission/callback admission, stop and release feature renderers and their PSO/scope borrowers, retire feature compute and registry graphics owners, release remaining shared GPU resources, drain deletion on the matching live context, and only then close the manager/unpatch integration. Connect the existing mod-system cleanup methods to that ordered owner; do not depend on unspecified mod-system disposal order. Repeated engine Dispose callbacks must be harmless. Window destruction retires the same lifetime before native handle reuse; if the originating context is already gone, abandon its queued native deletes instead of routing them to a new context. Stamp program deletion and queued work with RenderContextRegistry identity using existing resource services. Broader unrelated resource redesign is outside this shader migration.

## Migration coverage and evidence

Paths below are relative to the repository root unless linked otherwise. This is the source-backed dependency and future-test map for the migration, not a runtime validation report.

### Installed engine receipt

Read-only Harmony GetOriginalInstructions inspection on 2026-10-10 used installed G:/Vintagestory/VintagestoryLib.dll 1.22.7.0, module ID 8a3ad3aa-2940-4c39-ae1b-881de9e65346, and VintagestoryAPI.dll 1.22.7.0, module ID 83c0addd-72c9-42ba-a23e-5e05fe098ba0. No engine rendering method or game process was executed.

- ShaderAPI.RegisterMemoryShaderProgram casts to ShaderProgram; the string RegisterShaderProgram overload assigns IDs/names, stores the owner and calls LoadShaderProgram. registerDefaultShaderCodePrefixes directly dereferences both stage fields. This confirms why a stage-free cached VGE executable cannot safely enter that registry.
- ShaderProgramBase.Use binds the executable and writes CurrentShaderProgram. Its include-driven side effects include fog/light, shadows, warp, sky, colormap and underwater inputs, plus registered UBO binding. Stop unbinds program/samplers/UBOs and clears the global; Dispose detaches/deletes non-null stages, custom samplers and the program. These are real engine behaviors to remove from VGE ownership, while preserving native consumers.
- ShaderRegistry.ReloadShaders checks suppression, reloads shader/include assets, disposes and clears stored programs and rebuilds native programs. Full ShaderAPI.ReloadShaders IL retains the registry result on the evaluation stack and discards TriggerReloadShaders' result. VGE event success cannot redefine that returned Boolean.
- RenderAPIBase.CurrentActiveShader returns the global directly. MeshDataPoolManager.Render contains five getter sites (mini-dimension staging, three restoration branches, ordinary origin). The installed finally clause covers the RenderMesh call (try offset 409, length 22; handler offset 431, length 117). The checked-out ../vsapi/Client/MeshPool/MeshDataPoolManager.cs confirms the same branch structure, including the mvpMatrix alternative and dimension-adjusted ordinary origin. The adapter must preserve the method's existing exception boundary, not promise restoration for earlier staging exceptions outside it.

### Dependency disposition

| Current source/component | Current coupling and selected migration owner |
| --- | --- |
| Rendering/Shaders/GpuProgram.cs, .Preparation.cs, .Spirv.cs | Engine fields, NewShader, registration, base Use/Dispose, engine Compile override and stage ownership become IGpuProgram workflows plus composed executable preparation. Public facades lose engine assignability intentionally. |
| GpuProgram.Inputs.cs, .UniformOwnership.cs, .Options.cs, .Graphics.cs | Guards/CPU blocks use per-owner lifecycle state; settings transactions and graphics identity/revision stay graphics-specific. OnAfterCompile stays a post-install notification. |
| Rendering/GpuComputeShader.cs and GpuComputePipeline.Preparation.cs | Rename family; move duplicated guards/workflow/restoration to interface and existing scopes. Keep native pipeline ownership and feature rebuild descriptions explicit. |
| Rendering/Shaders/GpuShaderPrograms.cs; VgeShaderPrograms and generated Register/Get paths | Replace API-keyed implicit creation and candidate disposal/invalidation with explicit registry lifetime/descriptor lookup. Keep selected preload and batching. |
| Rendering/ShaderCompilation/ShaderLinkBatch.cs; ProgramBinaries and SPIR-V loaders | Preserve caches, source identity and transactional interface preparation; adapt engine asset reads at integration and transfer pending native ownership exactly once. |
| Rendering/Pipeline/GraphicsCommandContext.cs, GraphicsPipeline.cs; Rendering/EngineBoundaryScope.cs | Keep selected PSO/revision and ordered cleanup; borrow IGpuProgram in existing activation scopes. EndPass releases activation before pass targets; stale PSOs remain invalid. |
| Rendering/Integration/EngineBoundaryExecution.cs; StateCache program scopes; BindScopeTracker.cs | Replace engine-owner type tests and integer-only managed restoration with scoped owner/generation metadata, retaining entry rejection and a single native cache. |
| PBR/Liquids/LiquidGraphicsSubmission.cs; LiquidPoolSubmissionHook.cs | Active liquid submission supplies input sink/adapter; pool draw interception still calls GraphicsCommandContext.Draw. Remove obsolete engine-current fallback branches. |
| LiquidShaderProgram.EngineInterface.cs; LiquidDepthShaderProgram.cs | Move logical uniform translation into the integration adapter. Retain draw/wave/frame input owners; audit their actual owned versus borrowed CPU blocks during lifetime migration. |
| HarmonyPatches/OwnedShaderSubmissionHook.cs, GpuProgramStopHook.cs | Remove VGE base-call compensation once interface activation and sampler cleanup replace it. |
| HarmonyPatches/FrameShaderBindingHook.cs | Retain for real engine frame/light consumers. VGE must use explicit generated FrameInputs/LightsInputs; verify contracts before relying on removal of base Use side effects. |
| PBR/IPBRDirectLightingShaderProgramBindings.cs, PBRComposite bindings, liquid bindings, debug/frame bindings | Already declare VgeFrameUBO/VgeLightsUBO where used and source current shared CPU snapshots. Preserve generated publication and test it without engine Use hooks. |
| PBR/SceneColor/SceneColorProgramBindings.cs; Rendering/Profiling/EngineRenderScopes.cs | Remove impossible GpuProgram exclusion from native code; obtain VGE profiling names from scoped owners and retain native labels in integration. |
| TerrainTessellationPrograms/DisplacementRuntime, TerrainSurfaceNormals, SceneColorParticleCapture | CurrentShaderProgram access belongs to actual native terrain/particle draws and stays under engine ownership. Retain material/relief, atmosphere, shader-patch recovery and particle hooks. |
| ShaderDigestReloadHook.cs; VanillaGraphicsExpandedModSystem.OnReloadShader/TryReloadShadersInWorld | Replace queued RegisterAll with safe synchronous invalidation, callback epoch checks and separate native/VGE readiness. Preserve suppression and native config snapshot behavior. |
| EngineRenderContext.cs, RenderContextRegistry.cs, RenderContextLifetimeHook.cs | Reuse window handle/registration generation; world leave is not context loss. Integrate owner withdrawal and context-stamped deletion. |
| GpuResource.cs, GpuResourceManager.cs/System.cs, GpuResourceManagerModSystem.cs | Keep targeted deletion but establish originating-context admission/drain for program resources and ordered shutdown; thread identity alone is insufficient. |
| ShaderContractGenerator RuntimeSubmissionEmitter, BindingReader, InterfaceBindingReader | Update both old family-name recognition sites and pipeline-field alternate emission. Keep abstract-family eligibility and authored Submit; verify interface workflow facades compile. |
| Test/diagnostic Uniform/HasUniform/BindTexture adapters and GpuProgramInterface | Migrate inherited APIs to typed retained inputs or preparation-owned numeric helpers; retain diagnostic name/location projections and native engine adapters. |

Direct-use search found production graphics activation concentrated in GraphicsCommandContext.ShaderActivation, EngineBoundaryScope.Activate and liquid/owned-engine hooks; restoration calls live in the two program families. TryUse has no external production call site in this snapshot but remains a public facade and test obligation. Compute Dispatch/DispatchIndirect use their owner scopes; raw GpuComputePipeline Use/UseScope/Dispatch/DispatchBound remain explicit lower-level contracts. Inspect those direct entry points as well as managed draw consumers. Feature compute creation occurs in DepthHierarchyComputeShader, AtmosphereGpuComputation, WorldProbeTraceBatch/HybridCommit/history-clear, and LumOn scene capture/feedback/relight/query/history owners; each asset-backed path must carry the selected rebuild description without a new global registry.

The current supporting docs were consulted: [engine state switching](Rendering.EngineStateSwitching.md) preserves exact native routing/targeted invalidation; [shader binding state](Rendering.ShaderBindingState.md#prepared-resource-submission) and its desired-state/compatibility sections preserve numeric validation, retained publication and actual engine adapters; [demand loading](GPU.ShaderDemandLoading.md) supplies demand/preload and failure suppression. Their existing engine-reload/base-disposal descriptions are the migration baseline, not a requirement to retain engine ownership. [GpuProgram.md](GpuProgram.md) has stale runtime source-compilation, registration and immediate-setter guidance; the current source and this proposal control those replacements, with documentation reconciliation required before final acceptance.

### Verification map and known gaps

The listed tests were inspected, not run for this source-audit task. Paths without a prefix below are under VanillaGraphicsExpanded.Tests; generator tests are under ShaderContractGenerator.Tests. Existing names identify reusable coverage, not proof of the proposed implementation.

| Contract | Existing source evidence | Required migration evidence |
| --- | --- | --- |
| Interface/default workflow dispatch | GPU/ShaderDemandPreparationTests: ActivationPreparesOnlySelectedOwner, InterfaceDisposalOfUnusedDeclarationDoesNotCreateGpuWork; GPU/GpuProgramUseScopeTests | Replace IShaderProgram routes with concrete/IGpuProgram/IDisposable routes; prove no facade recursion, inner-failure teardown, mutation during Submit or revival. |
| Generated submission and rename | ShaderContractGenerator.Tests/RuntimeSubmissionTests, InterfaceBindingTests; Unit/Rendering/ShaderInputSubmissionStateTests | Cover GpuComputeProgram, inherited/explicit/authored implementations, default-method dispatch, runtime/offline shells and unchanged desired-state publication. Existing default property tests do not validate the new lifecycle interface. |
| Cached graphics/compute and original crash | GPU/DriverProgramCacheTests, FinalDisplayShaderTests, ShaderLinkBatchTests | Combine actual pbr_final cold/warm execution with a fixture that rejects NewShader/RegisterMemoryShaderProgram. Current cache tests Initialize directly rather than requiring registered catalog owners; they cannot expose registration failure. Cover bad cache, batching, supersession and cancellation. |
| Managed scopes/resources | GPU/GpuProgramUseScopeTests, ComputeInputSubmissionTests, ShaderInputSubmissionTests, ShaderUniformStateTests | Preserve all four family nesting combinations, unchanged-ID publication and replay; replace assertions that VGE occupies CurrentShaderProgram with scoped VGE identity. Add context/revision reuse and borrowed-retirement rejection. |
| Boundaries and PSOs | GPU/EngineBoundaryEntryTests, EngineBoundaryBindingTests, EngineBoundaryRestorationTests, GraphicsCommandContextTests, PreparedGraphicsPipelineTests | Keep unsupported-footprint rejection, exactly-once exceptional cleanup, stale-revision rejection and native state restoration. Current retired-enclosing-owner scenarios must be reconciled with earlier rejection at active borrowing. |
| Liquid input bridge | GPU/LiquidGraphicsSubmissionTests, LiquidShaderProgramTests, LiquidDepthShaderProgramTests, EngineLiquidPoolGeometryTests; LiquidDrawTransformTests | Existing submission test calls the draw hook from a delegate using a fixture shader; it does not execute manager mini-dimension traversal. Add installed call-site checks and real manager traversal for visible/depth, native/empty/culled pools, mvp alternative exclusion, transparency and finally restoration. |
| Registry semantics | GPU/ShaderDemandPreparationTests, LumOnDebugShaderDemandTests; Unit/Rendering/Contracts/ShaderRegistryTests and ShaderDeclarationTests | Contract registry tests cover metadata, not runtime GpuShaderPrograms lifetime. Add duplicate descriptor/type/settings, missing lookup, explicit closed-instance admission, world epoch callbacks and selective preload coverage. |
| Reload including successful compute | GPU/ShaderDemandPreparationTests.ComputeFailureWaitsForExplicitRetry; DriverProgramCacheTests.GraphicsCacheRetainsRenderingAndReloadLifetime | Existing compute retry only proves a failed first load; graphics test simulates engine base disposal. Add synchronous asset-epoch invalidation, native suppression/failure/event ordering and successful feature compute pipeline replacement with retained CPU blocks. |
| CPU/native retirement and context | Unit/Rendering/GpuResourceDisposalQueueTests; GPU/GpuResourceManagerDeletionQueueIntegrationTests, StateCacheResourceDeletionTests, GpuProgramSamplerRetirementTests, EngineStartupContextTests | Current queue test proves background-thread deletion, not wrong-context protection. Add scope-borrow rejection, callback withdrawal, exactly-once stages/programs, context-handle reuse and ordered feature/registry/manager teardown. |
| Native/shared inputs and live behavior | GPU/FrameShaderBindingHookTests, DirectLightingSubmissionTests, SceneColorRuntimeTests; user-run client | Prove VGE draws use explicit frame/light inputs without base hooks while native consumers still work. Obtain cold/warm world/reload/liquid acceptance separately; local installed-client IL is not CI or live-rendering evidence. |

BinaryShaderApiFixture.NewShader constructs engine stages and RegisterMemoryShaderProgram only inserts/disposes dictionary entries. RuntimeLightingPrograms uses Moq registration callbacks that record a GpuProgram. SurfaceCacheRuntimeFixture and LumOnDebugRendererFunctionalTests also stub registration. None reproduces ShaderRegistry.LoadShaderProgram/default-prefix stage dereferences. Migrate these fixtures to observe VGE registry/preparation directly and reject accidental engine registration instead of perpetuating the mock blind spot. EngineShaderPlatformScope and current-shader assertions must remain only for explicit native adapter tests after separation.

## Dependency-ordered implementation and acceptance

1. Pin installed-engine handoff/pool-input contracts and audit all inherited API consumers. Define registry lifetime, executable generation, the shared IGpuProgram contract and existing scope invariants.
2. Remove engine-owner dependencies from existing program/command scopes and establish the liquid input bridge. Include compute and raw-binding interactions without introducing a parallel tracker.
3. Remove engine inheritance and interface implementation from VGE executable owners; establish IGpuProgram default workflows with explicit owner state, rename GpuComputeShader to GpuComputeProgram, install independent executable resources, and migrate generator assumptions, interface/facade dispatch, disposal hooks and inherited-API consumers. Remove engine registration as part of the same coherent migration.
4. Establish synchronous reload invalidation, registry shutdown and context-retirement behavior. Remove obsolete registration callbacks and VGE-specific engine shader hooks.
5. Reconcile documentation and validate the complete owned path, including the original cached final-display failure scenario.

These steps are dependencies for one reviewed migration, not permission to ship an intermediate state that loses liquid input staging or reload ownership.

Acceptance requires:

- Both program families implement IGpuProgram; shared default workflows preserve preparation, mutation and retirement guards through interface and concrete entry points. GpuComputeProgram replaces the old family name in runtime code and generated owners. No shared GpuShader base or parallel activation tracker is introduced.

- Structural checks show no VGE executable derives from engine ShaderProgram/ShaderProgramBase or implements IShaderProgram, and no VGE executable reaches engine registration. Engine compatibility adapters stay outside the hierarchy.
- A final-display shader loaded from a real executable cache hit prepares and renders without NewShader or RegisterMemoryShaderProgram. Repeat with a cache miss, invalid cache fallback, batched compilation and preparation failure.
- Readiness is transactional across settings changes, asset generations and failed/superseded candidates. No stale GraphicsPipeline accepts a replaced generation; no old registry callback revives a retired owner.
- Graphics-to-graphics, graphics-to-compute, compute-to-graphics and compute-to-compute scopes restore resources and owner identity on success and failure. Raw bindings and context/ID reuse cannot masquerade as a live owner.
- Liquid visible/depth rendering preserves ordinary and mini-dimension inputs, preview transparency and exception cleanup using real engine traversal where feasible.
- Shader-owned CPU blocks survive executable reload and retire once; native programs and temporary stages retire once on the right context. Targeted cache/retirement semantics remain intact.
- Existing pipeline, binding, sampler retirement, state switching, generator, demand-loading and resource tests pass with fixtures that reject accidental engine registration rather than simulating it as a dictionary insert.
- User-run game acceptance covers cold/warm startup, entering/leaving/re-entering a world, native shader reload, live shader configuration, liquids and native rendering around VGE passes. Headless evidence alone does not establish that acceptance.

No builds, tests, performance measurements or live-game validation were run for this analysis. Existing tests were inspected to identify migration coverage and engine-mocking gaps.
