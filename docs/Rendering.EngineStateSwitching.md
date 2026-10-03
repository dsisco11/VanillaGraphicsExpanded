# Engine state switching through the cache

The engine's supported native state calls are replaced at their managed call sites with adapters into `GlStateCache`. This is an integration change to the existing cache, independent of the approved authoritative-pipeline proposal.

## Patch boundary

[EngineStateSwitchingHook](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateSwitchingHook.cs) participates in the existing client mod's `Harmony.PatchAll()` startup. It discovers concrete methods and constructors in the loaded `VintagestoryLib`, `VSEssentials`, `VSSurvivalMod`, and `VSCreativeMod` assemblies whose original IL contains a mapped static OpenGL call. OpenTK itself, VGE, and third-party mods are not patched. Cache operations therefore still reach the original native functions without recursion.

[EngineStateTargets](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateTargets.cs) scans metadata and walks IL instruction boundaries before resolving candidate callers. This avoids loading unrelated optional dependencies or resolving unrelated method references during discovery. Built-in assemblies must already be loaded when the mod's startup patches run; late-loaded assemblies are not automatically patched. A generic caller containing a supported call is explicitly rejected rather than silently omitted.

[EngineStateCallMap](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateCallMap.cs) resolves adapters by exact method name, parameter types, and return type. An adapter without a matching native signature fails map initialization. The transpiler replaces the call operand only, preserving evaluated arguments, instruction count, branch labels and exception blocks. Unsupported methods are left unchanged. Another transpiler's replacement is not forcibly overwritten.

## Supported operations

[EngineStateCalls](../VanillaGraphicsExpanded/Rendering/EngineStateCalls.cs) supplies signature-compatible adapters for:

- Enable/disable of depth testing, culling, scissor testing and global/indexed blending. Other capability values pass through to OpenGL.
- Depth comparison/write mask, global/indexed blend factors including separate RGB/alpha factors, global color mask, line width and point size.
- Executable, program-pipeline, VAO, framebuffer, renderbuffer and transform-feedback binding.
- Active texture unit, texture and sampler binding. Native texture-unit enums become zero-based cache units; texture binding uses the active unit and does not alter sampler ownership.
- Generic buffer, indexed buffer base/range, and image-view binding.
- Pixel-store changes, patch vertex count and the cached provoking-vertex convention.
- Engine texture/buffer retirement and deletion invalidation for framebuffer, sampler, VAO and program bindings, including the engine's by-reference bulk buffer deletion overload.

Only exact mapped overloads are substituted. The installed-engine coverage test exercises discovered targets; adapters for currently unused operations provide coverage when those exact calls appear in the engine. This is not a claim that every OpenTK overload is supported.

The cache does not currently own viewport, scissor rectangles, blend equations, stencil configuration, vertex attribute formats or depth range. Those calls remain native. Capability adapters similarly preserve untracked capability behavior rather than extending the cache's fixed-function state model.

## Cache consistency fixes

An indexed blend mutation invalidates the cached global blend value. Otherwise a later global restore to the previous value could be skipped even though one output differs. Tests exercise global-to-indexed-to-global transitions against actual driver state.

Binding a previously unobserved VAO no longer invents a zero element-buffer association. The engine may have populated that VAO before cache observation; its EBO remains unknown until queried or bound through the cache.

Line width and point size use exact equality rather than approximate equality, so forwarding engine calls does not suppress distinct requested values.

Deletion adapters preserve native deletion semantics and invalidate affected cached knowledge. Program deletion does not assume immediate unbinding: OpenGL may retain a deleted current executable until it is unbound. Bulk/less-frequent deletion paths conservatively invalidate binding snapshots; they do not reset unrelated fixed-function state.

Single-field pixel-store adapters preserve native overload behavior and invalidate the aggregate pack snapshot instead of querying and replaying unrelated fields.

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
