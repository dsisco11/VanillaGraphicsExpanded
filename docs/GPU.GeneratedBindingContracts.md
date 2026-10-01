# Generated GPU binding contracts

The shader declaration generator resolves `ShaderBinding` attributes on strongly
typed interface properties and class partial properties into each generated
`ShaderStageContract.Bindings`.
The attribute owns the index. Runtime shader owners receive typed resource setters
implemented through their existing linked program layout. Shared and contract-only
owners receive typed descriptors. Names, resource kinds, stage applicability and
optional program subsets remain explicit. Offline compilation emits the same immutable
contract without runtime resource types. Runtime source scanning and attribute
reflection do not participate in binding ownership.

Image setters accept either `GpuTexture` for the default image view or `GpuTextureBinding`
for explicit access, mip, layer and format choices. The latter replaces the former
`GpuImageBinding` name and describes binding parameters without creating a separate
OpenGL texture-view object. The direct texture form uses read-only access, mip zero,
layer zero, non-layered binding and the texture's internal format. Both forms use
the existing image-binding call.

Authoring examples and shared-layout rules are in [GpuProgram.md](GpuProgram.md#generated-gpu-binding-contracts).
The schema lives in `Rendering/Contracts/ShaderBindingAttribute.cs` and
`ShaderBindingSetAttribute.cs`; semantic validation and emission live in
`ShaderContractGenerator/BindingReader.cs`, `InterfaceBindingReader.cs` and `ProgramReader.cs`.

Interfaces declare ordinary public resource setters or typed descriptor getters. A
partial shader class implementing the interface receives missing implementations
through its existing GpuProgram or compute-pipeline layout. Existing authored setters,
explicit implementations and default interface bodies retain their behavior. Defining
partial properties can be completed from interface metadata without repeating attributes.
Inherited class implementations are reused rather than generated again on derived owners.

Interface diamonds retain one declaration. Explicit derived/composite redeclarations
resolve inherited metadata before conflict checks, independent of traversal order.
Property name/type and GLSL name/kind identify the same binding across overrides;
unresolved sibling ownership, unsupported types/accessors and conflicting layouts
produce compile-time diagnostics. Metadata-only `ShaderBindingSet` imports accept
interfaces on static contract owners and can retain default, stage and program filters.
Offline shells contain immutable contracts without runtime interface bodies or resource APIs.

Interface support is validated by 141 generator tests and 154 focused contract/GPU
tests, including interface-generated sampler/image dispatch, direct texture image
binding, all 166 original stage layout fingerprints, capture and atmosphere checks.
The Debug solution build passed with zero errors and verified 398 SPIR-V variants.
Evidence is in `generator-interfaces.log`, `build-interfaces.log` and
`focused-interfaces.log`, with corresponding TRX files under
`artifacts/binding-contract-validation/`. The GPU fixtures now use interfaces;
class-based production declarations remain supported.

## Ownership and migration

The former `GpuShaderContracts.DeclareBindings` shader-name switch and its resource
helper files have been removed. Shader owners declare slots and binding-set attributes
in their main shader class files. `GpuShaderContracts` retains catalog access and two existing
identity aliases, which select generated program contracts rather than declaring slots.
The debug runtime layout now consumes its shader owner's generated fragment contract.

Shared geometry and surface-cache inputs have one declaring property set each. Fixtures
reuse production shader owners, and PIS/display resolve programs import their shared
vertex owners with explicit stage filters. Multiple debug views and height-bake passes
share their owner properties without repeating declarations. Atmosphere's scattering, sky
and lighting contracts share the common parameter slot and select their output-buffer
subsets explicitly.

`ShaderInterfaceLocations` preserves the existing stable location table and intentional
I/O aliases. `ShaderIncludeBindings` supplies optional include-block defaults. Explicit
declarations override defaults by kind/name; conflicting independently declared defaults
are rejected. Resource namespaces remain independent, and the original required/optional
policy survives even when a compiled variant optimizes a resource away.

The task's `TerrainNeutralNormalShaderProgram` name is absent from the current checkout,
including its original source snapshot. Current terrain neutral resources are immediate
texture uploads in `PBR/Materials/TerrainReliefBindings.cs`; there is no shader declaration
under that name to migrate. Existing material-bake and atmosphere shader owners were
migrated.

## Validation evidence

An isolated copy of the original committed generator and declarations was compiled
before refreshing the regression fixtures. Its registry contains 134 production
programs, including 69 debug views, 161 production stages, 405 program assignments and
398 distinct shader binaries. Across production, build-validation and generator-fixture
scopes, it supplies 166 distinct stage binding layouts.

`ShaderBindingMigrationBaseline.txt` contains fingerprints from that independently
compiled original registry. Each fingerprint includes all seven binding/interface maps,
indices and required-resource flags. `ShaderBindingMigrationTests` compares the new
generated layouts against every original fingerprint. The semantic registry baseline
was also refreshed from the compiled original, correcting historical test expectations
that had already become stale before this binding migration.

Focused generator coverage includes normal/offline parity, immutable publication,
independent resource namespaces, inheritance, diamond imports, program subsets,
include defaults, stage filters, enabled/disabled structural variants and invalid
declarations. GPU coverage inspects compiled sampler/UBO slots, the shared PIS vertex
pairing, enabled/disabled variants and all three atmosphere SSBO layouts before runtime
rebinding. The existing atmosphere GPU suites also exercise actual computation.

All builds and test runs use subagents. Logs and TRX results are retained under
`artifacts/binding-contract-validation/`; the isolated original-source probe is under
`artifacts/binding-pre-migration/`. These are build and headless GPU checks; live game
rendering was not launched for this task.

Earlier contract migration results on 2026-10-01 (before the typed-property correction):

| Check | Result |
| --- | --- |
| Generator suite | 112 passed |
| Focused contract and GPU suite | 142 passed, including 39 GPU cases; no skips |
| Original stage binding fingerprints | All 166 matched |
| Debug solution build | Passed, zero errors; existing package/analyzer warnings remain |
| Offline SPIR-V build | 161 stages, 398 variants compiled |

The final logs are `solution-corrected.log`, `generator-corrected.log` and
`focused-contract-gpu-corrected.log`, with matching TRX files for the test suites.
`original-compiled-probe.log` records the independent original-source verification.

All 55 shader binding companion files were subsequently merged into their main shader
class files. The Debug solution build and the same 142 focused tests passed after the
move, including all 166 original binding fingerprints and the semantic declaration
baseline. This source-layout check is recorded in `build-inline-bindings.log` and
`focused-contract-gpu-inline-bindings.log`, with its matching TRX result.

The typed-property correction replaces all attributed integer fields with partial
properties and moves every index into its marker attribute. Runtime owners use typed
resource setters; shared layouts and static contract owners use typed descriptors.
The voxel and mesh-card capture paths now apply their generated image, UBO and SSBO
setters, retaining image access, format and layering. Existing caller-facing methods
remain compatible.

Final typed-property validation on 2026-10-01 passed 118 generator tests and 154 focused
contract/GPU tests. Generator execution covers all four resource setter types on both
GpuProgram and compute-pipeline owners; offline output excludes runtime accessors.
A linked GPU dispatch verifies generated sampler/image assignments and inactive-resource
handling. Capture tests exercise the migrated buffer/image assignments. All 166 original
stage binding fingerprints still match. The Debug solution build passed with zero
errors and verified 398 SPIR-V variants; package/analyzer warnings remain. The generator
suite was rerun after the final generator-only validation changes.

Evidence is in `build-typed-properties.log`, `generator-typed-properties.log` and
`focused-typed-properties.log` under `artifacts/binding-contract-validation/`, with
matching TRX test results. Live game rendering was not run.

The `GpuTextureBinding` rename and direct `GpuTexture` image-setter support passed the 118
generator tests, the Debug solution build (398 SPIR-V variants), and 11 focused
GPU/baseline/capture tests. The GPU test checks inferred format and default access,
mip and layer state before applying an explicit write-only view and verifying dispatch
readback. Evidence is in `{generator,build,focused}-image-view.log` and the corresponding
test TRX files in the same validation directory.

Handwritten declarations use ordinary property names and short imported resource
types. Existing caller-facing setter names are preserved. This naming cleanup passed
the Debug solution build, all 118 generator tests and nine focused catalog, binding
fingerprint and generated-resource GPU tests. Evidence is retained in the
`*binding-names*` logs and TRX files in the validation directory.
