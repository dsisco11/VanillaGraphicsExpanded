# Declaring owned shaders

This guide is for maintainers adding or changing VGE-owned graphics and compute shaders.
Declare configuration and bindings on the owning C# type, author GLSL entry points, and use
those declarations for both the shader build and runtime preparation. Engine-owned GLSL
patches use their existing integration path.

```mermaid
flowchart LR
    Owner["Owner declarations"] --> Contract["Shared shader contract"]
    Contract --> Build["Shader build"]
    Source["GLSL entry points"] --> Build
    Build --> Assets["SPIR-V and digest manifest"]
    Contract --> Prepare["Prepare requested settings"]
    Assets --> Prepare
    Prepare --> Submit["Capture bindings and submit"]
```

## Declare the contract

Put the immutable contract on the shader's owning partial class. The generator includes it
in the shared build/runtime catalog; no registration list or special declaration filename
is needed.

```csharp
/// <summary>Owns the example's declared graphics configuration.</summary>
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

    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;
}
```

Add the GLSL entry points under `assets/vanillagraphicsexpanded/shaders`. Declare resource
slots and interface locations through `GpuBindingContract`. Use `ShaderStage.Layout` when
the layout identity differs from the source stem. The build does not infer configurable
settings from GLSL macros.

Declare every stage, including stages without configurable options. A compute program has
one `ShaderStageKind.Compute` stage. For a multi-program owner, repeat `ShaderProgram` and
`ShaderStage` with distinct contract member names. The generator emits each named contract
and a read-only `Contracts` collection. The runtime family selects its `ProgramContract` by
active program identity; callers update only options accepted by that program.

Reused stages must agree on identity, source, layout, fixed defines and setting uses.
Repeat the exact uses and accepted groups when sharing a fragment with another vertex
pairing. Conflicting shared declarations are rejected; incompatible stages need distinct
identities and binary assets.

`ShaderProgram.Scope` defaults to `production`. Dedicated fixtures can declare another
scope, such as `build-validation`, without entering the packaged production inventory.
Source-only packaged fixtures still declare their owner in the mod source tree.

The build tool reads mod declarations semantically and emits declaration-only owners; it
does not depend on the completed mod assembly. Keep declarations independent of
framework-specific C# conditional symbols because the tool and mod target different frameworks.

### Options and supported assignments

The variant budget limits supported structural assignments. Boolean options have their
natural two-value domain; structural integer and enum options require an explicit `Domain`.
Defaults and domain/range constants must exactly match the property type. Numeric
specializations do not multiply binaries; their IDs must be explicit, stable and unique
within the stage. Register options that affect declared stages, and keep ordinary per-frame
values in uniform blocks.

Use repeated `ShaderAssignment` attributes when only specific complete structural
assignments are supported. Each row supplies every structural option with canonical names
and typed values, including a default row. Otherwise the catalog enumerates the Cartesian
product. Sharing a stage does not multiply its binary count. Use `ShaderFixedDefine` for
fixed defines; include guards and helper macros need no option declaration.

Shared options use attributed static get-only `ShaderOption<T>` partial properties. An
instance accessor can use `[ShaderOptionReference(typeof(SharedOptions), nameof(SharedOptions.Enabled))]`
with a matching type. Shared groups use `ShaderGroup` on their owner and `ShaderAcceptGroup`
on consumers. Group names and generated contract member names are string literals; option
references can use `nameof` on source-declared properties. See
[lighting options](../VanillaGraphicsExpanded/LumOn/Shaders/LumOnShaderOptions.cs) and
[lighting groups](../VanillaGraphicsExpanded/LumOn/Shaders/LumOnShaderGroups.cs).

`When` refers to local attributed properties, including shared-option references. It accepts
Boolean properties/literals, `!`, `&&`, `||`, parentheses and `Property == constant` with C#
precedence. Constants are exact Boolean/int/uint literals or members of the property's enum
type; use a `u` suffix for small uint literals. Enum names may be simple, namespace-qualified
or `global::`-qualified. Calls, arbitrary member access, arithmetic, casts, option-to-option
comparisons and `!=` are unsupported; use `!(Mode == Quality.High)` for inequality.
Null/omission is unconditional; empty text is invalid. Every dependency must be structural
in the consuming stage. Names inside strings do not receive automatic rename support;
stale names produce a build error at the attribute.

## Choose runtime inputs

Owned numeric inputs use explicit std140 uniform blocks. Typed setters write
`CpuUniformBuffer` storage, supplied through a `ShaderBinding` uniform-block getter.
Stages sharing numeric data use one block declaration and publication.
The generator rejects numeric `UniformLocation` properties, and owned executables reject
active standalone numeric uniforms during interface preparation.

Sampled resources use `GpuTextureRef` through typed binding interfaces. Declare dimensions
and sampler policy, including optional non-2D inputs. Images use `GpuTextureBinding` for
mip/layer/access/format; storage buffers use `GpuStorageBufferBinding` for ranges.
Sampler/image contracts describe fixed units and activity; their native locations are
preparation metadata. Mark an input optional only when the shader has a valid path without
it. Optional absence clears its slot; required active resources must be usable at execution.

Engine-owned GLSL compatibility paths retain their native numeric setters and activation
semantics. See [binding compatibility boundaries](Rendering.ShaderBindingState.md#retained-compatibility-boundaries).

### Shared frame and lights

Camera consumers import `includes/vge_frame_ubo.glsl` and bind `VgeFrameUBO` at
`GpuBindingRegistry.Ubo.Frame` (12). Borrow the published frame for matrices, camera/fog data,
clip planes, frame duration, unsigned `frameIndex` and render-origin conversion. The
[source schema](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_frame_ubo.glsl)
and [typed buffer](../VanillaGraphicsExpanded/Rendering/VgeFrameUniformBuffer.cs) define its layout.
Temporal consumers reject prior camera history when bit zero of `frameFlags` marks a camera cut.

`screenSize` describes the source view. Reduced-target dimensions, effect-specific clocks,
pool/model transforms and light-specific shadow transforms stay in their owning contracts.
An engine mesh-pool `modelViewMatrix` combines object and view transforms; adapt it explicitly
instead of treating it as the shared view matrix. Shadow passes must not require an
unpublished world-camera snapshot. Render-dependent random sampling uses shared `frameIndex`.

Dynamic point-light consumers import `includes/vge_lights_ubo.glsl` and bind `VgeLightsUBO`
at `GpuBindingRegistry.Ubo.Lights` (16). Borrow the common snapshot instead of publishing
private light arrays/counts or applying another coordinate/intensity conversion. The
[light schema](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_lights_ubo.glsl)
and [typed buffer](../VanillaGraphicsExpanded/Rendering/VgeLightsUniformBuffer.cs) define the contract.
Solar, block-light, probe and surface-cache inputs remain separate sources.

Alternate views supply their own frame and light snapshots in the receiver's actual view
space without mutating the world publication. Shared blocks have explicit external owners;
borrowing shaders do not own their storage. Use `OwnUniformBuffer` for shader-owned blocks.

## Prepare and submit

Generated accessors retain validated requested settings without scheduling compilation.
`ConfigureOptions` groups edits atomically. The editor copies its immutable requested
snapshot on the first normalized change; a batch that restores the original values keeps
the original snapshot and load plan. Callbacks and write validation still run, and a failed
edit aborts publication.

`EnsureReady`, explicit preload or activation compares requested and installed inputs.
Inactive selections stay stored and become effective when their conditions become true.
Equivalent writes require no loading, but do not clear preparation failures or reload
invalidation. Consumers still use their readiness boundary.

Graphics loading captures a `ShaderLoadPlan` before reading assets. Its immutable settings
supply binary paths, stage kinds, entry points and specialization bits. A successful candidate
is installed only while its requested plan remains current. Failed replacement retains the
old executable/interface/settings; superseded candidates are discarded. Explicit
`CompileAndLink` and engine reload force preparation. Reload preserves the layout and owned
CPU uniform state; an explicitly retired owner cannot be reactivated.

`SetDefine` adapts declared names and aliases. Unknown names and invalid values fail before
GPU preparation. Null or `RemoveDefine` restores the default. Equivalent aliases are no-ops;
conflicting alias/canonical values in one dictionary are rejected. The Boolean return value
describes an effective input change, not merely a stored inactive-value change. Bent-normal
spelling is an alias of short-range AO.

Assign desired runtime inputs before `CaptureBindings`. Prepare the executable, create/select
its exact RHI pipeline, bind the captured table and draw/dispatch through `RHICommandContext`.
Capture freezes mutable values; execution validates the complete active input set before
publication. References do not transfer ownership or extend borrow lifetime. Uploads, clears,
barriers and readbacks remain explicit. See
[prepared resource submission](Rendering.ShaderBindingState.md#prepared-resource-submission).

Compute owners use `GpuComputeProgram` and `ComputeShaderAsset`. Loading accepts explicit
`ShaderSettings`; direct-file loading requires settings alongside the path and never infers
stage kind from a filename. The asset compatibility overload resolves its argument as a
program identity. Creation prepares a new pipeline and preserves an existing caller-owned
pipeline on failure. The span loader consumes selected binaries synchronously without
reading source or runtime metadata to reconstruct configuration.

### Uniform publication lifetime

Typed writes advance content revision only when packed bytes change. Custom writable-span
or array writes must call `MarkDirty`. `UniformBufferUsage` is fixed at construction and
does not limit how often CPU contents may change:

| Policy | Publication behavior |
| --- | --- |
| `SingleDraw` | A fresh publication for an independent activation or dispatch. |
| `SingleFrame` | Reuse unchanged data within the current open allocation epoch; compatibility default. |
| `MultiFrame` | Retain unchanged data across frames until replacement or owner retirement. |

Capture retains logical bytes/revisions; physical allocation and upload happen at execution.
Every `BeginFrame`, including the same frame index, opens a new epoch. `EndFrame` closes
publication until another begin. Changed bytes get a new range, preserving submitted data.
Failed allocation/binding preserves pending CPU work. Compatible slot changes rebind;
incompatible layouts require repacking or replacement.

Uniform allocators belong to one renderer/context lifetime. Dispose them while that context
is current, before teardown. See [uniform publication lifetime](Rendering.UniformBufferLifetime.md)
for allocator, fence, retirement and compatibility details.

## GLSL and compiler policy

Graphics sources use `#version 330 core`; stages requiring a higher version, such as 430
compute shaders, declare it explicitly. The build preserves authored versions. It enables
`GL_ARB_shading_language_420pack`, `GL_ARB_separate_shader_objects`,
`GL_ARB_explicit_uniform_location` and `GL_EXT_control_flow_attributes`. Other newer features
need a source-declared extension or higher version. These extensions and the SPIR-V runtime
path are required; the source baseline does not imply unextended OpenGL 3.3 compatibility.
The compiler target is `opengl4.5`, separately from the GLSL source version.

Both Debug and Release use performance optimization (`-O`). Debug requests debug information
(`-g`); Release omits it. Compiler-policy changes invalidate cached outputs through the build
receipt. Source preprocessing uses TinyAst and `ShaderSyntaxTreePreprocessor`, with explicit
typed fixed/structural values and stable specialization IDs. Imported guarded defaults stay
in GLSL for compiler evaluation. `ShaderSourceLayout` owns resource/interface layout editing.

## Build and validate changes

When changing declarations, run generator tests, the normal shader-enabled build, focused
contract/source tests and relevant GPU tests. Build/source inventory checks cover owned
entry points, shared declarations, supported assignments and expected binary paths. Focused
loading/lifecycle tests cover generic mechanics; functional tests check actual shader behavior.

Run the matching shader-enabled build before GPU tests so copied assets match declarations.
`ShaderBuildTool/Tests/ValidateBuildContract.ps1 -Configuration Debug` (or `Release`) checks
compilation, selective reuse, repair, publication and runtime manifest compatibility with
isolated fixtures. The normal mod build verifies MSBuild copying; the `Package` task in
`CakeBuild` performs full packaging.

Deploy `spirv-digests.json` with its matching binaries: it contains versioned compiler interface
declarations for exact binary contents. Keep private caches, receipts and recovery directories
out of runtime assets. Linked activity and driver resource addresses remain runtime facts;
do not use binary rewriting, extra metadata manifests or filename-pairing rules to repair
contract or packaging errors.

### Incremental builds

Normal builds reuse validated source, variant, compiler and interface records. Shader edits
process that root and its variants; imported-file edits process recorded dependents.
Declaration edits select variants whose effective contracts changed. The catalog and outputs
are still checked. Avoid routine clean builds, which discard reusable state.

Read `roots expanded`, `variants emitted`, `compilerInvocations` and `interfaces extracted`
to see actual work. An unchanged build reports zero for all four; selected-variant progress
is not a count of compiler invocations. Reverting an edit can reuse historical results.

Use `-p:SpirvVerifyContents=true` for edits preserving file size and timestamps; normal
metadata-based hashing can miss them. Debug and Release have separate caches, and
`SpirvArtifactsDir` overrides their location. See [incremental shader builds](ShaderBuild.Incremental.md)
for repair, publication recovery, command options and measurements.

### Diagnose selection and loading failures

| Symptom | What to inspect |
| --- | --- |
| Generator diagnostic | Property/constant types, domains, condition names and structural stage uses. |
| Unknown option or conflicting aliases | Accepted keys/groups and equal values for alias/canonical entries. |
| Unsupported assignment or budget failure | Complete structural rows, defaults and intended domain size. |
| Missing asset | Selected `BinaryPath`, build output and packaged path; there is no runtime GLSL fallback. |
| Specialization failure | Stage identity, entry point, active values and driver log; inactive values are omitted. |
| Link failure | Declared stage pairing, interface layouts and driver log. |
| Requested change not visible | Requested/installed settings, readiness boundary and last error; inactive values wait for their condition. |
