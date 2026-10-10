# VGE shader ownership and engine separation proposal

Status: proposed; no runtime implementation or migration validation has been performed.

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

Choose interface accessibility together with its implementation hooks: the existing GpuProgram is public while IShaderSubmissionTarget and GpuProgramLayout are internal. An internal IGpuProgram can serve the rendering implementation without expanding those types' visibility. If IGpuProgram is made public, separate its public contract from internal layout/state hooks; a public interface cannot inherit a less-accessible interface. Do not expose mutable lifecycle state as a public API merely to support default methods.

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
