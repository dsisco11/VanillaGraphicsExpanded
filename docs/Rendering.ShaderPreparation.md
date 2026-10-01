# Shader input submission contract

Status: implementation and lifecycle validation are complete for graphics, compute and liquid pool boundaries. The [checklist](Rendering.ShaderPreparation.todo) records completion evidence; the [inventory](Rendering.ShaderPreparation.Inventory.md) identifies owners and explicit execution adapters. Three broader-suite failures remain documented below.

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

`UseScope` submits on entry even for same-owner nesting. On exit it restores a previous VGE owner's current retained resources by invoking its use path, without rolling back input edits. Scopes capture the enclosing owner when entered. Disposing that owner before scope exit makes restoration fail closed instead of rebinding its retired GL identifier. Foreign engine owners retain their existing activation convention; their callers remain responsible for resources. Interface and engine-base activation route through the same owned submission path. Reload replaces the executable while retaining inputs; terminal retirement rejects future activation.

## Execution boundaries

Graphics callers assign retained inputs before use and draw. CPU UBO source getters and shared resources participate in generated submission; production owners have no compatibility submission bodies.

Compute owners submit before dispatch while preserving barriers, counters, clears, readbacks and command uploads as explicit execution operations. Low-level pipelines continue owning executable lifetime. Production compute callers use the retained owner's Dispatch or DispatchIndirect boundary.

Liquid engine callbacks retain origin/model-view/transparency between mesh-pool draws. The owner-specific pre-draw hook submits these values immediately before the engine draws each pool. Vanilla engine shaders remain explicit adapters with their existing resource-restoration responsibilities.

## Verification

The contract is checked against actual driver bindings and submitted bytes, as well as generated source:

| Requirement | Coverage |
| --- | --- |
| Retained defaults and partial assignments; no setter uploads; one active CPU block per use | `ShaderInputSubmissionTests`, `UniformBufferCallerTests`, `CpuUniformBufferTests` |
| Required, optional and optimized-out resources; failure and retry | `ShaderInputSubmissionTests`, `ComputeInputSubmissionTests`, `PbrModeLifecycleTests` |
| Nested scopes, engine routing and fail-closed restoration | `GpuProgramUseScopeTests`, `ComputeInputSubmissionTests`, `AtmosphereGpuComputationTests` |
| Reload, retirement and demand preparation | `ShaderDemandPreparationTests`, `ProductionShaderAccessorGpuTests`, `SpirvGraphicsLifecycleTests` |
| Frame rollover and persistent/fallback ring paths | `ShaderInputSubmissionTests`, `GpuUniformRingBufferIntegrationTests`, `TestUniformRingRetirementTests` |
| Generated source coverage, numeric locations and exact resource slots | `RuntimeSubmissionTests`, `ShaderBindingMigrationTests` |
| Engine liquid pre-draw submission | `LiquidShaderProgramTests` and liquid rendering tests |

Tests assert allocation counts and GPU-visible values; they do not establish frame-time savings. Headless validation does not replace user-run game rendering acceptance.

## Compute ranges and engine pool draws

GpuStorageBufferBinding retains a borrowed buffer plus its exact initialized offset and size. This preserves runtime array lengths for queued queries, trace work and completion records. Generated validation runs before any publication. Atomic-counter resources declare their buffer slot through ShaderBindingKind.AtomicCounter; the active executable is queried by slot because SPIR-V does not guarantee uniform lookup names.

Liquid engine Uniform callbacks retain origin, model-view and transparency in the draw block. LiquidPoolSubmissionHook submits after those callbacks and immediately before each MeshDataPool.RenderMesh draw. Restoration writes remain retained for the next pool. Direct test or application draws must call Use after their assignments. Counter clears, timestamp admission, barriers and readbacks are explicit operations; they are not setters.

## Validation findings

Lifetime tests exposed a restoration defect when an enclosing compute owner was disposed during a nested graphics or compute scope. Both scope types now capture that owner at entry and fail closed on restoration after retirement. The regression checks the exception and the absence of a bound executable.

PBR composition legitimately has no indirect texture before a lighting provider publishes, or after that provider withdraws. Its binding is now optional; the renderer disables indirect intensity on that path. The lifecycle regression preserves its numerical output checks and verifies that the absent sampler clears the old binding.

The persistent/fallback tests initialize capability detection and assert the actual allocation path. Both paths passed in the headless context, including GPU-visible retained bytes after reload and frame rollover. Multiple parameter assignments followed by one use publish one active CPU block.

Broader validation also reconciles stale fixtures with the current documented contract: explicit demand preparation instead of queued reload callbacks, hemispherical sky response without an independent solar halo, current debug/registration inventories, expanded engine imports and current terrain/surface signatures, depth-capable texture allocation, and isolated GL state. These fixture changes do not establish live rendering or performance results.

The focused [boundary run](../VanillaGraphicsExpanded.Tests/TestResults/contract-validation-boundaries.trx) passed 28/28 with no skips, including both actual ring paths. The [generator suite](../ShaderContractGenerator.Tests/TestResults/contract-validation-generator.trx) passed 150/150 with no skips.

The final shader-enabled Debug build passed with zero errors and 101 warnings. The [unfiltered main suite](../VanillaGraphicsExpanded.Tests/TestResults/contract-validation-final.trx) ran 4,502 tests: 4,482 passed, 3 failed and 17 were skipped. No test filter or new skip hides the residuals below. Second source review and an independent contract audit found the lifecycle requirements satisfied. No game was launched and no frame-time improvement was measured.

### Broader-suite residuals

The following assertions remain unchanged. Isolated runs reproduced them; their baseline status and root causes are not established by this work. They do not justify changing binding policy or weakening numerical expectations:

| Test | Observed mismatch |
| --- | --- |
| `MaterialAtlasCacheKeyStabilityTests.CacheKeys_AreStable_AcrossCalls_AndCultureIndependent` | Toggling `EnableNormalMaps` leaves the material-parameter cache key unchanged; the test expects a different key. Current key inputs use the aggregate `RequiresNormalDepthAtlas` policy. |
| `MaterialDisplacementTests.SubdivisionSettingsSanitizeNonFiniteAndOutOfRangeValues` | A non-finite target edge size sanitizes to the current default of 8; the test expects 16. |
| `SurfaceLightingSpatialRuntimeTests.MixedConsumersFollowReplacementPolicy(sh9: true)` | At yaw 1 after increasing probe spacing, final RGB is zero despite nonzero projected coefficients, a populated world atlas and a published output. The non-SH9 case passes. The cause remains unconfirmed. |

The last case needs a separate SH9 gather/replacement investigation. Headless submission tests establish publication and lifetime behavior; they do not establish that every lighting algorithm produces the expected image.
