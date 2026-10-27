# Generated shader binding state

The shared [GPU resource contract](Rendering.GpuResources.md) defines canonical metadata,
initialization, query policy, original associations and deletion. Binding knowledge and shader-local
compatibility history remain separate from that resource metadata.

## Executable ownership and current compatibility

Graphics `GpuProgram` and compute `GpuComputeProgram` implement internal `IGpuProgram`;
shared default workflows use concrete lifecycle state and deliberate facades, preserving authored
and generated `Submit`. Runtime/offline generation recognizes both families through abstract
intermediates. Family owners expose ProgramLayout/ProgramId for generated publication; direct
pipeline adapters retain their explicitly accessible pipeline-field path.

Prepared interfaces publish atomically with the independent executable/revision/asset generation.
Desired values and CPU blocks survive reload, while successful-upload history follows the installed
generation. OpenGlStateCache's existing scopes carry owner/revision/context borrows; native engine globals
are not a VGE ownership channel. Replacement and retirement reject active borrows. Every managed draw
publishes intended inputs even when the binding ID is unchanged.

Shader preparation and native geometry admission use the RHI preparation gate to reject work inside an
active engine restoration boundary. The gate consults existing thread-local boundary authority without native
context admission or queries, so CPU-only asset invalidation retains its context-free behavior.

The world `VgeFrameUBO` remains one shared snapshot. An early `Before` callback withdraws the
previous publication; capture runs at order `0.001`, immediately after native camera and ambient
preparation at order zero and before liquid depth at `0.995`. Projection is copied immediately
from engine scratch storage; view comes from the dedicated `CameraMatrixOriginf`. Continuity
samples player orientation at that same boundary, before perception effects alter future angles.
Shadow and Opaque consumers reuse this snapshot without recapture or another frame-block upload.

`GpuProgramInterface` retains preparation-owned diagnostic name/location projections and raw-handle
fixture helpers. It does not populate an inherited engine dictionary. Public VGE shader classes no
longer implement engine `Uniform`/`HasUniform`/texture APIs or `IShaderProgram`; use typed retained
bindings/CPU blocks instead. `LiquidPoolInputAdapter` translates the three supported logical pool
inputs without engine executable ownership. Real native terrain/material/atmosphere/frame/light hooks
retain their engine interfaces. See [supported replacements](GpuProgram.md#shared-workflows-and-compatibility).

## Prepared executable bindings

The C# attributes remain the authoritative contract. `ArrayLength` and optional `ShaderType`
constraints, texture targets and sampler policies are carried by generated resource declarations.
Texture dimensions come from explicit `TextureTarget` contract metadata, independently of wrapper
class names. Exact types use the engine-independent `ShaderResourceType` enum and compare numeric
identifiers with the linked interface; target compatibility uses explicit enum cases. Unconstrained
dimensions remain flexible, and actual storage formats are resource inputs validated during
submission rather than invented shader-type constraints.
`GpuBindingContract.Merge` rejects cross-stage disagreement and overlapping ranges within each
resource namespace. Resource entries receive deterministic indices ordered by kind and name;
these indices are independent of GPU binding numbers.

[GpuResourceManager](../VanillaGraphicsExpanded/Rendering/GpuResourceManager.Registry.cs) owns canonical
native program metadata and publishes snapshots after observed executable or interface changes.
[GpuPreparedBindings](../VanillaGraphicsExpanded/Rendering/Spirv/GpuPreparedBindings.cs) consumes
only a supplied complete snapshot, including sampler/image unit assignments, validates
contiguous array assignments and compatible types/targets, and records immutable element activity.
It also records UBO, storage-block and atomic-counter activity by binding. Sampler and image units
use separate namespaces. Uncontracted active slots and ambiguous mappings reject preparation;
inactive required entries remain legal. No sampler/image uniform writes repair a fixed assignment.
Preparation has no native program-ID parameter, native query path or manager acquisition fallback.
Unavailable snapshots, uninitialized collections and missing required properties or unit assignments
reject preparation. `GpuProgramInterface` requests canonical facts through the private RHI acquisition boundary when its caller
has no snapshot; the admitted backend invokes manager acquisition. Graphics and compute candidates supply
their existing owner snapshots directly.
Unit/block changes revalidate prepared resource tables through the existing revision machinery;
relinking or binary installation withdraws executable preparation until the owner validates its replacement.
Cached driver executables retain their candidate owner during installation; the manager delivers the
linked snapshot before graphics/compute preparation, including tools without an installed registry.
Ordinary numeric uniform writes do not refresh resource metadata. Managed ordinary uniforms use core named ProgramUniform operations on the admitted OpenGL 4.5 context.
Exact engine overloads retain their original native semantics at the external boundary.

[GpuProgramInterface](../VanillaGraphicsExpanded/Rendering/Spirv/GpuProgramInterface.cs) owns this
table and retains the existing ordinary-uniform and diagnostic adapters. Graphics and compute
candidate layouts already transfer their interface together with the executable after validation;
failure retains the installed generation. Existing binary digests, source/layout cache keys and
fresh interface validation protect replacement without a new sidecar format or GLSL-derived
binding contract. GLSL parsing remains solely in the existing layout-injection operation.

Layout cache rebuilding and binding validation reuse immutable active-binding maps derived from
the prepared table. Diagnostic resource-name adapters reuse recorded locations and indices.
Neither path repeats linked-interface enumeration. Native executable discovery belongs to the manager's
reader boundary; preparation projects block sizes, stage ownership and native diagnostic indices from
the supplied collections.

Validation covers fixed sampler/image namespaces, array activity, incompatible types, supplied metadata,
executable replacement and failure preservation. Generated submission consumes the resulting table.

## Prepared resource submission

Texture inputs use `GpuTextureRef` for owned textures, borrowers and native views. Wrapper-owned implicit
conversions retain the managed object without exposing lifecycle operations to the shader contract.
`GpuTextureBinding` combines that reference with explicit image access, mip, layer and format selection.
Native-view selections are relative to the view; source offsets are used only for shared-storage checks.
Resource usability belongs to `GpuResourceManager`, while shader compatibility and pass feedback checks
remain at submission. A reference follows intentional replacement by its owning wrapper, so equality
alone never suppresses current-identity, availability or compatibility checks.

Generated `Submit` snapshots each source once, selects entries from the installed prepared table,
validates the complete active input set, then supplies a frozen `RHIResourceBindings` table for context execution, using
[ShaderPreparedSubmission](../VanillaGraphicsExpanded/Rendering/ShaderPreparedSubmission.cs).
Activity and required/optional policy come from the executable's entries. Inactive inputs are skipped;
missing required allocations fail before publication; optional absence clears the corresponding slot.
Ordinary uniform and engine-facing location adapters remain available for their inventoried consumers.
Validation checks texture targets and signed/unsigned/floating interpretation, image format compatibility,
selected mip/layer bounds and storage-range alignment. An explicit single-layer view of layered storage
uses the layer's dimensionality. Texture-only image properties select the whole allocation when the
linked shader expects a layered image; optional absent images clear with canonical valid view parameters.

The generator emits deterministic numeric resource identities from the authoritative kind/name pair.
Preparation maps those identities to its immutable entries and rejects collisions. This handles owners
with multiple program contracts without assuming that an entry's table index or fixed slot identifies
the same desired property across programs. Names serve preparation and diagnostics; submission does
not reflect the executable or perform name/location lookup. Array publication respects element activity.

Generated inputs retain a shader-local `ShaderInputValidation` value. Prepared-entry resolution runs
only when the installed table changes. Successful texture/image/range compatibility checks are reused
while the resource reference, allocation metadata, manager-owned metadata/storage revisions and desired
view/range remain equal. Snapshot availability is checked before reusing compatibility history;
uncached inputs and arrays also reject unavailable metadata. Explicit storage ranges remain bounded by
the current allocation. Owner metadata is sampled without driver queries; current lifetime
is still checked before publication. Resize, capacity changes and executable replacement invalidate the applicable result. Failed checks never
replace a successful result. Context-wide binding comparisons and UBO content uploads remain independent
of this compatibility history.
Managed resources resolve their current allocation and validity on each use. Raw engine IDs are checked
once during validation, and their texture slot cache is invalidated before binding because equal IDs
cannot establish external allocation identity. CPU uniform-block uploads and dirty-state commitment
remain with `CpuUniformBuffer` and the existing ring allocator, using the prepared binding slot.

Cloud map, primary visibility and liquid-depth consumers retain typed non-owning wrappers through the
common resource manager. Stable selections use published metadata and ordinary slot comparisons;
deletion invalidates their original associations before a reused name can be selected again. Simple
cloud liquid depth uses the same typed route. Material depth is supplied as its existing owned texture.
These covered paths do not use raw-ID slot invalidation. Unsupported raw callers retain the conservative
validation and binding behavior above; registry membership does not extend interception coverage.

[OpenGlStateCache.ResourceSlots](../VanillaGraphicsExpanded/Rendering/RHI/Backends/OpenGL/State/Observation/OpenGlStateCache.ResourceSlots.cs)
adds complete image-view and indexed buffer-range keys to the existing context cache. Cache invalidation,
immediate and queued deletion, image scopes, buffer texture views and engine terrain buffer binds use
the same owner. Debug-only image/buffer slot bind counts distinguish actual driver calls from cache comparisons.
No second global cache or texture attachment is introduced.

## Generated desired state and ordinary value publication

`RuntimeSubmissionEmitter` emits a nested `<ShaderName>State` and active instance for every
participating concrete graphics/compute owner, including empty states for UBO-only owners.
`ShaderInputSubmissionStateTests.EveryConcreteOwnerHasCompleteGeneratedState` checks every
main-assembly concrete owner and its complete inherited non-UBO contract field names/types.
Descriptors and UBO contents are excluded. Generated accessors preserve mutation guards;
reference-valued resources compare identity, image/range records compare their full payload,
and ordinary numeric values compare exact upload bits. The shader-local input revision changes
only on an actual desired-value change; it never suppresses context-wide resource checks.

Authored getters are sampled once per submission and compared into the same retained state.
Ordinary arrays are copied on retention and on generated getter exposure, so caller edits cannot
mutate generated state. Authored arrays are compared by content and captured when changed.
Existing liquid/lighting matrix and array facades continue to copy into their existing UBO owners.

`ShaderUniformPublication<T>` retains the last successful value and immutable executable identity.
First use uploads even a default value; unchanged values on that executable skip. Selecting another
executable forces an upload, including returning to the first executable after a switch. Failed
validation publishes no uniform values; a throwing upload does not commit successful history.
Typed ordinary values use actual `UniformLocation` contracts and retained linked type/array extent.
The offline `tests/uniform_state` fixture reads those float/array/vector/matrix values to produce
an image. Its locations are shader value addresses, never storage for CPU texture IDs or history.
Its specialization option is compiled by the existing specialization owner, independently of
ordinary uniform publication.

The inventoried production assembly has no authored `Submit` override. All its concrete shader
owners use generated state/submission. Static catalogs have no instance state; their supported
pipeline/fixture consumers retain the existing immediate-publication contract and are covered by
the catalog and binding regressions. Test-only `Submit` overrides deliberately instrument guards,
restoration or lifecycle and keep that explicit contract. `InterfaceBindingTests` retains coverage
for the alternate authored-submission emitter; its compatibility cleanup remains separately tracked.
This does not promise automatic retained publication for a future custom override.

## Shader ownership and publication

`GpuProgram` and `GpuComputeProgram` retain asset, requested-setting and generated-input policy.
Their installed `RHIShader` owns a completed native executable and supplied prepared interface.
`ComputeShaderAsset` composes that shader with a lightweight compute pipeline. Graphics and compute
pipelines retain exact shader-generation references; realization never repeats linking or reflection.
Failed replacement preserves the installed set, while successful replacement does not retarget existing
pipeline references or recorded resource tables. See [GPU resources](Rendering.GpuResources.md#shader-executable-ownership).

`RHICommandContext` selects pipelines, admits pass/boundary usage and executes resource publication.
`ShaderPreparedSubmission` validates supplied tables and resources behind that boundary; it is not a
second execution context. Cached compatibility never suppresses current resource/lifetime checks.
Physical uniform storage and successful history are committed only when publication executes.

## Retained compatibility boundaries

| Boundary | Current responsibility |
| --- | --- |
| `GpuProgramInterface.Uniforms` / diagnostic location projections | Recorded preparation metadata and raw-handle fixtures; no inherited engine uniform dictionary. |
| `GpuProgramLayout` resource lookup | Supplied metadata, diagnostics and fixed active-binding identities. |
| `GpuProgramLayout.ApplyUniformUnitContract` | Explicit preparation of externally owned engine programs, including atmosphere sampler assignments. Generated owned contracts bypass this native integration path. |
| `LiquidPoolInputAdapter` / `ILiquidPoolInputs` | Engine origin/model-view/transparency inputs copy into the existing visible/depth UBO owners without executable ownership. |
| Frame, terrain and atmosphere hooks | Engine membership/location/assignment policy; ordinary resource binding submits typed RHI inputs. |

Resource contracts describe fixed sampler/image units, stage applicability and required/optional policy,
not authored opaque uniform locations. Ordinary numeric values retain real upload locations. Program-
separated slot reuse is legal; conflicting simultaneously active ranges reject contract preparation.
Desired values remain per shader instance and successful-upload history follows the installed executable.
Engine `ShaderProgramBase` setters retain their own behavior; no global external-uniform history is added.

## Recording and lifetime

Recorded binding tables retain immutable interface and executable identities and copied logical uniform
snapshots. Recording allocates no physical ring storage, publishes no successful history and never clears
dirty CPU work. Execution allocates or reuses storage in the current publication epoch and commits only
the captured successful revision, preserving newer edits. A missing epoch, retired executable, unavailable
resource, expired borrow or incompatible table rejects use; a list does not extend engine callback lifetime.

Texture owner replacement remains visible through `GpuTextureRef`; borrowers/native views retain original
associations and dependencies. `OpenGlStateCache` owns actual slot knowledge and deletion/restoration
effects independently of shader-local compatibility. See [recorded ownership](Rendering.GpuResources.md#recorded-command-ownership).

## Verification boundary

Prepared-binding, generated-input, uniform-lifetime, RHI shader/pipeline/recording and engine-boundary tests
cover these contracts against supplied metadata and headless output/state checks. Exact adapter tests
preserve signature and native effects. Test receipts and qualification limits belong to the implementation
checklists and migration records; headless checks do not establish installed-game appearance or performance.
