# Shader input submission contract

Status: shared graphics activation and generated submission are implemented for upsample and probe anchor. Other graphics owners, compute wrappers and engine draw adapters remain in the [migration inventory](Rendering.ShaderPreparation.Inventory.md) and [checklist](Rendering.ShaderPreparation.todo).

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

The generator emits `protected override void Submit()` for concrete graphics owners without an authored override. It snapshots runtime sources once, validates the whole set, then publishes each binding through the installed layout. It rejects authored write-only resources and resource descriptors without runtime sources instead of silently omitting those bindings. An authored source getter must be CPU-only. Shared inputs appear as inherited or explicit contract getters, so generated submission includes them through the same mechanism.

Unmigrated owners temporarily retain their explicit compatibility `Submit` overrides and immediate setter behavior. Those overrides opt out of automatic submission until their resource sources and callers are migrated. They are migration work, not evidence of complete contract adoption. Static catalog/offline declarations do not acquire runtime submission. Compute wrappers require their own runtime owner boundary before automatic publication can replace their current dispatch code.

## Failure and restoration

Required active resources must be valid before publication. Optimized-out resources are skipped. Optional absent resources explicitly clear their resolved binding instead of inheriting an unrelated owner's resource. Texture IDs and resource lifetimes are checked at use, not during assignment. An unavailable ring fails use when an active CPU block needs uploading.

GPU submission is not transactional. `Use` throws and `TryUse` returns false on failure; no draw may follow failure. Clear failed activation, keep retained inputs, and allow retry. Dirty state is consumed only by successful CPU block publication. Same-frame allocation caching and cross-frame upload reuse are excluded.

`UseScope` submits on entry even for same-owner nesting. On exit it restores a previous VGE owner's current retained resources by invoking its use path, without rolling back input edits. Restoration failure fails closed. Foreign engine owners retain their existing activation convention; their callers remain responsible for resources. Interface and engine-base activation route through the same owned submission path. Reload replaces the executable while retaining inputs; terminal retirement rejects future activation.

## Remaining migration boundaries

Graphics callers must assign inputs before use and draw. Generated accessors alone do not migrate authored setters. Convert CPU UBO source getters and shared resources alongside each owner, then remove compatibility submission bodies.

Compute owners must submit before dispatch while preserving barriers, counters, clears, readbacks and command uploads as explicit execution operations. Low-level pipelines continue owning executable lifetime. A bound-dispatch shortcut requires a successfully submitted, still-current owner and no subsequent edits or binding disturbances.

Liquid engine callbacks can change origin/model-view/transparency between mesh-pool draws. Establish and verify an owner-specific pre-draw submission bridge before removing their existing publication. Vanilla engine shaders remain explicit adapters, not generated VGE owners. Do not claim arbitrary foreign state restoration.

## Verification

Focused tests must cover ordinary retained assignments, no assignment-time GPU calls, generated resource sources and diagnostics, one active CPU-block publication per use, missing/optional/optimized-out inputs, upload failure and retry, option transactions, recursive submission and mutation rejection, engine routing, actual resource restoration, reload and ring rollover. Both persistent and fallback ring paths are required. Upload counts demonstrate batching, not measured frame-time savings. Game rendering acceptance remains user-run.

The revised shared lifecycle and reference consumers passed a Debug build, 97 distinct focused CPU/GPU tests (95 consolidated plus two ring-integration tests), and 140 generator tests, with no skips or failures. This includes the affected upsample/probe-anchor functional tests, full lighting-pipeline rendering test, and generated sampler/image dispatch readback. Second review and independent source audit passed for this scope. Evidence: [CPU/GPU results](../artifacts/submission-tests/submission-final.trx), [ring results](../artifacts/submission-tests/ring-final.trx), [generator results](../artifacts/submission-tests/generator-final.trx). No game process was launched.
