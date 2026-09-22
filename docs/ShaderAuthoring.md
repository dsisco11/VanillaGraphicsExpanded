# Declaring owned shaders

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

Generated accessors validate requested settings and schedule only effective binary or specialization changes. The queued callback compares the latest request with the installed configuration, so writes that return to the installed values do no loading. Inactive numeric selections remain stored and become effective when their condition becomes true. Ordinary uniforms, buffer fields and per-frame values do not belong in the option contract. Batch helpers can use the same typed `SetShaderOption` hook and its changed result when rendering must wait after an update. Resource-driven helpers may explicitly replace topology with sentinel values; conditional omission itself never discards a selected value.

Compatible reused stages must agree on identity, source, layout, fixed defines and setting uses; they share one immutable stage and binary per structural assignment. A different vertex pairing can reuse the same fragment declaration by repeating its exact uses and accepted groups. The generator rejects conflicting shared declarations. Use separate stage identities and binary assets for genuinely incompatible configurations.

`ShaderProgram.Scope` defaults to `production`. Dedicated fixtures may use an explicit separate scope, such as `build-validation`; they are available to that scope without entering the packaged production inventory. Source-only packaged fixtures still declare their owner in the mod source tree so both compilation paths see them.

The build tool reads ordinary mod sources as semantic inputs and emits declaration-only owners. It never depends on the completed mod assembly. Keep declarations independent of framework-specific C# conditional symbols because the tool and mod target different frameworks. Run generator tests, the normal shader-enabled build, focused contract/source tests, and relevant GPU tests when changing declarations. Inventory tests verify source coverage, shared-stage consistency, supported assignments, and expected binary paths.

The compiler enumerates supported program assignments and builds each distinct projected stage once. Its summary reports program combinations separately from stage binaries. Source enumeration checks that every owned entry point is declared and every registered source exists; identities may differ from source paths, and incompatible source reuse must have distinct binary assets. Fixed and structural values are emitted with their declared types (Boolean preprocessing macros use `0`/`1`); active specialization declarations use stable IDs and declaration defaults. Imported guarded defaults remain in GLSL after the injected configuration, with conditional evaluation left to the compiler.

Global initialized `const` expressions containing parentheses are relaxed by a separate syntax-tree transformation to permit executable initialization. Layout-qualified specialization declarations, function-local constants, comments and nested expression text are preserved. Resource and interface layout editing remains the responsibility of `ShaderSourceLayout`.
Each graphics load captures a `ShaderLoadPlan` before asset reads. Its immutable `ShaderSettings` and projected stage selections supply the binary paths, explicit stage kinds/entry points and exact specialization bits. Successful linking and interface preparation commit the installed settings; failures retain the old executable, interface and installed snapshot. The layout object and its UBO state remain stable across reloads. Explicit `CompileAndLink()` and engine reloads force preparation, while queued setting updates skip equivalent inputs and cannot resurrect a disposed program.

`SetDefine(name, value)` remains a compatibility adapter for declared names and aliases; unknown names and invalid values fail before GPU preparation. Null and `RemoveDefine` restore the canonical default. Equivalent alias writes are no-ops; conflicting values within a supplied settings dictionary are rejected. Inactive values remain requested without reloading until enabled. The setter's Boolean result describes an effective input change, not merely a retained value change.

Compute loading accepts an explicit `ShaderSettings` snapshot. The asset compatibility overload resolves its argument as a program identity; direct-file loading requires settings alongside the path and never infers a stage from the filename. Compute creation prepares a new pipeline and leaves an existing caller-owned pipeline intact on failure. The span loader consumes each selected binary synchronously and does not read source or runtime metadata to reconstruct configuration.