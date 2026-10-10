# Declaring owned shaders

Graphics shaders use `#version 330 core` as their source-language baseline. The build preserves
each authored version instead of silently promoting it to 450. Stages that need a higher language
version, such as the existing 430 compute shaders, declare that requirement explicitly.

The SPIR-V build enables `GL_ARB_shading_language_420pack`, `GL_ARB_separate_shader_objects`,
and `GL_ARB_explicit_uniform_location` for the explicit bindings and locations emitted by the
shared interface contract, plus `GL_EXT_control_flow_attributes` for loop/branch attributes.
Other newer language features require a source-declared extension or an explicitly higher version.
This is a GLSL source baseline with extensions, not a claim of compatibility with an unextended
OpenGL 3.3 runtime: owned programs still use the existing SPIR-V loading path and resource capabilities.
The compiler's `opengl4.5` target environment is separate from the source `#version` and remains unchanged.

Shader compilation uses performance optimization (`-O`) in both Debug and Release builds.
Debug requests debug information (`-g`); Release omits it. The build receipt includes both
settings, so compiler-policy changes invalidate previous outputs.

Owned shader numeric inputs use explicit std140 uniform blocks. Shared vertex/fragment values
use one block declaration and one publication; the eye-relative and solar raster fixtures verify
this under stripped Release. Retaining debug metadata or duplicating shared values is unnecessary.
Typed setters write owned CpuUniformBuffer storage, and a ShaderBinding UniformBlock getter supplies
it to prepared submission. Numeric UniformLocation properties are rejected by the generator, and
linked owned executables reject active standalone numeric uniforms. Sampler/image locations remain
resource metadata; specialization constants remain structural selections.

Camera consumers import `includes/vge_frame_ubo.glsl` and declare the matching typed
`VgeFrameUBO` binding at `GpuBindingRegistry.Ubo.Frame` (12). The retained
`VgeFrameUniformBuffer` snapshot supplies projection, view, their inverses, previous/current
view-projection, inverse current view-projection, full-view pixel dimensions, time, an exact
unsigned frame index, camera position, fog, clip planes, frame duration and a precise render-origin bridge. Its std140 layout is 544 bytes;
`screenSize`, `timeSeconds` and `frameIndex` occupy offsets 384, 392 and 396.
The camera position occupies 400 and `fogMinimum` occupies its following scalar at 412.
Fog color/density remain in `fog0`; effects reuse these fields instead of uploading private fog copies.
The near/far clip pair occupies 496; `deltaTime` occupies 504, followed by unsigned `frameFlags` at 508. Bit zero marks a camera cut; temporal consumers
reject prior camera history on startup, world/view discontinuities and long frame gaps.
Camera adaptation reads that shared duration rather than republishing it with effect settings.
The integer chunk origin at 512 and bounded block remainder at 528 restore camera-relative
positions without subtracting large floating-point world coordinates.

The world camera owner publishes before sky and opaque lighting, independently of lighting mode.
Shaders reuse that snapshot instead of capturing matrices or viewport dimensions in their effect
blocks. Alternate-view fixtures supply their own actual frame snapshot through the typed binding.
Prepared submissions use the existing versioned uniform allocator, so unchanged snapshots reuse
the same publication and later writes cannot replace storage already referenced by submitted draws.
`screenSize` describes the source view; reduced-resolution target dimensions, pool/model transforms
and clocks specific to an effect remain in their owning effect or draw contract.
Engine mesh-pool `modelViewMatrix` is a combined per-draw transform: mini-dimensions replace it
with their object transform composed with the camera and restore it after drawing. It is not
interchangeable with the shared world view matrix; preserve that object-space contract and
light-specific shadow transforms. Owned liquid shaders adapt native combined matrices into an
object-only draw block using the shared inverse view, then compose that object transform with
`vgeFrame.viewMatrix` in the shader. Ordinary pools and native restoration writes retain an exact
identity object transform, so each pool no longer republishes the common camera matrix.
Terrain subdivision retains a frozen angular metric captured before shadow rendering so shadow
and visible edges choose identical subdivision. Its effect contract carries only the target edge
size, subdivision cap and fade parameters; visible projection fallback reads shared frame dimensions.
Shadow rendering does not consume an unpublished world camera.
Random sampling tied to rendering uses the shared unsigned
`frameIndex`, including compute consumers; work budgets and source-image dimensions stay local.

Shared-frame migration validation on 2026-10-09 passed four focused batches: 21 liquid
transform/interface checks, 86 lifecycle/binding/spatial checks, 35 native draw/water/compute/solar
consumer checks and 88 tessellation/displacement checks. The latter includes shadow execution
with no world-frame binding and both lighting modes. Receipts are in
`artifacts/frame-ubo-validation/`: `liquid-object-transform-windows-env.log`,
`frame-lifecycle-resumed.log`, `frame-consumers-final.log` and
`tessellation-shared-viewport-final.log`. The final incremental build reused all 438 production
and 507 test shader variants with no compiler invocations. These are headless functional
checks; they do not establish in-game visual acceptance or GPU frame cost.

Dynamic point-light consumers import `includes/vge_lights_ubo.glsl` and bind the same
`VgeLightsUBO` snapshot at `GpuBindingRegistry.Ubo.Lights` (16). Its fixed std140 contract
supports the engine's complete 100-light list: an unsigned light count at 0 with alignment padding, padded view-space
positions at 16 and padded colors at 1616, for 3216 bytes. Positions retain the collector's
world-to-view conversion and colors retain its HSV conversion and intensity calibration.
The shader's inverse-square attenuation and surface response remain unchanged; there is no
additional intensity multiplier or coordinate conversion in the buffer.

`VgeLightsRenderer` withdraws the snapshot at Before and captures once at early Opaque,
after engine collection at Before 0.1 and held attachment resolution at Before 0.45. It is
owned independently of LumOn. Deferred, forward and liquid shading reuse the same immutable
publication; their effect blocks no longer contain light arrays or light counts. The native
shader-use hook restores both shared slots for world consumers and preserves GUI/offscreen
route gating. Explicit alternate views supply their own light snapshot in the receiver's view
space through typed `LightsInputs`; they never mutate the world publication.
Solar irradiance/direction, block-light color lookup tables, probe irradiance and surface-cache
lighting parameters represent different sources and retain their own contracts. Debug and test
consumers use the same shared light schema instead of duplicating it. The engine collector
and its native GUI/offscreen shading retain their original APIs; VGE world shading does not
read or republish those standalone light uniforms.

Shared-light migration validation on 2026-10-09 passed 106 packing, lifecycle, direct/forward,
liquid and held-light checks plus 137 water, native-binding and registered lighting-path checks.
Coverage includes all 100 supported lights, alternate-view/world-origin invariance, both lighting
modes, no additional upload on repeated activation and old GPU slices surviving new publication.
Receipts are `artifacts/lights-ubo-validation/focused-shared-lights.log` and
`artifacts/lights-ubo-validation/runtime-water-shared-lights-final.log`. The final incremental build
reused all 438 production and 507 test shader variants. These headless checks do not establish
in-game visual acceptance or GPU frame cost.

The approved engine GLSL compatibility exceptions retain their existing numeric APIs and activation
behavior, including VGE-added atmosphere, scene-color and terrain inputs. They do not extend the
world-frame publication epoch. See the [migration inventory](Rendering.AuthoritativePipelineState.md#standalone-numeric-input-migration-inventory)
for owners and test-only rejection/source-processing exceptions.

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

## Incremental shader builds

Normal builds reuse validated source, variant, compiler and interface records. Editing a shader
processes that root and its variants; editing an imported file processes its recorded dependents.
TinyPreprocessor discovers those relationships during expansion. A declaration-only edit selects
variants whose effective contracts changed without invalidating unrelated compiler results.
The build still enumerates the catalogue and verifies inputs and outputs.

Read the roots expanded, variants emitted, compilerInvocations and interfaces extracted counters
to see actual work. An unchanged build reports zero for all four; selected-variant progress does
not imply that the whole catalogue is compiled. Reverting an edit can reuse historical results.
Avoid routine clean builds: explicit clean discards reusable state and forces regeneration.

Use `-p:SpirvVerifyContents=true` when checking edits that preserve file size and timestamps.
Normal hashing uses file metadata and can miss those edits. Debug and Release have separate
caches; `SpirvArtifactsDir` overrides their location. Copy the complete active binary/manifest
set after a successful build, leaving private caches, receipts and recovery directories out of
runtime assets. See [incremental build behavior](ShaderBuild.Incremental.md) for migration,
repair, publication recovery, hard-link fallback, command options and measured build costs.

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

Run the normal shader-enabled build before GPU tests so their copied assets match current declarations. `SpirvInventoryTests` enumerates every distinct stage binary and every declared graphics/compute assignment from the resolver. Rendering tests verify behavior separately from successful specialization/linking. `ShaderBuildTool/Tests/ValidateBuildContract.ps1 -Configuration Debug` (or `Release`) runs the maintained integration tests for compilation, selective reuse, repair, publication and runtime manifest compatibility against isolated fixtures. Run the normal shader-enabled mod build to check MSBuild asset copying; full mod packaging is the `Package` task in `CakeBuild`. Build receipts stay outside runtime assets. The existing spirv-digests.json also carries versioned compiler interface declarations associated with each exact binary. Deploy this manifest with its matching binaries; linked activity and driver resource addresses remain runtime responsibilities. Do not add binary rewriting, parallel metadata manifests or filename-pairing rules to repair a declaration or packaging error.

## Removed ineffective option writes

Unused temporal velocity-reprojection and rays-per-probe macros are not registered options. The migration removed writes that had no shader consumer: world-probe atlas update/bind switches, PBR composite AO and debug-mode writes, velocity-pass emissive writes, and PIS exploration or world-probe topology writes broadcast to programs that did not use them. CPU update budgets, actual atlas binding, current debug UBO routing and the consuming programs' settings remain separate functionality. An unknown explicit `SetDefine` key now fails instead of silently doing nothing; do not reintroduce an ineffective declaration merely to accept an obsolete writer.

The bent-normal spelling remains an alias of short-range AO. Passing conflicting alias/canonical values together now fails deliberately; sequential setter calls update the same canonical option, and null restores its default. Engine-owned PBR shader patches retain their existing configuration path and are outside this owned-program catalog.

## Retained runtime inputs

Assign shader inputs before Use, UseScope or Dispatch. Unchanged and unassigned values retain their previous value or default. Use invokes contract-generated Submit, which validates all active inputs before publishing any of them. Each active CPU block binds a complete snapshot: changed bytes or an expired allocation require a fresh version, while unchanged contents reuse storage permitted by their declared lifetime. There is no Prepare callback or CPU batch scope.

Declare borrowed resources through ShaderBinding interfaces. Generated setters retain references; authored getters may expose existing CPU blocks or shared buffers and must perform no GPU work. Attach the owner's mutation guard to owned CPU blocks. Raw texture IDs require ShaderTextureTarget and an explicit sampler policy when the default is unsuitable. Optional non-2D managed samplers also need the declared target so an unassigned value clears the correct texture binding. CPU block publication follows its instance lifetime policy; retained GPU references do not transfer resource ownership. Every sampler and image also needs a UniformLocation declaration in its stage contract: a binding slot alone cannot identify an active uniform in name-free SPIR-V.

Compute owners derive from GpuComputeShader and adopt the existing GpuComputePipeline. Use GpuStorageBufferBinding for exact SSBO ranges and GpuTextureBinding for image levels, layers, access and format. Dispatch owns activation and submission; keep work uploads, clears, barriers and readbacks explicit around it. Liquid engine pool callbacks are the specific exception to application-controlled draw ordering: they stage inputs, and LiquidPoolSubmissionHook establishes the pre-draw publication boundary.

### CPU uniform upload lifetime

`CpuUniformBuffer` tracks a content revision independently of dirty flags. Typed writes advance
it only when packed bytes change; custom writable-span or array writes must call `MarkDirty`.
`PackedUniformBuffer.SetBytes` compares and copies the complete block before marking changes.
`UniformBufferUsage` is fixed at construction: `SingleDraw` allocates per independent publication,
`SingleFrame` (the compatibility default) reuses within an open epoch, and `MultiFrame` retains
unchanged versions across frames. Usage never prohibits multiple writes within a frame.
UniformPublication separates logical publication from physical allocation. Reuse requires matching
revision, allocator identity and storage generation. It still calls the existing range-binding owner,
so another shader or slot cannot leave an unrelated block bound. Program reload does not change
the CPU bytes; normal executable validation and submission still apply.

Every `BeginFrame`, including reuse of the same frame index, opens a new allocation epoch.
`EndFrame` closes publication before inserting its fence; another `BeginFrame` is required to publish.
Repeated begin calls fence pending work before resetting a page, and disposal invalidates the allocator.
No transient snapshot is reused across these boundaries: a physically live page alone does not prove
that new draws belong to the work covered by its retirement fence. A different current ring
also requires a new upload. Changed bytes always allocate a fresh range, retaining earlier
draw snapshots. Failed allocation or binding leaves dirty work and the previous publication
record intact. CPU-buffer disposal resets publication and queues any retained version for safe retirement;
CPU bytes remain publishable for compatibility. The internal logical publication owner has terminal disposal.
Persistent pages are pooled and never orphaned while live. Each successful rebind, including scope
restoration, extends last use; only released versions behind completed frame fences can be recycled.

Use `OwnUniformBuffer` for shader-owned retained blocks. Shared blocks have one explicit external
owner; borrowing shaders do not own their storage. Engine executable reload preserves owned blocks.
A compatible slot change rebinds; an incompatible layout requires explicit repacking or replacement.
Uniform allocators belong to one renderer/context lifetime and must be disposed while that context
is current, before teardown. They do not detect or recover from context replacement.
Lifetime creates no shader variants or implicit same-name sharing. See
[uniform lifetime implementation](Rendering.UniformBufferLifetime.md) for policies and measurements.

Historical same-frame validation covered both persistent and orphaned-buffer rings, same-owner and nested
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
