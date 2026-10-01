# Shader input submission contract

Status: migration implementation covers graphics, compute and liquid pool boundaries. Validation remains tracked in the [checklist](Rendering.ShaderPreparation.todo); the [inventory](Rendering.ShaderPreparation.Inventory.md) identifies owners and remaining explicit execution adapters.

## Assignments and publication

Shader input properties retain CPU values or borrowed resource references. Assignments have no GPU effects. There is no `Prepare` callback or editing scope: unassigned inputs keep their defaults or previously assigned values until explicitly changed. Runtime edits are in-place and are not rolled back on an exception.

```csharp
shader.UpsampleDepthSigma = 0.5f;
shader.HoleFillRadius = 2;
if (shader.TryUse())
{
    // Draw using the complete retained input set.
}
```

The example assumes other required resources were assigned earlier. Every `Use` ensures the current executable is ready, activates it, updates the program cache, and invokes `Submit`. Repeated use of the same owner still submits. Changing an input after use takes effect on the next use. No setter may upload a partial parameter snapshot.

The base class rejects mutation during submission and recursive activation. Resource owners retain disposal responsibility: keeping a reference does not extend a GPU object's lifetime. CPU array inputs must copy supplied values so later caller mutation cannot alter retained state implicitly.

Compile-time options keep the existing `ConfigureOptions` transaction. Callers may use it explicitly to publish several option edits atomically; ordinary runtime assignments do not create option transactions. Options must be set before `Use` chooses the executable. Preserve demand-loading, failed-retry suppression and retirement/reload behavior in [GPU.ShaderDemandLoading.md](GPU.ShaderDemandLoading.md).

## Generated binding ownership

The existing interface `ShaderBinding` declaration remains authoritative for the shader resource name, kind, slot, stage and required policy. It also connects that binding to its runtime value:

- A generated resource property stores its assigned value for publication.
- An authored getter supplies an existing runtime source, including a shared GPU buffer or an owned `CpuUniformBuffer`.
- A CPU block getter publishes one complete ring snapshot when that block is active. Existing CPU packing and dirty tracking are preserved. Clean bytes alone never suppress a required upload.
- A raw texture ID declares a contract-owned `ShaderTextureTarget` enum value and optional `ShaderSamplerPolicy` enum value on its binding attribute. Texture-target values match OpenTK's underlying constants, allowing a direct runtime cast without an offline OpenTK dependency. The generator validates the declared enum constants and does not infer policy from arbitrary setter code.

For example, the upsample parameter binding is still `VgeLumOnUpsampleParamsUBO` at the existing object slot. Its interface property now returns `CpuUniformBuffer`; the explicit implementation returns the existing packed `LumOnUpsampleParamsUbo`. No second block name or slot declaration is introduced.

The generator emits `protected override void Submit()` for concrete graphics and GpuComputeShader owners. It snapshots runtime sources once, validates the whole set, then publishes each binding through the installed layout. It rejects authored write-only resources and resource descriptors without runtime sources instead of silently omitting those bindings. An authored source getter must be CPU-only. Shared inputs appear as inherited or explicit contract getters, so generated submission includes them through the same mechanism.

Production owners no longer use compatibility Submit overrides. Static catalog-only declarations do not acquire runtime submission. GpuComputeShader adopts an existing GpuComputePipeline and preserves its preparation policy; Dispatch and DispatchIndirect invoke submission immediately before executing work. Compute scopes restore the enclosing owned shader through submission.

## Failure and restoration

Required active resources must be valid before publication. Optimized-out resources are skipped. Optional absent resources explicitly clear their resolved binding instead of inheriting an unrelated owner's resource. Texture IDs and resource lifetimes are checked at use, not during assignment. An unavailable ring fails use when an active CPU block needs uploading.

GPU submission is not transactional. `Use` throws and `TryUse` returns false on failure; no draw may follow failure. Clear failed activation, keep retained inputs, and allow retry. Dirty state is consumed only by successful CPU block publication. Same-frame allocation caching and cross-frame upload reuse are excluded.

`UseScope` submits on entry even for same-owner nesting. On exit it restores a previous VGE owner's current retained resources by invoking its use path, without rolling back input edits. Restoration failure fails closed. Foreign engine owners retain their existing activation convention; their callers remain responsible for resources. Interface and engine-base activation route through the same owned submission path. Reload replaces the executable while retaining inputs; terminal retirement rejects future activation.

## Execution boundaries

Graphics callers assign retained inputs before use and draw. CPU UBO source getters and shared resources participate in generated submission; production owners have no compatibility submission bodies.

Compute owners submit before dispatch while preserving barriers, counters, clears, readbacks and command uploads as explicit execution operations. Low-level pipelines continue owning executable lifetime. Production compute callers use the retained owner's Dispatch or DispatchIndirect boundary.

Liquid engine callbacks retain origin/model-view/transparency between mesh-pool draws. The owner-specific pre-draw hook submits these values immediately before the engine draws each pool. Vanilla engine shaders remain explicit adapters with their existing resource-restoration responsibilities.
## Verification

Focused tests must cover ordinary retained assignments, no assignment-time GPU calls, generated resource sources and diagnostics, one active CPU-block publication per use, missing/optional/optimized-out inputs, upload failure and retry, option transactions, recursive submission and mutation rejection, engine routing, actual resource restoration, reload and ring rollover. Both persistent and fallback ring paths are required. Upload counts demonstrate batching, not measured frame-time savings. Game rendering acceptance remains user-run.

The revised shared lifecycle and reference consumers passed a Debug build, 97 distinct focused CPU/GPU tests (95 consolidated plus two ring-integration tests), and 140 generator tests, with no skips or failures. This includes the affected upsample/probe-anchor functional tests, full lighting-pipeline rendering test, and generated sampler/image dispatch readback. Second review and independent source audit passed for this scope. Evidence: [CPU/GPU results](../artifacts/submission-tests/submission-final.trx), [ring results](../artifacts/submission-tests/ring-final.trx), [generator results](../artifacts/submission-tests/generator-final.trx). No game process was launched.

## Compute ranges and engine pool draws

GpuStorageBufferBinding retains a borrowed buffer plus its exact initialized offset and size. This preserves runtime array lengths for queued queries, trace work and completion records. Generated validation runs before any publication. Atomic-counter resources declare their buffer slot through ShaderBindingKind.AtomicCounter; the active executable is queried by slot because SPIR-V does not guarantee uniform lookup names.

Liquid engine Uniform callbacks retain origin, model-view and transparency in the draw block. LiquidPoolSubmissionHook submits after those callbacks and immediately before each MeshDataPool.RenderMesh draw. Restoration writes remain retained for the next pool. Direct test or application draws must call Use after their assignments. Counter clears, timestamp admission, barriers and readbacks are explicit operations; they are not setters.

## Migration validation

The migrated graphics and compute families passed 856 focused CPU/GPU tests with four existing skips (one opt-in measurement, SH integration stability, and trace distance/tint expectations). All 150 generator tests passed. A separate fresh-process run passed 60 world-history, surface-lighting and near-field checks after adding missing numeric texture uniform locations. The production-wide metadata guard now rejects that omission; the exact binding snapshot includes atomic counters. The shader-enabled build regenerated 398 variants.

Evidence: [focused results](../VanillaGraphicsExpanded.Tests/TestResults/migration-final-focused.trx), [surface and history results](../VanillaGraphicsExpanded.Tests/TestResults/migration-metadata-runtime.trx), [generator results](../ShaderContractGenerator.Tests/TestResults/migration-generator-final.trx). These are focused migration results, not a clean full-suite claim. Broader validation remains open in the checklist. No game process was launched and no frame-time improvement was measured.

The final Debug rebuild and [28 atmosphere/restoration tests](../VanillaGraphicsExpanded.Tests/TestResults/migration-atmosphere-final.trx) passed after removing cleanup that overwrote restored SSBO bindings. Second source review and independent test/inventory review found no remaining migration gaps.

The interrupted broader GPU run also reported trace sun/registration expectations, accessor reload-queue expectations, PBR mode lifecycle, installed-engine GLSL/depth fixtures, terrain GLSL variables and a raw SPIR-V GL error. Those cases were excluded from focused migration validation and are recorded for follow-up; their baseline status has not been established. Across the three runtime result files, 911 distinct tests passed.
