# Engine state integration

Supported engine OpenTK calls are replaced at managed call sites with compiler-checked adapters into
the OpenGL RHI backend. `OpenGlStateCache` owns executed knowledge; `GpuResourceManager` owns canonical
resource facts and lifetime. Neither is a second public rendering command context.

## Patch boundary

[EngineStateSwitchingHook](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateSwitchingHook.cs)
discovers concrete methods and constructors in loaded built-in `VintagestoryLib`, `VSEssentials`,
`VSSurvivalMod` and `VSCreativeMod` assemblies. It replaces only exact static call operands, preserving
evaluated arguments, branch labels and exception blocks. OpenTK, VGE and third-party assemblies are not
rewritten. Native calls inside backend forwarding therefore cannot recurse into interception.

`EngineStateTargets` walks original IL and resolves only candidate native references. Built-in assemblies
must already be loaded; there is no per-frame discovery or automatic third-party interception.
`EngineGenericStateTargets` discovers concrete value-type method specifications, including calls through
virtual base declarations. Harmony receives closed concrete methods only. Open definitions, unresolved
forwarding and shared reference-type instantiations reject explicitly; reflection-created instantiations
remain outside this static discovery boundary.

`EngineStateCallMap` and `NativeAdapters/MetadataAdapterCatalog` match full managed return/parameter types,
method names and generic constraints against ordinary compiled C# adapters. Closed generic adapters use
the caller's type arguments. There is no generated adapter IL or command-name pattern dispatcher.
Unsupported calls remain unchanged, including calls replaced by another transpiler.

## Supported operations

Only GL (named Core in adapter files), GL.Ext and GL.Arb owners participate. Other auxiliary owners,
matrix stacks, display lists, attribute stacks, client defaults/legacy arrays, alpha testing, point
smoothing, separate polygon face modes, stipple and automatic mip generation are excluded. Generic
Enable/Disable preserves unsupported capability values unchanged without inventing cached metadata.
See the [API coverage](Rendering.GpuResourceHandles.ApiCoverage.md) for the complete retained families,
overloads and exclusions. Current engine call frequency does not narrow that support obligation.

## Execution and reconciliation

Exact overloads execute once in focused `Rendering/RHI/Backends/OpenGL` external modules. They preserve
original arguments, side effects and native error behavior. They do not enter command lists, suppress
known-equal calls, clamp values or substitute managed validation for native rejection.
Fixed-function adapters live in `State/External/EngineStateCalls`; resource/storage/executable/vertex
and routing adapters live with their owning backend family. Managed operations use core OpenGL4 4.5
implementations separately, sharing observation, metadata and knowledge services rather than an executor.

Fixed-state calls admit their precise field footprint and withdraw affected knowledge before native dispatch. Resource operations retain
original associations and use existing observation/publication once, including interruption. Content
updates never become storage metadata. Reserved generated names, proxy targets, exact unsigned/ref/
pointer/generic overloads and per-object deletion outcomes retain their native semantics.

Bindings distinguish active texture selection, per-target texture slots, samplers, images and indexed
buffer ranges. A native texture bind never queries or reselects the active unit. Unknown effects remain
unknown for explicit/lazy recovery; registry membership does not extend interception coverage.

Deletion reconciles only affected associations and selectors. Program deletion preserves native deferred
destruction of a selected executable; managed retirement retains its distinct unbinding contract. Queued
retirement uses captured context and registration, never a reused numeric name. See [resource ownership](Rendering.GpuResources.md).

## Selective invalidation and restoration

`OpenGlStateCache.Invalidate(EPipelineState)` changes knowledge only. Categories remain independent,
with explicit dependencies for indexed blend values, VAO element bindings, feedback outputs and
framebuffer routing. `None` is a no-op; undefined mask bits reject. Warm managed transitions can suppress
known-equal effects, while exact engine calls always execute.

An RHI boundary declares all permitted pipelines and helper-state coverage before mutation. Existing
selective capture/restoration preserves known native engine state, rejects undeclared changes and
invalidates failed restoration fields. No arbitrary callback execution API belongs to the cache.
See [boundary lifetime](Rendering.AuthoritativePipelineState.md#shader-ownership-and-boundary-lifetime).

## Previously compiled render API callers

`EngineRenderApiStatePatches` rebuilds declared methods/instance constructors in the engine IRenderAPI
hierarchy and explicit existing menu/render caller types after native call replacements. This addresses
platform operations inlined before mod startup. Reapplication rebuilds existing patches without
duplicating the owner transpiler. Third-party implementations, abstract/open bodies, static initializers
and unlisted higher callers remain excluded. No runtime call graph or per-frame scan is introduced.

## Verification boundary

Exact-signature/IL and concrete-generic tests check rewriting. Headless tests execute representative
adapters and verify native effects, errors, exactly-once dispatch, selector restoration and cache agreement.
Global patch fixtures serialize and remove observers during cleanup. Installed-client rendering and
warmed menu behavior require user-run evidence; server-only CI cannot qualify those client classes.
