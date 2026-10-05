# Declaring owned shaders

Shader compilation uses performance optimization (`-O`) in both Debug and Release builds.
Debug additionally requests debug information (`-g`); Release omits it. The build receipt
includes both settings, so a change in optimization policy invalidates previous shader outputs.

Declare a shader's immutable contract on its owning partial class. The generator automatically includes it in the shared build/runtime catalog; no registration list or special declaration filename is needed.

```csharp
[ShaderProgram("Contract", "example", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "example.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Steps),
    SpecializationId = 4, When = "Enabled")]
internal partial class ExampleShader : GpuProgram
{
    /// <summary>Controls the feature's compiled structural branch.</summary>
    [ShaderOption("EXAMPLE_ENABLED", false)]
    public partial bool Enabled { get; set; }

    /// <summary>Selects the tracing work count when the feature is enabled.</summary>
    [ShaderOption("EXAMPLE_STEPS", 10)]
    public partial int Steps { get; set; }

    internal override GpuShaderContract ProgramContract => Contract;
}
```

Add the referenced GLSL entry points under `assets/vanillagraphicsexpanded/shaders`. Declare resource slots and interface locations through the existing `GpuBindingContract` layout API. `ShaderStage.Layout` overrides the default source-stem layout identity when necessary. The build uses TinyAst and `ShaderSyntaxTreePreprocessor`, then publishes SPIR-V compiler output. It does not infer configurable settings from GLSL macros.

A program must declare every stage, even when it has no configurable options. A compute program declares one `ShaderStageKind.Compute` stage. Repeat `ShaderProgram` and `ShaderStage` attributes for a multi-program owner, using distinct contract member names. The generator emits each named contract and a read-only `Contracts` collection. A runtime family chooses its `ProgramContract` by the active program identity; callers must only update options accepted by that program.

The variant budget is the maximum supported structural assignment count. Booleans use their natural two-value domain; integer and enum structural options require an explicit `Domain`. Numeric specializations do not multiply binaries. Their IDs are explicit, stable and unique within the stage. Defaults and domain/range constants must exactly match the property type. Avoid adding restrictions that reject supported caller values.

Shared options use attributed static get-only `ShaderOption<T>` partial properties. An instance accessor can reference one with `[ShaderOptionReference(typeof(SharedOptions), nameof(SharedOptions.Enabled))]`; its type must match. Shared groups use `ShaderGroup` on their owner and `ShaderAcceptGroup` on consuming shaders. Group names and generated contract member names are string literals because those members do not exist until generation. Option references can use `nameof` on the source-declared properties. Lighting declarations in `LumOn/Shaders/LumOnShaderOptions.cs` and `LumOnShaderGroups.cs` demonstrate this pattern.

`When` references local attributed property names, including shared-option references. It accepts Boolean properties/literals, `!`, `&&`, `||`, parentheses and `Property == constant` with C# precedence. Constants are exact Boolean/int/uint literals or members of the property's enum type. Use a `u` suffix for small uint literals. Enum type names may be simple, namespace-qualified, or `global::`-qualified. No calls, arbitrary member access, arithmetic, casts, option-to-option comparisons or `!=` are accepted. Use `!(Mode == Quality.High)` for inequality. Null/omission is unconditional; empty text is invalid. Every dependency must be structural in the consuming stage. Names inside strings do not receive automatic C# rename support; stale names produce a build error at the attribute.

Generated accessors validate and retain requested settings without scheduling compilation. EnsureReady, explicit preload or activation compares the latest request with the installed configuration, so writes that return to the installed values require no loading. Inactive numeric selections remain stored and become effective when their condition becomes true. Ordinary uniforms, buffer fields and per-frame values do not belong in the option contract. ConfigureOptions groups option edits atomically; runtime input batching occurs separately at submission. Resource-driven helpers may explicitly replace topology with sentinel values; conditional omission itself never discards a selected value.

The settings editor borrows the immutable requested snapshot and copies values only on the
first normalized change. Unchanged batches, including batches that restore every original
selection, retain that snapshot and its projected load plan. Callbacks and individual write
validation still run; nested writes compare against the pending batch, and any failed edit
still aborts publication. Real inactive edits remain stored even when they do not require a
new executable. This reuse does not compare against installed settings or clear preparation
failures/reload invalidation: consumers must still call their existing readiness boundary.
Editor, callback and scalar-validation overhead remains; unchanged batching is not allocation-free.

Compatible reused stages must agree on identity, source, layout, fixed defines and setting uses; they share one immutable stage and binary per structural assignment. A different vertex pairing can reuse the same fragment declaration by repeating its exact uses and accepted groups. The generator rejects conflicting shared declarations. Use separate stage identities and binary assets for genuinely incompatible configurations.

`ShaderProgram.Scope` defaults to `production`. Dedicated fixtures may use an explicit separate scope, such as `build-validation`; they are available to that scope without entering the packaged production inventory. Source-only packaged fixtures still declare their owner in the mod source tree so both compilation paths see them.

The build tool reads ordinary mod sources as semantic inputs and emits declaration-only owners. It never depends on the completed mod assembly. Keep declarations independent of framework-specific C# conditional symbols because the tool and mod target different frameworks. Run generator tests, the normal shader-enabled build, focused contract/source tests, and relevant GPU tests when changing declarations. Inventory tests verify source coverage, shared-stage consistency, supported assignments, and expected binary paths.

The compiler enumerates supported program assignments and builds each distinct projected stage once. Its summary reports program combinations separately from stage binaries. Source enumeration checks that every owned entry point is declared and every registered source exists; identities may differ from source paths, and incompatible source reuse must have distinct binary assets. Fixed and structural values are emitted with their declared types (Boolean preprocessing macros use `0`/`1`); active specialization declarations use stable IDs and declaration defaults. Imported guarded defaults remain in GLSL after the injected configuration, with conditional evaluation left to the compiler.

Global initialized `const` expressions containing parentheses are relaxed by a separate syntax-tree transformation to permit executable initialization. Layout-qualified specialization declarations, function-local constants, comments and nested expression text are preserved. Resource and interface layout editing remains the responsibility of `ShaderSourceLayout`.
Each graphics load captures a `ShaderLoadPlan` before asset reads. Its immutable `ShaderSettings` and projected stage selections supply the binary paths, explicit stage kinds/entry points and exact specialization bits. Successful linking and interface preparation commit the installed settings only if the requested plan still matches. A superseded candidate is discarded; failures retain the old executable, interface and installed snapshot without allowing incompatible activation. The layout object and its UBO state remain stable across reloads. Explicit CompileAndLink and engine reloads force preparation. Demand preparation skips equivalent inputs and cannot resurrect an explicitly retired owner. The engine reload disposal path invalidates the executable while preserving its declaration.

`SetDefine(name, value)` remains a compatibility adapter for declared names and aliases; unknown names and invalid values fail before GPU preparation. Null and `RemoveDefine` restore the canonical default. Equivalent alias writes are no-ops; conflicting values within a supplied settings dictionary are rejected. Inactive values remain requested without reloading until enabled. The setter's Boolean result describes an effective input change, not merely a retained value change.

Compute loading accepts an explicit `ShaderSettings` snapshot. The asset compatibility overload resolves its argument as a program identity; direct-file loading requires settings alongside the path and never infers a stage from the filename. Compute creation prepares a new pipeline and leaves an existing caller-owned pipeline intact on failure. The span loader consumes each selected binary synchronously and does not read source or runtime metadata to reconstruct configuration.

## Supported assignments and diagnostics

Use repeated `ShaderAssignment` attributes when only specific complete structural assignments are supported. Each row must specify every structural option with canonical names and typed values, and the default row must be included. Otherwise the catalog enumerates the Cartesian product. The budget limits program assignments; sharing a stage does not multiply its binary count. Fixed defines belong in `ShaderFixedDefine`, and ordinary include guards or helper macros need no option declaration.

When selection or loading fails, follow the reported owner and stage:

| Symptom | What to inspect |
| --- | --- |
| Generator diagnostic on an attribute | Property type, constant type/domain, condition property names and structural stage uses; fix the declaration before building binaries |
| Unknown option or conflicting aliases | The selected program's accepted keys/groups; an alias and canonical key may coexist only with equal values in one input dictionary |
| Unsupported assignment or budget failure | Complete structural rows, declared defaults and intentional domain size |
| Missing built asset | The selected stage's `BinaryPath` in `ShaderLoadPlan`, the normal shader build result, and the packaged asset path; there is no runtime GLSL fallback |
| Specialization failure | Stage identity, entry point, active specialization values and driver log; inactive values are intentionally omitted |
| Link failure | Declared stage pairing and source-owned interface layouts, then the driver link log |
| Requested change is not visible | Compare requested and installed settings, the last explicit preparation/activation and the last load error; inactive values are retained without reloading and a failed replacement leaves the prior installed generation intact |

Run the normal shader-enabled build before GPU tests so their copied assets match current declarations. `SpirvInventoryTests` enumerates every distinct stage binary and every declared graphics/compute assignment from the resolver. Rendering tests verify behavior separately from successful specialization/linking. The isolated `ShaderBuildTool/Tests/ValidateBuildContract.ps1` checks compilation, incremental invalidation and package rules; full mod packaging is the `Package` task in `CakeBuild`. Build receipts stay outside runtime assets. Do not add binary rewriting, reflection manifests or filename-pairing rules to repair a declaration or packaging error.

## Removed ineffective option writes

Unused temporal velocity-reprojection and rays-per-probe macros are not registered options. The migration removed writes that had no shader consumer: world-probe atlas update/bind switches, PBR composite AO and debug-mode writes, velocity-pass emissive writes, and PIS exploration or world-probe topology writes broadcast to programs that did not use them. CPU update budgets, actual atlas binding, current debug UBO routing and the consuming programs' settings remain separate functionality. An unknown explicit `SetDefine` key now fails instead of silently doing nothing; do not reintroduce an ineffective declaration merely to accept an obsolete writer.

The bent-normal spelling remains an alias of short-range AO. Passing conflicting alias/canonical values together now fails deliberately; sequential setter calls update the same canonical option, and null restores its default. Engine-owned PBR shader patches retain their existing configuration path and are outside this owned-program catalog.

## Retained runtime inputs

Assign shader inputs before Use, UseScope or Dispatch. Unchanged and unassigned values retain their previous value or default. Use invokes contract-generated Submit, which validates all active inputs before publishing any of them. Each active CPU block binds a complete snapshot: changed bytes or an expired allocation require a fresh ring allocation, while an unchanged current-epoch snapshot can be rebound. There is no Prepare callback or CPU batch scope.

Declare borrowed resources through ShaderBinding interfaces. Generated setters retain references; authored getters may expose existing CPU blocks or shared buffers and must perform no GPU work. Attach the owner's mutation guard to owned CPU blocks. Raw texture IDs require ShaderTextureTarget and an explicit sampler policy when the default is unsuitable. Optional non-2D managed samplers also need the declared target so an unassigned value clears the correct texture binding. CPU blocks are copied into the current frame's ring; retained GPU references do not extend resource lifetime. Every sampler and image also needs a UniformLocation declaration in its stage contract: a binding slot alone cannot identify an active uniform in name-free SPIR-V.

Compute owners derive from GpuComputeShader and adopt the existing GpuComputePipeline. Use GpuStorageBufferBinding for exact SSBO ranges and GpuTextureBinding for image levels, layers, access and format. Dispatch owns activation and submission; keep work uploads, clears, barriers and readbacks explicit around it. Liquid engine pool callbacks are the specific exception to application-controlled draw ordering: they stage inputs, and LiquidPoolSubmissionHook establishes the pre-draw publication boundary.

### CPU uniform upload lifetime

`CpuUniformBuffer` tracks a content revision independently of dirty flags. Typed writes advance
it only when packed bytes change; custom writable-span or array writes must call `MarkDirty`.
`PackedUniformBuffer.SetBytes` compares and copies the complete block before marking changes.
Submission borrows the last successfully bound range only when its revision, ring identity,
allocation epoch and live buffer still match. Reuse still calls the existing range-binding owner,
so another shader or slot cannot leave an unrelated block bound. Program reload does not change
the CPU bytes; normal executable validation and submission still apply.

Every `BeginFrame`, including reuse of the same frame index, opens a new allocation epoch.
`EndFrame` closes reuse before inserting its fence, and disposal invalidates the allocator.
No snapshot is reused across these boundaries: a physically live page alone does not prove
that new draws belong to the work covered by its retirement fence. A different current ring
also requires a new upload. Changed bytes always allocate a fresh range, retaining earlier
draw snapshots. Failed allocation or binding leaves dirty work and the previous publication
record intact. CPU-buffer disposal releases the borrowed record without retiring ring storage.

Focused validation covers both persistent and orphaned-buffer rings, same-owner and nested
restoration, shared blocks, changed snapshots, repeated frame indices, page wrap, replacement,
disposal, reload and failed publication. GPU binding/readback and actual draw/compute checks
cover liquid and non-water consumers. Thirty-six distinct cases passed across the initial run
and a targeted rerun correcting two old assertions that required an upload on every activation.
The C# builds passed while reusing unchanged shader artifacts.

A warmed sequence of 32 nested surface/volume scopes previously performed 288 whole-block
allocations and copied 462,336 payload bytes. It now performs zero additional allocations or
copies, while restoring each owner's distinct GPU-visible draw values. Changed blocks and new
epochs still require their first upload. `AllocationsWritten` and `BytesWritten` count successful
copies independently of binding; alignment padding is excluded from bytes. CPU timing varied
between runs, so this establishes copy/allocation savings, not a CPU or GPU speedup. Receipts
are retained in `artifacts/WaterLagAnalysis/uniform-reuse-*`.

Mark a resource optional only when the shader has a valid path without it. For example, PBR composition permits an unavailable GI texture because the renderer also sets indirect intensity to zero. Optional absence clears the binding; it never borrows the previous shader's resource. Required active resources must be valid at use. Do not draw after a failed TryUse or a thrown Use/Dispatch.

UseScope restores the enclosing owner's current retained inputs, including changes made while nested. It retains the owner's identity, not the lifetime of borrowed GPU resources. Disposing the enclosing owner or a required resource can make restoration throw; failed restoration leaves no executable bound. Dispatch already activates and submits, so callers normally stage inputs and call it directly without an extra Use.
