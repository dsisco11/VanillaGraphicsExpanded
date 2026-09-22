# Shader variant contract proposal

**Status: Approved** — 2026-09-22.

## Objective

Declare supported shader configurations in shared C# contracts consumed by the build tool and runtime. A contract owns the supported settings, their meanings, and the shader stages that use them. GLSL remains the implementation of the shader algorithm.

The result should let a developer add a setting in one declared place, build its supported variants, and select them through a typed runtime API. No runtime manifests, custom SPIR-V reflection, or checks that the compiler honored explicit layouts are required.

## Current implementation and proposed changes

The existing `GpuBindingContract` describes bindings and interface locations. `ShaderSourceLayout` applies those declarations through VGE's GLSL AST before compilation. `ShaderStageContract` now supplies shared structural defaults, deterministic binary paths, and numeric specialization declarations. Runtime loading reads the selected binary directly.

The current configuration model is deliberately small: structural values are binary, specialization types are strings, availability uses callbacks, and the short-range AO alias is special-cased. Stage selection is spread between the contract factory, graphics programs, and test helpers. Unlisted stages currently receive an empty configuration.

This proposal develops that foundation into explicit program and stage contracts. It preserves the existing binary-only loading path, TinyAst processing, and source-assigned resource layouts. It does not restore macro discovery or runtime metadata files.

## Main objects and ownership

The following names describe the intended responsibilities; exact API spelling can be settled during implementation.

| Object | Owns | Does not own |
| --- | --- | --- |
| `GpuShaderContract` | A program identity, its declared stage combination, accepted settings, and supported configurations | GL handles or mutable per-instance settings |
| `ShaderStageContract` | A stable stage identity, source asset, shader stage, entry point, binding contract, and explicit uses of settings | Settings belonging only to another stage |
| `GpuBindingContract` | Resource slots, standalone uniform locations, and stage input/output locations | Variant enumeration or runtime selection |
| `ShaderOption<T>` | A canonical setting name, scalar type, default, allowed values or range, and aliases | GL uniform locations or implicit membership in every program |
| `ShaderSettings` | Values selected for one program instance, resolved against its contract | Modifying contract declarations |
| `ShaderVariantResolver` | Normalization, supported-configuration checks, stage projection, binary selection, and specialization arguments | Asset I/O, binary inspection, or GL calls |

Contracts are constructed once and exposed as immutable declarations. Mutable builders, if useful, are confined to contract construction. Runtime settings remain separate so changing one program instance cannot change another program's defaults.

Each shader class owns and exposes its immutable static contract. An adjacent `*.ShaderContract.cs` partial file contains the declaration, allowing the offline build tool to compile that same part of the shader class without its game/GL-dependent runtime implementation. Classes representing several programs, such as debug views and height-bake passes, expose a named static contract for each owned program. Shared option groups remain reusable declarations.

`GpuShaderContracts` discovers those shader-owned instances once and caches the validated lookup. Discovery examines only the containing assembly's declared static get-only properties and readonly fields whose exact type is `GpuShaderContract`; it does not infer settings, pairings or budgets. Compiler-generated backing fields, inherited members and collection helpers are not extra declarations. No manually maintained program list or per-shader registration marker is required. Isolated validation-only shader owners explicitly opt out with `ExcludeFromShaderCatalog` and remain available to their dedicated test scope. Each model and resolver has its own file under `Rendering/Contracts`; shader loading remains under `Rendering/Spirv`. These ownership and automatic-discovery corrections were requested after the initial registry implementation. They require no additional project or general-purpose configuration framework.

## Shader declaration generator

**Approved direction — 2026-09-22:** replace the handwritten contract partial-file requirement and reflection discovery described above with a small Roslyn incremental source generator. The existing implementation remains the migration baseline, not the final authoring model.

Shader classes declare program metadata through attributes and configurable options through attributed typed accessors. Prefer partial properties for generated runtime getters/setters so writes can invoke validation and reload scheduling; plain mutable fields cannot intercept assignments. Keep GLSL names explicit. Attributes reference shared typed definitions where appropriate, preserving one source for option defaults, domains and aliases. Exact attribute syntax and a readable representation of structural conditions must be settled against the full inventory before implementation.

Generate each shader class's immutable static contract, typed option keys, accessor implementations and deterministic automatic enumeration. Reuse the existing contract models, condition semantics and settings logic. Support graphics/compute stages, multi-program classes, shared stages and isolated validator fixtures. Emit useful compiler diagnostics for invalid declarations. No manually maintained registry entries, per-frame discovery or additional runtime manifests are required.

The offline shader compiler must consume the same generated declarations without depending on the completed mod assembly. Establish and verify an acyclic clean-build pipeline; a source generator alone does not solve that dependency. A focused generator project is permitted if required by analyzer integration, but this is not a general configuration framework. Runtime accessor generation uses a shared settings-update hook; the remaining coherent stage loading, effective-change detection and replacement ownership work remains in the runtime migration.

Implement the generator and representative fixtures first, then migrate all production and test shaders and remove the superseded partial-file/discovery machinery. The detailed contracts and completion gates are subphases 3.1 and 3.2 of the task list.

## Programs and stages

Every owned graphics or compute program has an explicit shader-owned contract, including GPU fixtures. The catalog discovers these contracts automatically. A graphics program lists its vertex and fragment stages and any optional geometry/tessellation stages. A compute program lists its compute stage. Shared fullscreen vertex stages are referenced by identity instead of being paired through filename heuristics in tests.

An empty settings list is a valid explicit declaration. An unknown program or stage is an error, rather than an implicit empty contract. Build source enumeration can report an owned shader asset missing a registry entry, but must not invent that entry or discover options from its text. Include files are not entry points.

A shared stage has one definition of its source, entry point, fixed defines, bindings, and setting uses. Programs referencing it must agree on that definition. If a source needs two incompatible fixed configurations or layouts, declare two distinct stage identities. Do not let consumer order determine the compiled result.

The build enumerates supported program configurations, projects them onto their stages, and compiles each distinct stage configuration once. This means changing a fragment-only option does not multiply identical vertex binaries.

## Settings and their stage uses

A typed option provides the name and meaning of a setting. A stage explicitly declares how it uses that option. Merely importing a GLSL include or adding a constant to `VgeShaderDefines` does not register a configurable setting.

| Stage use | Declaration | Build behavior | Runtime change |
| --- | --- | --- | --- |
| Structural | A typed option with an explicit finite domain | Emit its macro value and compile the supported configurations | Select another existing binary and relink |
| Specialization | A numeric option, stable numeric ID, scalar type, and optional structural condition | Emit its specialization constant and macro mapping | Specialize a new shader object and relink |
| Fixed define | A name and immutable typed value owned by the stage | Emit the same define for every configuration of that stage | Cannot be changed through program settings |

Start with the types actually needed: booleans and finite integer/enum domains for structural choices; 32-bit signed integer, unsigned integer, float, and boolean specialization values where supported by the source pipeline. Type-to-GLSL spelling and GL argument encoding belong in one implementation. Do not represent types with arbitrary strings or normalize all values through `double`.

Structural domains are explicit: a boolean has two values, while a debug mode might declare integers 0 through 4. Numeric settings can declare ranges; defaults must belong to those domains. Floating-point settings reject nonfinite values. No implicit numeric truncation is allowed.

A setting may be structural in one stage and specialized in another, provided the shared option has a compatible type and finite domain wherever structural use requires one. The program owns one value, projected consistently to both stages. Specialization IDs belong to a stage and are explicitly assigned, never inferred from sorted option names.

Ordinary per-frame uniforms and buffer fields remain ordinary rendering data. The variant system is for settings that justify preparing another shader program; it should not become the setter path for frequently changing draw values.

## Aliases and defaults

Alias metadata belongs to the option declaration. For example, the short-range AO option declares `VGE_LUMON_ENABLE_SHORT_RANGE_AO` as canonical and `VGE_LUMON_ENABLE_BENT_NORMAL` as a legacy alias. Both spellings resolve to the same typed option before variant selection.

Omitted values use the declared default. Supplying an alias and canonical name with equivalent values is valid; conflicting values produce a useful settings error. This intentionally replaces the current canonical-name-wins behavior. A compatibility setter receiving `null` removes the override and restores the default.

Shared settings groups are explicitly included by each program that accepts them. A global graphics configuration is projected through those declared memberships before resolving an individual program. An explicitly supplied, unknown program setting is an error; the stage resolver must not reject legitimate settings used by another stage in the same program.

A recognized option can be irrelevant in a selected structural configuration. Its value remains part of the user's settings, but it contributes neither a stage binary choice nor a specialization argument where unused. Enabling the relevant feature later restores that selected value.

## Conditional specialization constants

Specialization availability must follow explicit structural conditions. Replace the current callbacks with a small declarative condition model: equality against a structural option, plus `All`, `Any`, and `Not` composition. Conditions cannot depend on frame state, arbitrary code, or another numeric specialization value.

For example, world-probe dimensions are specialized only when world-probe sampling is enabled for that stage. Importance-sampling exploration parameters are specialized only when importance sampling is enabled and the uniform-mask and batch-slicing overrides are disabled.

Both source emission and runtime argument generation evaluate the same condition. An inactive specialization declaration and its macro override are omitted; the underlying GLSL must either exclude that use structurally or provide its fixed fallback. A contract must not declare a specialization that its selected shader implementation never uses. Tests exercise the supported branches so incorrect declarations fail during shader loading, without adding a binary parser or runtime manifest.

Availability is separate from supported configurations: a supported configuration may legitimately omit some numeric inputs. There is no attempt to infer their availability from optimizer output.

## Supported configurations and binary identity

The default is the Cartesian product of explicitly declared structural domains. A program may instead declare a finite list of complete supported assignments when not every combination is useful or valid. Validate that list for duplicate rows, missing values, unsupported values, and inclusion of the default configuration. Do not use arbitrary callbacks to silently filter it.

Each program declares a maximum variant count to catch accidental expansion before compilation. The build reports both program combinations and deduplicated stage binaries so a shared stage does not appear to multiply unnecessarily.

Canonical stage keys contain only the structural options used by that stage, sorted by canonical name and formatted by type using invariant rules. Aliases, unrelated stage settings, and numeric specialization values do not affect the key.

Retain the current direct path for a stage's default binary and the deterministic keyed path for other variants. Stage identity determines the asset path; contract construction rejects two distinct stage identities that would publish to the same path. Fixed defines, source changes, and binding changes invalidate the build through the existing build receipt. No runtime source hash or manifest compatibility check is added.

## Example: a trace program

The trace program references its declared vertex stage and a trace fragment stage. Its fragment-stage declaration might include:

| Setting | Use | Default | Applicability |
| --- | --- | --- | --- |
| Near-field enabled | Structural boolean | Disabled | Trace fragment stage |
| World-probe enabled | Structural boolean | Disabled | Trace fragment stage |
| Ray steps | Integer specialization with a declared range | 10 | Trace fragment stage |
| World-probe resolution | Integer specialization | Existing supported default | Only when world-probe sampling is enabled |

Other current trace settings remain declared as well; this table illustrates the ownership rather than replacing the complete contract.

Selecting near-field enabled, world-probe disabled, and 20 ray steps chooses the corresponding fragment binary, supplies the ray-step specialization, and omits world-probe specialization arguments. The vertex stage keeps its existing binary if it uses none of those settings. Changing ray steps to 24 reuses the same fragment binary and prepares a new specialized program. Turning world probes on selects the corresponding structural variant and includes the declared world-probe constants.

## Build flow

1. Construct the shared registry and validate our declarations: identities, stage combinations, option types/defaults/domains, aliases, specialization IDs, structural conditions, fixed defines, and variant budgets.
2. Enumerate supported program assignments and project them onto stages. Deduplicate identical stage configurations.
3. Expand imports through the existing `ShaderSyntaxTreePreprocessor`. Apply fixed and structural defines, active specialization declarations, and resource layouts using VGE's extended GLSL schema and TinyAst editor. Insert configuration before its uses and guarded source defaults. The GLSL compiler evaluates conditional directives.
4. Compile each stage configuration directly to its deterministic `.spv` asset. Publish the compiler bytes unchanged. The existing build receipt owns incremental invalidation and output tracking; it is not a runtime asset.
5. Package those binaries through the existing build/package process. Registry-driven tests verify that every supported stage configuration has an asset and that declared graphics combinations link.

Imported GLSL helpers, include guards, and internal macros remain normal source constructs. Contract validation does not require registering every preprocessor identifier. Only application-configurable settings and explicitly owned fixed defines belong to the contract system.

The remaining derived-global-constant rewriting should separately move from regex handling to the existing syntax model. It must preserve source behavior and must not become a configuration-discovery mechanism.

## Runtime flow and reload ownership

`GpuProgram` and the compute pipeline resolve an explicit program contract. A typed setter accepts an option key and value; the current string-based `SetDefine` can remain as a compatibility adapter that resolves names and aliases through the contract.

Normalize and validate a settings update before preparing GPU objects. Resolve all stages from the same settings snapshot, including each binary path and its typed specialization arguments. The resulting load plan is transient in-memory data, not a generated or serialized manifest.

Coalesce multiple settings changes through the existing reload scheduling. If the effective stage selections and specialization arguments are unchanged, no reload is necessary. A structural or active specialization change prepares replacement stages and links a replacement program. The current program remains usable until the replacement succeeds; on failure, release the candidate resources and retain the current program. Keep the distinction between requested settings and the settings of the currently installed program visible to the owner.

Load binaries through the read-only span asset-reader contract. Numeric resource lookup comes from `GpuBindingContract`, with OpenGL queries identifying active resources so callers do not set optimized-away uniforms. Keep normal OpenGL load/link status handling. Do not compare compiled bindings against source declarations or inspect SPIR-V instructions.

## Verification and diagnostics

Pure contract tests cover typed normalization, alias equivalence/conflicts, defaults, domains, conditional constants, unsupported assignments, deterministic paths, stage sharing, and variant-count limits. Source-emission tests verify that our AST transformations place the declared values correctly, without inspecting compiled binaries.

GPU tests load every finite stage variant and link the declared program combinations. Numeric specialization ranges are tested with selected default, boundary, and representative values rather than claiming exhaustive coverage of unbounded inputs. Retain rendering, uniform/buffer behavior, and reload/disposal regressions.

Failures identify the program, stage, option, and requested value where applicable. Build output reports configuration counts and asset failures. No new per-frame validation, runtime source hashing, or compiler-obedience checks are introduced.

## Implementation order

1. Define immutable program/stage declarations, typed options and stage uses, and the resolver. Preserve existing default paths and supported behavior while replacing string types and special-case alias logic.
2. Inventory existing setting writers and stage entry points. Register production programs and fixtures explicitly, including shared stage combinations and known aliases. Classify settings that no longer affect any shader instead of silently treating them as configurable.
3. Migrate the existing contract-driven builder to the richer model: finite domains, explicit supported assignments, stage deduplication, and declarative specialization conditions. Retain the shared AST pipeline and build receipt. Macro discovery and runtime manifests are already removed and must stay removed.
4. Route graphics and compute settings through program-level resolution and one immutable load snapshot. Retain the compatibility setter and existing successful-reload ownership rules.
5. Derive inventory/link tests from the registry, verify representative rendering and numeric specializations, and update developer guidance for adding programs and options.

## Acceptance criteria

Adding an application-configurable shader option requires an explicit typed contract declaration and explicit stage use. Every owned entry point and supported program combination is registered. Build and runtime resolve the same configurations, shared stages are compiled once per distinct stage configuration, and unsupported settings fail clearly.

TinyAst and the existing shader preprocessor remain the source-processing path. Published assets are SPIR-V binaries, with no runtime manifests or custom binary reflection. Runtime loading remains SPIR-V-only, and changing settings preserves the working program until its replacement successfully links.
