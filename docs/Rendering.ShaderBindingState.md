# Generated shader binding state

The shared [GPU resource contract](Rendering.GpuResources.md) defines canonical metadata,
initialization, query policy, original associations and deletion. Binding knowledge and shader-local
compatibility preparation remain separate from that resource metadata.

## Executable ownership and current compatibility

Graphics `GpuProgram` and compute `GpuComputeProgram` implement internal `IGpuProgram`; shared lifecycle
workflows preserve authored and generated `CaptureBindingsCore` ownership. Generation recognizes both
families through abstract intermediates. Captures retain the exact `Shader` reference and its prepared
interface. ProgramLayout/ProgramId remain explicit asset/integration and diagnostic adapters rather than
a generated native publication route.

Prepared interfaces publish with the independent executable/revision/asset generation. Desired selections
and CPU blocks survive reload; captured tables keep their exact generation and bytes. Compute execution
uses change-delivered readiness under owner lifetimes. Graphics restoration and actual pass intervals
remain with their operation owners; attachment owners preserve storage through pass end. Every requested draw/dispatch publishes its intended inputs through
the executed cache; a reused table never suppresses actual rendering or uniform consumption.

Shader preparation and native geometry admission use the RHI preparation gate to reject work inside an
active engine restoration boundary. The gate consults existing thread-local boundary authority without native
context admission or queries, so CPU-only asset invalidation retains its context-free behavior.
Managed/engine geometry preparation and client reload/shutdown use this same gate. Native engine mesh
ownership stays with its adapter, while scoped upload/deletion and all VGE draw/dispatch execution stay
behind the admitted backend. Restored selector knowledge survives a failed upload.

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

Storage-range alignment validation consumes cached semantic RHI capabilities. Shader batch policy
likewise consumes the RHI parallel-compilation capability; originating context/generation checks remain
with the asset and lifetime owners. Neither helper selects a concrete cache or native capability variant.

## Prepared executable bindings

The C# attributes remain the authoritative contract. `ArrayLength` and optional `ShaderType`
constraints, texture targets and sampler policies are carried by generated resource declarations.
Texture dimensions come from explicit `TextureTarget` contract metadata, independently of wrapper
class names. Exact types use the engine-independent `ShaderResourceType` enum and match semantic
`ShaderValueType` facts through explicit cases; target compatibility also uses explicit cases. Unconstrained
dimensions remain flexible, and actual storage formats are resource inputs validated during initial
or relevant-change compatibility preparation rather than invented shader-type constraints.
`GpuBindingContract.Merge` rejects cross-stage disagreement and overlapping ranges within each
resource namespace. Resource entries receive deterministic indices ordered by kind and name;
these indices are independent of GPU binding numbers.

[RHIResourceManager](../VanillaGraphicsExpanded/Rendering/RHIResourceManager.Registry.cs) owns canonical
native program metadata and publishes snapshots after observed executable or interface changes.
[GpuPreparedBindings](../VanillaGraphicsExpanded/Rendering/Spirv/GpuPreparedBindings.cs) consumes
only a supplied complete snapshot, including sampler/image unit assignments, validates
contiguous array assignments and compatible types/targets, and records immutable element activity.
It also records UBO, storage-block and atomic-counter activity by binding. Sampler and image units
use separate namespaces. Uncontracted active slots and ambiguous mappings reject preparation;
inactive required entries remain legal. No sampler/image uniform writes repair a fixed assignment.
Preparation has no native program-ID parameter, native query path or manager acquisition fallback.
Unavailable snapshots, uninitialized collections and missing required typed fields or unit assignments
reject preparation. `GpuProgramInterface` requests canonical facts through the private RHI acquisition boundary when its caller
has no snapshot; the admitted backend invokes manager acquisition. That operation reuses maintained facts;
descriptor discovery requires a recorded pre-installation or supported EXT implicit-creation cause, and
unavailable original facts require explicit recovery. Missing tracking and local tools never grant read
permission. Graphics and compute candidates supply their existing owner snapshots directly.
Unit/block changes publish their supplied scalar/array values against the existing canonical interface,
without uniform getters or full reflection. Ignored locations, numeric uniforms and proven rejected
assignments preserve revisions and active certificates. Unavailable executable facts remain unavailable;
an assignment cannot authorize recovery or revive a withdrawn executable generation.
Meaningful unit/block changes revalidate prepared resource tables through the existing revision machinery;
relinking or binary installation withdraws executable preparation until the owner validates its replacement.
Refreshing assignments first clears the active binding projections as well as withdrawing prepared access.
If compatibility preparation fails, actual canonical assignments still reach aliases, while no previous
active sampler/image/block projection remains available as a successful refresh result.
Cached driver executables retain their candidate owner during installation; the manager delivers the
linked snapshot before graphics/compute preparation, including tools without an installed registry.
Ordinary numeric uniform writes do not refresh resource metadata. Managed ordinary uniforms use core named ProgramUniform operations on the admitted OpenGL 4.5 context.
Exact engine overloads retain their original native semantics at the external boundary.

Native shader factories establish fixed stages; program factories publish complete fresh unlinked
defaults without reflection. Combined shader-program factories still require link output. External
parallel linking leaves the original entry unavailable while pending; an explicit native link-status or
positive completion-status result completes its driver-produced interface. Managed pending candidates
use saved-original status reads and their own single completion publication. Pipeline Gen reserves its
identity; Bind, UseProgramStages, ActiveShaderProgram, validation and explicit parameter getters realize
that reservation where specified. Its canonical schema remains empty; selectors stay execution-owned.

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
the supplied collections. Reflected resources use `ProgramResourceKind`, nullable typed location/extent/block
fields and `ShaderStageMask`; native property selectors and type tokens never enter those snapshots.
Preparation receives texture/image-unit limits as supplied values and requires no context admission.

Validation covers fixed sampler/image namespaces, array activity, incompatible types, supplied metadata,
executable replacement and failure preservation. Generated submission consumes the resulting table.

Program snapshots are assigned before the shared internal resource-change callback reconciles preparation.
Structurally equal interfaces/assignments retain metadata revisions and prepared facts; equal
republication still supplies missing snapshots. Executable replacement remains a change even when its
interface is equal. Published withdrawal/recovery invalidates unavailable preparation, while temporary
operation exclusion alone does not emit availability transitions. Tracked fixed shaders check original
registration/revisions; local fixed shaders also retain their exact prepared-interface identity and
preparation invalidation, so equal-interface replacement cannot silently preserve an obsolete executable.
Managed assignment exclusion remains held through callbacks. Notification failure after commit never
withdraws the successfully installed executable or retries linking. See [resource publication](Rendering.GpuResources.md#snapshot-and-change-publication).

## Prepared resource submission

Texture and buffer references remain non-owning. Owners preserve their selected storage through immediate
execution or execution/discard of retained commands, and withdraw consumers before incompatible replacement.
An owner-following texture selection refreshes after explicit replacement; borrowers and native views remain
tied to their original associations. Canonical resource facts stay with the manager.

Generated `CaptureBindingsCore` samples authored getters once and retains generated desired state. Its input
revision changes only for actual resource-selection edits. Arrays are copied on assignment/capture. The
resulting `RHIResourceBindings` freezes numeric authored identities, exact executable identity, sampler array
elements, image selectors, buffer intervals and logical uniform snapshots. Capture is CPU-only: it performs
no native queries, slot publication, physical uniform allocation or resource-lifetime acquisition.

Each retained resource structure projects its exact shader interface into active semantic entries. Excluded
and inactive inputs do no compatibility, subscription or publication work. Required absence rejects
preparation; optional absence is an explicit clear of the declared slot/target. A present but unavailable
resource never becomes an optional clear. Preparation establishes texture target/format, image view/mip/layer,
buffer extent/alignment and linked-block relationships before native publication. It stores derived results
and non-owning selections, without a duplicate canonical descriptor registry.

Initial compatibility preparation occurs on the first use in an admitted context. Relevant committed resource
changes withdraw that structure through weak subscriptions on the existing resource-change delivery path;
the next use refreshes it before execution. Failed preparation remains unavailable until another relevant
change, with no stale-result fallback, per-use metadata polling, native discovery or automatic retry.
Ordinary content writes and unrelated registrations do not visit structural preparation. Abandoned tables
and their subscriptions are collectible; resources do not retain consumer owners.

Logical uniform versions are independent of resource structure. Generated capture uses `WithUniforms` when
only CPU block revisions change, so old recordings retain their captured bytes while both tables share
structural preparation. Changing a uniform/storage buffer declaration still creates a new structure.
Immutable captured block sizes are checked once. Execution retains the actual open-epoch, allocation,
consumption, commitment and completion protocols of `UniformPublication` and the existing ring allocator.
Only successfully bound captured versions commit CPU history; newer CPU edits remain pending. Single-draw
consumption advances when a draw or dispatch is about to execute, rather than during recording or selection.
Different captured versions and executable consumers use their actual captured publication/version pairs.

The backend weakly associates each semantic structure with native `OpenGLBindingPlan` realization. Native
names and resolved image formats refresh only when that structure changes. Texture slot ordering and active
partitions are retained across uniform-only captures. This is derived backend realization under owner
lifetimes, never another registry or ownership certificate. Shared policy samplers keep their existing
context owner and invalidate the actual structures that depend on them when retired.

Publication walks compact active partitions and consults the existing context-wide slot cache. There is no
whole-table receipt invalidated by arbitrary observation. Known-equal assignments suppress native work;
external changes restore only affected texture/sampler/image/buffer bindings without revalidating resource
compatibility. Indexed buffer keys include range/whole intent and their generic selector side effect.
Optional texture clears preserve unrelated targets on the same unit. Current-pass sampler/image overlap
relationships are cached for the selected target generation, texture selection and pipeline write state; relevant
changes withdraw them. These checks remain separate from shader/resource compatibility and infer no synchronization.

Native slot writes withdraw affected knowledge before dispatch. Per-unit/per-buffer-selector and active-unit/program mutation stamps
detect overlapping observer reentry; interrupted fields remain unknown, the managed command reports failure,
and already-issued native work is never retried. Other units/domains remain independent. Intercepted native
calls still forward exactly once and contain observer exceptions. Context retirement discards originating
knowledge; deletion and restoration reconcile the existing affected selectors.

`RHISampledTextureBindings` provides the same retained declaration contract for explicit engine sampled
slots without manufacturing an owned shader. Its constructor copies/sorts supplied slots and rejects duplicate
or malformed declarations; relevant resource changes withdraw its preparation. Immediate and recorded
commands retain the immutable table. Engine atmosphere, terrain atlas, chunk mapping and energy-lookup owners
reuse declarations and withdraw them at their existing publication/replacement boundaries. One-off arrays
or spans convert into copied declarations; callers that reuse selections retain the resulting object.
Direct buffer bindings use producer-delivered snapshot readiness and bounded new role/slot/range checks;
original-association misuse checks are DEBUG diagnostics. Dynamic ranges retain mandatory bounds/alignment
handling without repeated manager resolution in ordinary Release.

## Generated desired state and ordinary value publication

The generator emits a nested `<ShaderName>State` for each participating concrete graphics/compute owner.
Resource properties preserve mutation checks and increment the shader-local revision only when desired
values change. Authored getters are read once per capture; generated properties use retained state.
`ShaderInputSubmissionStateTests` verifies the complete inherited contract. Descriptors and CPU uniform
contents are separate from resource revision tracking.

Ordinary numeric data remains in the existing CPU uniform owners. Captures copy coherent bytes and versions;
physical allocation/upload and successful-history commitment occur only during execution in an open epoch.
Static catalogs and explicitly authored capture implementations retain their own declaration ownership and
use the same context execution contracts. Engine `ShaderProgramBase` numeric setters keep their native
semantics; no global ordinary-uniform history or inherited engine dictionary is introduced.

## Shader ownership and publication

`GpuProgram` and `GpuComputeProgram` retain asset, requested-setting and generated-input policy.
Their installed `RHIShader` owns a completed native executable and supplied prepared interface.
`ComputeShaderAsset` composes that shader with a lightweight compute pipeline. Graphics and compute
pipelines retain exact shader-generation references; realization never repeats linking or reflection.
Failed replacement preserves the installed set, while successful replacement does not retarget existing
pipeline references or recorded resource tables. See [GPU resources](Rendering.GpuResources.md#shader-executable-ownership).

`RHICommandContext` selects pipelines, admits pass/boundary usage and executes resource publication.
Typed binding structures establish supplied compatibility behind that boundary; the backend executes their
native realization under owner-guaranteed resource lifetimes.
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
the captured successful revision, preserving newer edits. A missing epoch or withdrawn preparation rejects use. Owners finish or discard recorded work before
retiring its dependencies; a list does not extend engine callback lifetime.

Texture owner replacement remains visible through `GpuTextureRef`; borrowers/native views retain original
associations and dependencies. `OpenGlStateCache` owns actual slot knowledge and deletion/restoration
effects independently of resource compatibility preparation. See [recorded ownership](Rendering.GpuResources.md#recorded-command-ownership).

## Verification boundary

Prepared-binding, generated-input, uniform-lifetime, RHI shader/pipeline/recording and engine-boundary tests
cover these contracts against supplied metadata and headless output/state checks. Exact adapter tests
preserve signature and native effects. Test receipts and qualification limits belong to the implementation
checklists; headless checks do not establish installed-game appearance or performance.
