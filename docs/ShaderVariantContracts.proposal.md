# Shader variant contract proposal

## Objective

Declare every supported shader configuration in the shared shader contracts. The build tool must not infer configuration options, default values, types, or supported combinations from preprocessor text. Build and runtime must use the same declarations.

## Current implementation

`GpuShaderContracts` supplies resource bindings and explicit interface locations through `GpuBindingContract`. `ShaderSourceLayout` applies those values through the shared GLSL AST before compilation. `ShaderStageContract` now declares structural choices and numeric specialization inputs shared by the build tool and runtime; runtime manifests and binary reflection have been removed.

The source pipeline uses TinyAst and `ShaderSyntaxTreePreprocessor` for imports. GLSL conditional evaluation remains the compiler's responsibility. Current stage contracts cover binary structural choices and conditional numeric constants; the broader program ownership, typed-domain validation, alias conflict handling, and stage-combination declarations below remain proposed work.
## Contract ownership

Add a `GpuShaderContract` that composes resource bindings, declared stages, and configuration declarations. Keep `GpuBindingContract` responsible for resource slots and explicit interface locations. Put shared configuration groups in small reusable declaration files, referenced explicitly by each program that uses them. Includes do not automatically add options.

Every owned program, including test fixtures, must have an explicit contract. An empty configuration is valid and produces one variant. Unknown program names must fail the build rather than silently receive an empty contract. Existing debug and height-bake family aliases may share declarations, but each entry point must be listed explicitly.

Declare graphics stage combinations in the program contract, including the existing shared fullscreen vertex shaders. A reusable stage is compiled once for each distinct stage configuration requested by its consumers. Linking validates the declared combination and cross-stage interfaces.

## Configuration declarations

Each configurable input has a stable name, type, default, stage applicability, and one of these roles:

- **Structural option:** an explicit finite value set, such as disabled/enabled or debug modes 0 through 4. Structural options produce preprocessor defines before compilation. Options controlling declarations, array sizes, interfaces, or conditional compilation belong here.
- **Specialization constant:** an explicitly assigned stable numeric ID, scalar type, default, and supported range or finite set. Values specialize a compiled binary. These must not control preprocessor conditions or change the resource/interface layout.
- **Fixed define:** an explicit build constant that is not a runtime selection dimension. `VGE_SPIRV_BUILD` belongs to the compiler environment and is reserved rather than exposed as a user option.

Declare legacy aliases explicitly in configuration metadata. Normalize aliases before selection, reject conflicting alias/canonical values, and replace the compatibility alias handling in `ShaderStageContract.Value`. Shared settings may use reusable declarations, but defaults and IDs must remain unambiguous within each stage.

For example, a trace program would explicitly declare the near-field switch as a boolean structural option for its fragment stage. Any numeric setting retained as a specialization constant would separately declare its integer/float type and ID. A macro name alone is not enough to declare either role.

## Supported variants

By default enumerate the Cartesian product of the explicitly declared structural domains. Allow a contract to supply an explicit list of supported configurations when only a subset is valid; require complete assignments and reject duplicates, unknown values, and a missing default configuration. Avoid arbitrary filtering callbacks so the allowed set can be serialized, inspected, and shared with runtime.

Canonical keys use declaration names in ordinal order and invariant typed values. The same canonicalization code serves the build tool and runtime. No source scanning creates options or variants. Specialization values do not multiply offline binaries.

The compiler receives each selected structural configuration through AST-based define injection, followed by the shared import preprocessor and SPIR-V emission. Generated specialization declarations use contract IDs and types. Existing derived-global-constant rewriting should receive a separate syntax-aware implementation and focused coverage; it must not become another source of inferred configuration.

## Validation and runtime selection

Validate contracts before invoking the shader compiler: duplicate names/IDs, unsupported types, nonfinite numeric defaults, invalid ranges, reserved names, conflicting aliases, unknown stages, and invalid supported configurations are errors. Enforce a declared variant-count budget per program to prevent accidental combinatorial expansion.

A runtime program resolves typed settings against its program contract, then projects the result onto each declared stage. Unknown program-level options and unsupported combinations are errors; stage loading must not reject legitimate options owned by another stage. Defaults apply only to omitted declared settings. Report program, stage, option, and requested value on failure.

Build and runtime use the same compiled contract declarations and deterministic binary paths. Include contract content in build invalidation. Do not generate or load runtime manifests, hash source assets during loading, or inspect compiled binaries to check whether the compiler honored explicit layouts. Keep strict SPIR-V-only loading and ordinary driver load/link error handling.

Specialization declarations specify the structural branches where each constant is used, so the loader supplies only the constants belonging to the selected configuration. GPU tests cover supported specialization combinations and linked graphics interfaces. Runtime numeric queries identify active resources to avoid setting uniforms removed by optimization; they do not revalidate compiler-assigned bindings.
## Implementation order

1. Introduce the program/configuration models and deterministic validation, with unit tests for normalization, aliases, domains, combinations, IDs, and defaults.
2. Inventory current runtime settings and shader entry points. Declare all production and fixture contracts, stage combinations, and explicit shared option groups. Use source declarations and existing runtime consumers as the authority.
3. Replace `Describe` and `Variants` discovery with contract-driven enumeration and AST-based emission. Remove reflection over `VgeShaderDefines`, regex condition/default discovery, guessed boolean domains, and special-case option names. Preserve all-stage compilation and incremental invalidation.
4. Switch runtime selection to the same contracts and typed validation. Reject unsupported values before allocating shader handles.
5. Verify every declared variant compiles, graphics combinations link, bindings agree, lifecycle reload tests pass, and packaged binaries cover their declared configurations. Add negative tests for invalid contracts and unsupported runtime selections. Compare default and representative nondefault rendering fixtures to preserve behavior.

## Acceptance criteria

Adding a configurable shader option requires an explicit contract declaration. No variant is discovered from GLSL. Both build and runtime consume the same declarations; all supported combinations are built and exercised by inventory/link tests. TinyAst and the shared shader preprocessor remain the source-processing path. No runtime GLSL fallback is introduced.
