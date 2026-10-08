# Engine state switching through the cache

The engine's supported native state calls are replaced at their managed call sites with adapters into `GlStateCache`. This is an integration change to the existing cache, independent of the approved authoritative-pipeline proposal.

## Patch boundary

[EngineStateSwitchingHook](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateSwitchingHook.cs) participates in the existing client mod's `Harmony.PatchAll()` startup. It discovers concrete methods and constructors in the loaded `VintagestoryLib`, `VSEssentials`, `VSSurvivalMod`, and `VSCreativeMod` assemblies whose original IL contains a mapped static OpenGL call. OpenTK itself, VGE, and third-party mods are not patched. Cache operations therefore still reach the original native functions without recursion.

[EngineStateTargets](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateTargets.cs) scans metadata and walks IL instruction boundaries before resolving candidate callers. This avoids loading unrelated optional dependencies or resolving unrelated method references during discovery. Built-in assemblies must already be loaded when the mod's startup patches run; late-loaded assemblies are not automatically patched. A generic caller containing a supported call is explicitly rejected rather than silently omitted.

[EngineStateCallMap](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateCallMap.cs) resolves adapters by exact method name, parameter types, and return type. An adapter without a matching native signature fails map initialization. The transpiler replaces the call operand only, preserving evaluated arguments, instruction count, branch labels and exception blocks. Unsupported methods are left unchanged. Another transpiler's replacement is not forcibly overwritten.

## Previously compiled render API callers

Immediately after `Harmony.PatchAll()` installs the native-call replacements, [EngineRenderApiStatePatches](../VanillaGraphicsExpanded/HarmonyPatches/EngineRenderApiStatePatches.cs) applies the same transpiler to declared managed methods and instance constructors on engine types implementing `IRenderAPI`. Selection includes private helpers and property accessors, without individual method names. Abstract bodies, open generics and static initializers are excluded. Both passes use the mod's Harmony owner and existing unpatch lifecycle.

The menu can compile render API methods before mod startup. The JIT can inline platform GL wrappers into those bodies, so patching the platform wrappers alone leaves native calls in already compiled callers. Rebuilding the render API bodies after the wrappers are patched removes that bypass. Where no mapped GL call exists, the transpiler preserves the original IL; Harmony still generates a replacement body. Existing copies in unselected higher callers are not automatically rebuilt, and this selection does not claim complete coverage of arbitrary mod code.

## Supported operations

[EngineStateCalls](../VanillaGraphicsExpanded/Rendering/EngineStateCalls.cs) supplies signature-compatible adapters for:

- Enable/disable of depth testing, culling, scissor testing and global/indexed blending. Other capability values pass through to OpenGL.
- Depth comparison/write mask, global/indexed blend factors including separate RGB/alpha factors, global color mask, line width and point size.
- Executable, program-pipeline, VAO, framebuffer, renderbuffer and transform-feedback binding.
- Active texture unit, texture and sampler binding. Native texture-unit enums become zero-based cache units. Engine texture binding issues only the native bind, without querying or reselecting the active unit, and does not alter sampler ownership. When the unit is known, its cached target binding is updated; otherwise that target's snapshots are invalidated across units and the active unit remains unknown.
- Generic buffer, indexed buffer base/range, and image-view binding.
- Pixel-store changes, patch vertex count and the cached provoking-vertex convention.
- Engine texture/buffer retirement and deletion invalidation for framebuffer, sampler, VAO and program bindings, including the engine's by-reference bulk buffer deletion overload.

Only exact mapped overloads are substituted. The installed-engine coverage test exercises discovered targets; adapters for currently unused operations provide coverage when those exact calls appear in the engine. This is not a claim that every OpenTK overload is supported.

The complete graphics-state adapters also route viewport, scissor rectangles, blend equations, stencil configuration and depth range through their cache owners. Exact mapped signatures remain the boundary; unsupported operations and capability values retain native behavior. See [authoritative pipeline state](Rendering.AuthoritativePipelineState.md) for the complete ownership contract.

## Cache consistency fixes

An indexed blend mutation invalidates the cached global blend value. Otherwise a later global restore to the previous value could be skipped even though one output differs. Tests exercise global-to-indexed-to-global transitions against actual driver state.

Binding a previously unobserved VAO no longer invents a zero element-buffer association. The engine may have populated that VAO before cache observation; its EBO remains unknown until queried or bound through the cache.

Line width and point size use exact equality rather than approximate equality, so forwarding engine calls does not suppress distinct requested values.

Deletion adapters update only entries referencing the retired resource. Matching sampler slots and framebuffer read/draw bindings become zero; deleting a VAO removes only its element-buffer association and resets the selected VAO only if it was current. Single and bulk buffer deletion share targeted cleanup of matching generic, indexed and element-buffer snapshots. Zero names leave cache knowledge unchanged. Unrelated entries in the same category remain known, as does the active texture unit.

Program deletion leaves binding knowledge intact: a current executable remains in use until a subsequent program switch. See [Khronos program deletion semantics](https://wikis.khronos.org/opengl/GLAPI/glDeleteProgram). Matching buffer associations in other VAOs become unknown rather than being asserted zero: unbound containers can retain references to deleted storage. See [OpenGL object deletion semantics](https://registry.khronos.org/OpenGL/specs/gl/glspec44.core.pdf), section 5.1.2. Resource-specific cleanup is owned by `StateCache.ResourceDeletion.cs`; category invalidation is reserved for boundaries where the exact changed resource is not known.

Targeted-deletion validation passed 38 distinct tests, including six new GPU cases for surviving sampler/framebuffer/VAO bindings, bulk buffer deletion, retained storage in an unbound VAO, and current-program deletion. After strengthening exception-path test cleanup, the final normal build passed with zero errors and 101 warnings and all six new tests passed again without skips. Receipts: `artifacts/targeted-deletion-build.log`, `artifacts/targeted-deletion-tests.log`, `artifacts/targeted-deletion-unbind-tests.log`, and `artifacts/targeted-deletion-final-tests.log`. No game was launched.

Single-field pixel-store adapters preserve native overload behavior and invalidate the aggregate pack snapshot instead of querying and replaying unrelated fields.

## Selective invalidation

`GlStateCache.Invalidate(EPipelineState)` forgets selected cached knowledge without issuing GL commands or queries. Flags can be combined, for example `Invalidate(EPipelineState.Program | EPipelineState.FramebufferBindings)`. `None` is a no-op; unknown bits are rejected before modifying the cache.

The enum covers depth, blending, culling, scissor enable, color mask, line width, point size, patch count, provoking convention, program/pipeline, VAO, framebuffer, renderbuffer, transform feedback, active texture selection, textures, samplers, buffers, images and pixel-pack layout. `FixedFunction`, `Bindings`, and `All` provide composite masks. These flags are independent of the existing pipeline descriptor bit layout.

Active texture selection, per-unit texture bindings and sampler bindings are separate categories. Invalidating one preserves the other two. Dependencies are explicit: blending invalidates global and indexed snapshots together; VAO invalidation also clears element-buffer associations; buffer invalidation clears generic, indexed and element-buffer snapshots while preserving the selected VAO; transform-feedback invalidation also removes that object's indexed feedback-buffer snapshots. Framebuffer invalidation clears both read/draw snapshots and their combined alias.

`InvalidateAll()` remains a compatibility entry point for `All`. `PurgeCache()` and `BeginFrame()` retain their existing broad binding/pixel-pack boundary semantics. Unknown external mutation boundaries still require broad invalidation; this change narrows resource deletion paths where affected state is known. Diagnostic counters and immutable capability limits are preserved.

Selective-invalidation validation: the normal build passed with zero errors and 106 warnings; all 41 focused invalidation, engine-switching, unbind, pixel-pack and shader-binding tests passed with zero skips. Tests cover independent categories, combined and invalid masks, dependent VAO/buffer and transform-feedback snapshots, full-invalidation compatibility, and preservation of active-unit knowledge through resource deletion. Logs: `artifacts/selective-invalidation-build.log` and `artifacts/selective-invalidation-tests.log`.

## Limits

This work routes state through the existing cache. Binding operations that currently always issue native calls retain that behavior; this is not a new blanket binding-deduplication policy or a measured performance improvement.

Existing shader ownership, framebuffer observation, invalidation and restoration scopes remain in place. Tracking engine mutations does not itself restore state required after a VGE pass. Raw calls from other mods and unobserved external operations still require the established boundary handling. Context lifetime behavior is unchanged.

The first validation build also exposed an existing `GpuQuery.ResourceId` setter assigning `nint` to its `int` field. The setter now explicitly converts to `int`, matching its stored GL object name and allowing the build to proceed.

## Verification

Verification uses exact-signature/IL tests, installation against discovered engine methods without executing the game, and headless GL tests that execute transpiled representative call sites. No game process was launched; live rendering acceptance remains user-run.

On 2026-10-03 the installed binaries contained 216 mapped call sites across 78 methods: 196 calls in 72 `VintagestoryLib` methods and 20 calls in six `VSEssentials` cloud-rendering methods. Every discovered body was checked for complete mapped-call replacement, and the real Harmony transpiler installed successfully on all 78 targets before test cleanup removed it.

The builtin metadata inventory found only unsupported `DrawBuffers` use in `VSSurvivalMod`; `VSCreativeMod` and the API assembly do not reference OpenTK.Graphics. The core's only alternate OpenTK graphics declaring type was `GL.Ext.CheckFramebufferStatus`, which is a query and remains unchanged.

The delegated Debug build completed with zero errors and 101 warnings. All 45 selected tests passed, with zero failures or skips:

```powershell
$env:NUGET_PACKAGES='C:\Users\Sisco\.nuget\packages'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj --no-restore --filter 'FullyQualifiedName~EngineStateSwitching|FullyQualifiedName~EngineStateInventoryTests|FullyQualifiedName~GlStateCache|FullyQualifiedName~ShaderBindingOperationTests|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~GpuTextureLifetimeTests|FullyQualifiedName~PixelPackStateTests|FullyQualifiedName~GlPipeline' -v normal
```

Receipts: `artifacts/engine-state-tests.log`, `artifacts/engine-state-patched-methods.txt`, and `artifacts/engine-gl-inventory.txt`. Tests installing global patches are serialized with the GPU collection and remove their patches in `finally` blocks.

The source review checked the exact-signature boundary, builtin assembly selection, IL operand walking, shader ownership preservation, texture/sampler independence, native deletion semantics, and interaction between indexed and global blend caches. These checks do not claim live engine rendering or a measured speedup.

### Active-unit texture binding follow-up

The engine adapter now uses `BindTextureOnActiveUnit` rather than querying the active unit and calling the explicit-unit binding API. Focused regression coverage verifies no active-unit query or reselection in that path, one native texture bind, known-unit bookkeeping, unknown-unit selective target invalidation, and sampler preservation. All 19 selected engine-switching, inventory, shader-binding and texture-lifetime tests passed with zero skips.

The full build encountered a shader-cache atomic file-move access error; the serialized shader retry was stopped after it stalled. Validation compiled the production C# project with `EnableSpirv=false`, then compiled tests with `BuildProjectReferences=false`, reusing existing shader artifacts. Both C# builds passed. This fix changes no shaders; it does not establish that a fresh shader rebuild succeeds. Receipts: `artifacts/active-texture-fix-production-build.log`, `artifacts/active-texture-fix-build.log`, and `artifacts/active-texture-fix-tests.log`.

### Render API caller rebuilding validation

The standard Debug build passed with zero errors. Both warmed scissor regressions passed individually in fresh test hosts, covering direct API calls and PushScissor/PopScissor before subsequent cache restoration. The broader state and atmosphere selection passed 384 tests with five opt-in measurement skips. Installed coverage verifies 216 render API bodies, repeated application without duplicate transpilers, and owner-scoped removal. Receipts: `artifacts/RenderApiStatePatches/build.log`, `artifacts/RenderApiStatePatches/broad.trx`, and the fresh-host scissor receipts in that directory. No game was launched; live visual acceptance remains user-run.
