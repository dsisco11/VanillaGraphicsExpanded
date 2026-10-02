# Prepared shader bindings and generated shader state

Status: proposed. The existing implementation contains partial state generation; this document describes the intended design, not completed behavior.

Tracked by [PBR baseline shading](PBR.BaselineShading.todo), under “Refactor generated shader binding state and eliminate redundant sampler uniform locations.” The declaration inventory and migration evidence belong in the [binding-state audit](Rendering.ShaderBindingState.md).

## Problem and intended behavior

A fixed sampler binding already declares the texture unit that the shader reads. Assigning a texture to that unit should not depend on a second, manually authored sampler uniform location or on resolving a shader resource name during submission.

For example, `WaterMediumIndicesTexture` should identify texture unit 7 through its binding contract. The generated property retains the assigned texture. When the shader is used, submission checks the prepared variant's binding entry and asks the existing GPU state cache to bind that texture to unit 7. No sampler uniform write is needed to repeat the fixed unit assignment.

The current sampler-location declarations identify existing GLSL uniforms; they neither create additional uniforms nor store previous CPU values. Removing their runtime binding role and retaining previous property values are related refactoring work, but solve different problems.

## Current implementation

- [ShaderBindingAttribute](../VanillaGraphicsExpanded/Rendering/Contracts/ShaderBindingAttribute.cs) now permits an inline `UniformLocation` on sampler and image declarations. Moving separate descriptors into this attribute simplified declarations but preserved the location dependency.
- [GpuProgramInterface](../VanillaGraphicsExpanded/Rendering/Spirv/GpuProgramInterface.cs) matches linked active uniform locations against the contract's name-to-location map. This avoids relying on preserved SPIR-V debug names.
- [ShaderBindingSubmission](../VanillaGraphicsExpanded/Rendering/ShaderBindingSubmission.cs) uses `ActiveUniform` when validating and publishing textures. [GpuProgramLayout](../VanillaGraphicsExpanded/Rendering/GpuProgramLayout.cs) also resolves locations in its active texture-binding path and applies sampler/image unit assignments through uniform access.
- [RuntimeSubmissionEmitter](../ShaderContractGenerator/RuntimeSubmissionEmitter.cs) emits a shader-owned state struct and equality checks for generated resource setters. Submission still validates and publishes resources on each use. Authored sources and owners with specialized submission require explicit coverage before the state design is complete.

An already linked program does not spontaneously lose a sampler. Different compiled variants can have different active interfaces because of conditional compilation and optimization. Resource activity belongs to the prepared executable, not to a repeated name lookup during drawing.

## Contract and prepared executable

Keep one authoritative authored binding contract. For each resource, it declares its kind, binding slot, compatible type, shader-stage and variant applicability, required-input policy, and relevant texture target or sampler policy. Names remain useful for diagnostics and offline source matching.

Generate stable contract-entry indices so runtime submission can address prepared entries directly. These indices are internal table indices, not GPU uniform locations or additional authored binding numbers.

Each linked executable owns an immutable prepared binding table. An entry contains its contract identity, activity, binding slot or ordinary uniform location, validated type and array extent, and applicable publication policy. The table belongs to that executable's generation. Switching variants selects the corresponding table; replacement publishes the executable and table together.

Prepare the table before exposing the executable for use:

1. Use the selected variant's offline contract metadata to establish declarations, applicability, types and fixed binding assignments. Reject conflicting declarations and missing applicable declarations during shader preparation/build validation.
2. Inspect the linked executable once. For samplers and images, enumerate active uniform resources, inspect their types and array extents, and read their initial unit assignments. Match these assignments against the contract's binding slots within the appropriate resource namespace. Numeric uniform locations used by this inspection remain internal to preparation.
3. Match uniform blocks, storage blocks and atomic-counter buffers through their binding metadata. Preserve ordinary standalone uniform locations for actual value uploads.
4. Validate types, slot ranges, stage agreement and array elements. Reject ambiguous overlapping assignments unless a supported alias is explicitly represented and validated. Never choose an arbitrary declaration sharing a slot.
5. Install the complete prepared table only after validation succeeds. Preserve the previous valid executable if replacement preparation fails.

Sampler units, image units and buffer binding points occupy distinct namespaces. A sampler on unit 7 and an image on unit 7 are not an alias. Resource arrays require element-aware metadata and validation rather than assuming one active resource always describes one slot.

The fixed layout is authoritative. An unexpected linked assignment is a preparation failure; the normal VGE path must not silently repair it by writing a different sampler unit.

If the offline compiler requires numeric sampler/image locations in its binary interface, generate those as compiler metadata with deterministic, collision-free allocation. They must not reappear as public binding properties or become dependencies of runtime texture submission. Verify the toolchain requirement before removing source qualifiers indiscriminately.

## Activity and missing-resource rules

Separate executable activity from the caller's obligation to supply a resource:

- A declaration excluded by the selected variant is inactive.
- A declared resource removed by legal optimization can be inactive. Preserve the existing allowance for optimized-away resources; absence alone is not evidence of a compiler or contract defect.
- A missing applicable declaration, incompatible active type, invalid binding assignment or ambiguous mapping is a preparation error.
- An active entry with `Required = true` requires a valid assigned resource before submission publishes any bindings.
- An active optional entry with no valid assigned resource clears its binding through the established abstraction. It must not inherit another shader's resource.
- An inactive entry requires no resource validation or binding operation.

Do not reinterpret `Required` as a demand that the compiler retain an otherwise unused uniform. If a particular shader needs an assertion that a resource remains active, represent that separately in validation and cover it with a focused test.

These rules are resolved into the prepared table. Submission does not repeat executable reflection, name lookup or name-to-location resolution for fixed-slot resources.

## Generated per-shader state

Generate a `<ShaderName>State` struct for every participating concrete shader class, with one correctly typed field for each non-UBO value or resource property in its binding contract. Layout descriptors are metadata, not value fields. C# structs are inherently non-inheritable; there is no `final` modifier to emit.

Each shader instance retains one active state value. Generated getters read it. Generated setters preserve the existing mutation/lifecycle checks, compare the incoming value against the retained value, and update state and change tracking only when different.

Use concrete `GpuTexture` subclasses for VGE-owned resources. Preserve raw engine texture IDs only at existing engine ownership boundaries, with their target and sampler policy. This work does not rename or change the ownership contract of `GBufferPosition`.

Define equality according to the property contract:

| Property | Comparison and retained information |
| --- | --- |
| Scalar, vector or matrix | Exact value comparison appropriate to the upload representation; no approximate comparison that discards intentional changes |
| Managed texture or buffer | Resource identity in desired state; current validity and allocation identity are checked when submitting |
| Image binding | Resource plus level, layer, layered flag, access and format |
| Buffer range | Resource plus byte offset and size |
| Engine texture ID | Numeric ID plus the contract's target/policy; lifecycle validity cannot be inferred from an unchanged integer |
| Mutable arrays or views | Owned value snapshot or an explicit content revision; reference equality alone is insufficient |

Authored getters must be sampled once per submission and compared into the same state model. Specialized submission implementations must use the shared prepared-binding/state mechanism or have a documented equivalent contract; silently excluding these shader classes is not completion.

UBO contents remain with their existing CPU staging, upload and ring-buffer owners. Keeping UBO contents outside the state struct does not remove their binding validation or publication obligations.

## Submission and GPU state ownership

Submission first snapshots inputs and validates the complete active resource set, then publishes through existing rendering abstractions. Validation failure must not publish a partial new set or mark pending values as successfully uploaded.

Use the prepared entries directly:

- Textures and images publish to fixed slots through the context-wide state cache.
- Buffer bindings publish their resolved resources and ranges through the existing buffer abstraction/cache.
- Ordinary standalone uniforms upload only when their value differs from the last successful upload to the selected executable, or when no successful upload exists for that executable generation.
- UBO owners continue to determine content uploads independently of resource-reference equality.

The shader state describes desired inputs. `GlStateCache` describes current context bindings. Both are necessary: shader A can retain texture X while shader B replaces its unit with texture Y. A's next use must restore X even though A's property did not change.

Consequently, unchanged desired resource values may still require a context-cache comparison. Suppress redundant GL operations through that cache rather than skipping rebinding solely from a shader-local dirty bit. This proposal does not promise zero CPU work for unchanged submissions.

Track ordinary uniform publication per executable generation, or conservatively invalidate its publication history on variant switches. A dirty flag cleared after updating one executable must never imply that another variant already holds the same value. First use must upload required values even when they equal the state struct's default values.

Resource recreation, retirement and context invalidation must remain observable when a retained reference is unchanged. Reuse existing resource generations and cache invalidation mechanisms where available; identify any missing lifecycle notification before introducing new tracking. Updating texture contents without replacing its allocation remains the texture owner's responsibility and does not inherently require a texture rebind.

Engine callbacks and external GL writes must respect the existing cache invalidation boundary. Route supported external uniform writes through publication tracking or invalidate the affected uniform history. Do not introduce a second independent global binding cache.

## Source responsibilities and migration

Keep contract parsing and diagnostics in the generator's contract layer, source layout and artifact metadata in `ShaderBuildTool`, executable inspection in the SPIR-V preparation layer, generated state/accessors in the generator, and binding publication in the rendering abstractions. Shader classes retain domain-facing inputs without duplicating reflection or cache policy.

The implementation should proceed in dependency order:

1. Complete the audit of every sampler/image location descriptor and inline location, plus ordinary uniform consumers. List each occurrence, affected shaders, current consumer and retention/removal reason in the linked audit. Do not infer that every shared location is redundant.
2. Establish prepared fixed-slot entries and contract validation, including arrays, inactive variants and executable replacement.
3. Route generated resource submission through those entries. Remove the fixed-slot sampler/image path's dependence on `ActiveUniform`, location resolution and `ApplyUniformUnitContract`.
4. Complete generated state and successful-publication tracking across generated and authored inputs and graphics/compute owners.
5. Remove redundant authored sampler/image locations, including the newly added `ShaderBindingAttribute.UniformLocation` mechanism once all its consumers migrate. Retain ordinary uniform locations and explicitly justified engine compatibility metadata.
6. Review compatibility consumers independently before removing a compatibility path. Update the audit, task evidence and affected documentation together.

Engine indexed-uniform dictionaries and externally owned shader APIs need an explicit consumer inventory. If they require sampler locations, populate adapter metadata during preparation. Such adapters must not force the VGE fixed-slot submission path back through name/location lookup. Preserve existing engine APIs and rendering output.

## Validation and completion criteria

Run builds and tests through subagents. GPU fixtures must use offline SPIR-V and existing resource, framebuffer, readback and shader abstractions. Do not add runtime GLSL compilation or direct GL operations to tests where an abstraction exists.

Required evidence includes:

- Generator coverage for complete non-UBO state, concrete resource types, authored sources, equal assignments, mutable inputs and invalid contracts.
- A shader sampling the expected texture through a fixed binding with no authored sampler-location descriptor or inline location attribute.
- Equivalent image binding coverage, sampler/image namespace separation, array handling and deliberate rejection of ambiguous mappings.
- Variant cases for excluded declarations, optimized-away resources, active required missing inputs and optional-input clearing.
- First-use ordinary uniform upload, changed-value upload and no redundant upload for an unchanged value on the same executable.
- A-to-B-to-A shader switching that restores shared texture/image/buffer slots correctly and handles per-executable uniform history.
- Texture/buffer recreation and retirement with an unchanged wrapper reference, raw engine-ID lifetime boundaries, external cache invalidation and failed replacement preparation.
- Failure-before-publication behavior for an invalid resource set, with pending upload state preserved for retry.
- Instrumented evidence that steady-state fixed-slot submission performs no executable reflection or uniform-location/name resolution, and that repeated identical bindings do not issue redundant GL operations.

Report assignment skips, preparation/reflection work, submission/cache checks, uniform uploads and actual GL binds separately. Expected reductions in CPU work are not measured frame-time improvements. Build and headless-test success do not establish live visual acceptance; the user runs the game for that verification.

Completion requires the full audit, focused validation, and independent review of compatibility removals. Merely relocating location numbers onto attributes or avoiding equal field assignments does not satisfy the design.
