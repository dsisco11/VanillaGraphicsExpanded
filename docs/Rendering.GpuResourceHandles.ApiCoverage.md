# OpenGL resource metadata API coverage

This is the API-derived planning matrix for [the proposal](Rendering.GpuResourceHandles.Proposal.md).
Current callers do not define or limit coverage within the supported API scope. Every command in a family, every supported desktop extension
variant and every managed overload has the implementation and verification obligation specified below.

## Publication policy

The [approved command-publication policy](Rendering.CommandPublication.Proposal.md#approved-command-result-policy)
controls how the families below publish metadata. Here, "refresh" means make canonical facts current;
it does not mandate native readback after controlled commands. Controlled inputs/defaults/derivations
are authoritative under existing failure handling. Unknown external effects use acquisition/recovery;
required driver-output reads are individually justified in that proposal. Existing scope, native-error,
per-object outcome and lifecycle obligations remain. No broader command-success verification is added.

## Explicit API exclusions

Interception is restricted to the managed native owners GL, GL.Ext and GL.Arb.
The pinned OpenTK binding exposes core methods directly on GL; adapter classes named Core
refer to that owner. All other auxiliary owners are excluded, including Amd, Ati, Ibm, Intel,
Mesa, Oes, Ovr, Sgis, Sun, Apple and NV. This is an owner boundary, not a filter on feature origins,
command suffixes, extension capability checks or direct VGE resource operations.
Unmapped calls execute unchanged and do not provide continuing metadata or lifecycle observation.

Core, EXT and ARB operations remain required even when similar functionality also exists in an
excluded owner. Do not add interception or verification obligations for other native owners.
Legacy OpenGL display-list compilation and execution APIs, including NewList, EndList, CallList and
CallLists, are excluded. Native commands compiled into or executed indirectly through such lists are
outside this interception contract. Legacy pre-OpenGL 3.0 fixed-function/compatibility facilities are
also excluded, including matrix stacks, server/client attribute stacks, client default resets and
legacy client-array state, alpha testing, point smoothing, separate polygon face modes and line/polygon stipple. Legacy automatic mip
generation is excluded; explicit modern mip generation remains required. Commands retained in modern
OpenGL remain supported even if introduced before 3.0. These exclusions are unrelated to GpuCommandList.

## Excluded command dispositions

Matrix-stack and display-list commands have no mappings. Core/EXT server and client attribute
stacks/default resets and fixed-function client arrays have no observation tokens or VAO revision
publication. The excluded vertex families include color/normal/texcoord/index/fog/edge/secondary-color
pointers and their EXT, IBM, INTEL and SUN aliases, ARB matrix-index/weight arrays, ATI array/element/variant
objects, EXT variants and fixed-function VertexArray*Offset/EnableVertexArray aliases. Generic shader
VertexAttrib* operations on GL/ARB/EXT retain their supported routes; ATI generic attributes are excluded.
AlphaFunc and line/polygon stipple have no StateCache mappings or PSO fields. Enable/Disable still
forward unsupported capability values unchanged without caching them. Line/polygon smoothing,
programmable point origin/size, clip control, depth and blend state remain supported.
Subimage/copy-subimage/compressed-subimage commands are content-only and have no metadata adapters.
Texture parameter adapters observe base/max levels, not GENERATE_MIPMAP. Explicit mip generation
remains T2. Historical baseline inspection inputs remain unchanged; active comparisons apply these
semantic exclusions rather than treating removed entries as missing implementation.

## Authority and expansion rules

Use the [Khronos OpenGL registry](https://registry.khronos.org/OpenGL/) and
[gl.xml](https://github.com/KhronosGroup/OpenGL-Registry/blob/main/xml/gl.xml), including desktop GL
core/compatibility features and extensions whose supported API includes `gl` or `glcore`. ES-only APIs
are a separate API; desktop extensions are included only through the allowed managed owners above. The retained
registry snapshot has SHA256 `B9CA2CFA5C676E901C20D34AF3407F1687CDE0F1336A5FF7A8974D04C7494AD3`.
The [expanded matrix](../artifacts/gpu-resource-handles-baseline/api-command-matrix.csv) preserves native
signatures from the retained baseline expansion; [the expansion script](../artifacts/gpu-resource-handles-baseline/classify-api.ps1) and
[registry snapshot](../artifacts/gpu-resource-handles-baseline/gl.xml) make that enumeration reproducible.
These are API inspection artifacts, not reflection tests or a whitelist of callers. The retained baseline
artifacts enumerate a broader API set; apply the explicit exclusions before interpreting support obligations. The expanded names
below also include subsequent field-to-command reconciliation; retain the baseline artifacts unchanged.

The family rules are authoritative over the explicit adapter inventory. Review semantics for aliases and
indirect effects; a missing spelling must be added, never inferred to be out of scope. Future registry
additions require the same classification. The command list below records the current expansion.

Implementation means a StateCache route that executes the native command, identifies affected object
registrations, and forwards the actual effect to GpuResourceManager to update or invalidate canonical metadata. Bound operations
resolve selectors; DSA uses explicit names; multi-texture operations use explicit units; multi-bind and
multi-resource operations process all affected objects. Proxy texture targets alter proxy state, not a texture
object, and must not notify a bound object. Preserve errors, partial success, and existing binding effects.

Every OpenTK representation of each command is required: signed/unsigned IDs, pointer/IntPtr, ref/out,
array and generic overloads, and the allowed nested extension classes. Normalize to one semantic handler without
double notification. Extend exact adapter mapping and MethodSpecification discovery for generic calls;
no fixed set of observed element types is the coverage boundary. Missing binding entry points need a
capability-gated native delegate with the specification's ABI. Capability absence blocks native execution,
not adapter implementation. Existing assembly interception boundaries remain explicit; arbitrary native
code bypassing the StateCache is not silently tracked.

Metadata adapters are ordinary C# methods under `HarmonyPatches/NativeAdapters`, grouped by command
family and native owner. Adapters forward the exact signature to explicit StateCache
execution methods under `Rendering/Commands`; those methods own native dispatch and completion together.
Observation tokens remain internal to those execution paths, including vertex revision publication. `MetadataAdapterCatalog` declares owner pairs; initialization matches exact method
names, return/parameter types and generic constraints. Closed generic calls instantiate those compiled
adapter definitions with the caller's types. No runtime adapter IL emission, command-name pattern
classification or parameter-name inference participates in dispatch. The existing engine IL discovery
and call-operand replacement remain the interception boundary.

## Canonical destinations and publication route

The [canonical schema mapping](Rendering.GpuResourceHandles.Inventory.md#canonical-metadata-schemas-and-field-migration)
is the field-level destination for this matrix. T1–T4 target GpuTextureMetadata (including buffer/view
associations); B1 targets GpuBufferMetadata; R1 targets GpuRenderbufferMetadata; F1 targets framebuffer
attachment/default metadata while routing remains in StateCache. V1 invalidates VAO entry revision and
helper assumptions; it does not introduce a full layout mirror. P1 refreshes native program facts and
invalidates derived preparation. L1 retires entries/aliases and reconciles affected dependencies. I1
applies those same destinations only where their execution has a represented metadata effect.

Every route is native command -> StateCache observation -> context's GpuResourceManager -> canonical
struct update -> typed wrapper snapshots -> existing dependent validation/publication. Read canonical
facts once per change, not once per wrapper. Texture and renderbuffer changes also project selected
GpuImageMetadata to associated framebuffer images. Each kind has one canonical struct even when several
wrapper classes expose different aspects. No borrowed registry, StateCache metadata registry, abstract
per-wrapper query handler or generation-based resource key is introduced.

All enumerated API families/variants within the explicit exclusions remain in scope. Moving fields to canonical structs
does not narrow API coverage. Empty per-kind structs and owner-only fields retain the explicit semantic
dispositions below. Initial fixed shader type acquisition requires no new mutable shader-storage family.
The metadata revision used to invalidate stale assumptions is registration bookkeeping, not GPU contents.

## Families, implementation and verification

| ID | Metadata / API family | Implementation obligation | Verification obligation |
| --- | --- | --- | --- |
| T1 | Image definitions: TexImage 1D/2D/3D, compressed image definitions, CopyTexImage, immutable/multisample storage, DSA and EXT multi-texture equivalents, external-memory allocation | Refresh affected dimensions, format, samples, level allocation and immutable-storage facts. Copy definitions change storage even though their source is pixels. Same object retains identity. Unsupported typed dimensionality must become explicitly unavailable, never falsely represented by a 2D/3D snapshot. | Allocate/redefine every supported target/dimension, nonzero mip and cube face; rejected allocation retains actual facts; cover pointer/array variants, DSA, explicit unit and extension aliases. |
| T2 | GenerateMipmap, GenerateTextureMipmap, GenerateMultiTexMipmap and aliases | Refresh the generated mip range; preserve base/max-level semantics. | Missing levels become allocated; same registration/revision change; base level other than zero; failed/incomplete cube generation. |
| T3 | Texture buffer whole/range attachment, texture views and renderbuffer-backed textures | Refresh source association, format, range and selected levels/layers; a view has its own native identity. Preserve whole-buffer sentinel conventions and backing dependencies. | Change backing/range; delete or resize backing; view source identity versus view identity; reject invalid ranges without assumed success. |
| T4 | All texture parameter setters, bound/DSA/multi-texture and integer/float/vector forms | Classify pname effect. Refresh represented facts if affected; retain owner-only policy distinctions. Track represented base/max levels. Do not invent wrapper metadata for unrepresented parameters. | Parameter-family coverage and pname-dependent effects; base/max-level cases; unchanged storage does not spuriously replace identity. |
| B1 | BufferData, BufferStorage, named variants, external/memory-backed storage | Refresh actual capacity/usage and represented storage facts; preserve mapping ownership and authored EBO interpretation. | Bound/DSA capacity changes, zero size, immutable rejection, all overload forms and external-memory capability cases. |
| R1 | RenderbufferStorage, named storage, multisample/coverage aliases | Refresh actual dimensions, format and sample values, propagate to dependent attachments. | Bound/DSA allocation, rounded sample counts, rejection and alias refresh. |
| F1 | Framebuffer texture/renderbuffer attachment, face/layer variants, default-parameter and draw/read-routing variants | Refresh attachment identity/subresource and derived target metadata; preserve ownership. Route draw/read-buffer commands through the existing routing cache. Classify default-FBO parameters separately from window-system surface publication. | All attachment forms, detach via zero, split read/draw targets, partial configurations and affected publication revisions. |
| V1 | Vertex attribute pointer/format/binding/divisor/enables, vertex-buffer multi-bind, element-buffer binding | Revise represented VAO configuration and stale helper assumptions. Resolve current VAO and latched buffer dependencies. Fixed-function client arrays are excluded; generic shader attributes remain supported. | Bound/DSA, multi-bind partial failure, generic/integer/double formats, enablement/divisor, EBO association, stale helper use and unrelated VAOs. |
| P1 | Link/binary installation, shader-program creation, interface block bindings, sampler/image unit assignments and link-input configuration | Invalidate reflected executable layout on actual relink/binary/interface changes; use existing preparation flow. Uniform1i/1iv and ProgramUniform1i/1iv affect retained native unit facts only at sampler/image locations. Bindless-handle assignment at those locations makes the unsupported unit representation unavailable. Ordinary numeric uniforms remain content. Attach/detach/bind-location/transform-feedback declarations affect the next link rather than inventing an immediate new executable. | Successful/failed relink, binary replacement, block/unit-binding changes, sampler/image array locations, ordinary numeric writes excluded, bindless unavailability, deferred link-input effects, prepared submission revalidation. |
| L1 | Deletion of every represented kind, all aliases and array/ref/pointer forms | Retire the captured registration; preserve deferred native destruction and resource-specific unbinding. ARB generic object deletion resolves shader/program kind. Propagate affected attachment/VAO metadata. | Each kind, lists and duplicate/zero names, queued old identity, same-name replacement, aliases and retained native references. |
| I1 | External storage association and sparse residency operations | Storage attachment/replacement receives corresponding T/B metadata refresh. Residency-only commitment, access flags and performance hints have no represented-size/format change; explicitly preserve that distinction. Interop registration/mapping that defines texture storage must refresh it. | Extension-capability tests for association changes; commitment without dimension changes; unavailable imports cannot publish valid metadata. |

For every row, verify notification of all aliases, failure isolation, context/name reuse and dependent
validation revisions. Exercise native adapters through actual redirection as well as direct StateCache
calls. Native integration tests require supporting hardware; use deterministic adapter/dispatch tests
for absent capabilities and retain the unexecuted native cases as explicit qualification gaps. Do not
claim full native qualification merely because this machine lacks a capability.

## Explicit semantic dispositions

Queries, pure content transfers/clears/draws, synchronization, and sampler parameters with no represented
wrapper fields do not become metadata refreshes. Sparse page commitment changes residency rather than
allocation dimensions. ImportMemory/ImportSemaphore establish objects used by later storage commands;
there is no represented import descriptor to refresh today. Vertex current-value setters affect context
values rather than VAO array layouts. Buffer/texture selection alone changes binding knowledge; element
buffer and vertex-buffer association are the exceptions handled by V1. Creation registers identity;
it must not create a second owner or confuse a generated name with initialized storage.

Deletion-side dependency changes must not escape through the content-only dispositions. Window-system context/surface replacement remains on the
existing context registry/surface publication boundary. GL API coverage does not claim interception of
unrelated WGL/GLX/native interop code that bypasses the established boundary.

Semantic references consulted: [EXT direct state access](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_direct_state_access.txt),
[OpenGL 3.1 mipmap generation](https://registry.khronos.org/OpenGL/specs/gl/glspec31.pdf),
and [OpenGL 4.3 vertex binding model](https://registry.khronos.org/OpenGL/specs/gl/glspec43.core.pdf).
Individual extension definitions in the registry control capability/ABI/behavior for their variants.



## Expanded command names

### B1 buffer storage

Controlled storage publishes capacity/usage directly. Exact external storage commands withdraw the
selected existing snapshot before dispatch and retain a pending storage effect. They perform no
completion read; explicit manager acquisition at use recovers capacity/usage and propagates dependent
texture revisions, including equal-sized replacement. The original entry remains guarded through
native dispatch and is withdrawn at completion; reentrant acquisition cannot publish an intermediate
descriptor. Interrupted calls remain unavailable.

`glBufferData`, `glBufferDataARB`, `glBufferStorage`, `glBufferStorageExternalEXT`, `glBufferStorageMemEXT`, `glNamedBufferData`, `glNamedBufferDataEXT`, `glNamedBufferStorage`, `glNamedBufferStorageEXT`, `glNamedBufferStorageExternalEXT`, `glNamedBufferStorageMemEXT`.

Storage semantics are controlled by [ARB_buffer_storage](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_buffer_storage.txt),
[EXT_external_buffer](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_external_buffer.txt) and
[EXT_external_objects](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_external_objects.txt)
for imported memory. External allocation defines the same represented capacity/usage facts without
transferring ownership of imported memory. Rejected immutable reallocation retains actual native
storage; observation must not consume the pending GL error or publish the requested capacity.

### F1 framebuffer attachments

Attachment/default adapters execute through StateCache and recover the affected slot/default after
exact native dispatch, preserving known unaffected descriptors. Initial unknown FBOs retain full discovery.
Managed attachment operations publish command-established selections without descriptor queries. Named attachment
queries preserve DSA binding behavior; fallback queries preserve separate read/draw bindings.
[ARB_framebuffer_no_attachments](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_framebuffer_no_attachments.txt)
controls default-parameter query availability, and
[OVR_multiview](https://registry.khronos.org/OpenGL/extensions/OVR/OVR_multiview.txt)
defines the canonical base-view/count fields.
The native probe in `artifacts/gpu-resource-phase4/framebuffer-view-defaults-expanded-probe.*`
shows this driver also returns positive OVR counts for ordinary 2D, single-layer and whole-array
attachments. A positive count cannot establish multiview command provenance. Externally acquired descriptors
retain queried values and a separate mode established by admitted observed commands. Controlled
selections instead retain their supplied ordinary or multiview mode and range.
Known array allocation and legal slot/layer/level certify ordinary intercepted command eligibility;
the resulting queried descriptor must also match. Ordinary intercepted whole-array/layer commands and managed multiview operations
therefore remain distinct even when queries coincide. Rejected commands preserve prior evidence only
when their descriptor is unchanged. Manager-owned transient command evidence is consumed by canonical
publication, including explicitly composed managed attachment updates; no second framebuffer registry is created.
Direct VGE multiview operations retain selected range projection and named or bound OVR replay.
External GL.Ovr calls are not intercepted; later mutations through them cannot keep registered snapshots current.
Initial external acquisition cannot establish an unqueryable mode; its range can be inspected, but
ambiguous array helpers reject reattachment until a supported explicit selection establishes semantics.
External draw-route adapters invalidate the existing routing cache and defer actual capture until use.
Read routing has no cached mirror. Read/draw routes cannot be queried through named framebuffer
parameter queries ([ARB_direct_state_access, issue 41](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_direct_state_access.txt));
lazy capture avoids adding binds to named routing commands. Native range/replay and regression receipts
are retained in `artifacts/gpu-resource-phase4/multiview-mode-repair-focused.trx` and
`multiview-mode-repair-combined.trx`; these exercise named and binding-preserving fallback paths.

`glFramebufferDrawBufferEXT`, `glFramebufferDrawBuffersEXT`, `glFramebufferParameteri`, `glFramebufferReadBufferEXT`, `glFramebufferRenderbuffer`, `glFramebufferRenderbufferEXT`, `glFramebufferTexture`, `glFramebufferTexture1D`, `glFramebufferTexture1DEXT`, `glFramebufferTexture2D`, `glFramebufferTexture2DEXT`, `glFramebufferTexture3D`, `glFramebufferTexture3DEXT`, `glFramebufferTextureARB`, `glFramebufferTextureEXT`, `glFramebufferTextureFaceARB`, `glFramebufferTextureFaceEXT`, `glFramebufferTextureLayer`, `glFramebufferTextureLayerARB`, `glFramebufferTextureLayerEXT`, `glNamedFramebufferDrawBuffer`, `glNamedFramebufferDrawBuffers`, `glNamedFramebufferParameteri`, `glNamedFramebufferParameteriEXT`, `glNamedFramebufferReadBuffer`, `glNamedFramebufferRenderbuffer`, `glNamedFramebufferRenderbufferEXT`, `glNamedFramebufferTexture`, `glNamedFramebufferTexture1DEXT`, `glNamedFramebufferTexture2DEXT`, `glNamedFramebufferTexture3DEXT`, `glNamedFramebufferTextureEXT`, `glNamedFramebufferTextureFaceEXT`, `glNamedFramebufferTextureLayer`, `glNamedFramebufferTextureLayerEXT`.

### I1 external storage and residency

`glBufferPageCommitmentARB`, `glNamedBufferPageCommitmentARB`, `glNamedBufferPageCommitmentEXT`, `glTexPageCommitmentARB`, `glTexturePageCommitmentEXT`.

### L1 resource lifetime

`glDeleteBuffers`, `glDeleteBuffersARB`, `glDeleteFramebuffers`, `glDeleteFramebuffersEXT`, `glDeleteMemoryObjectsEXT`, `glDeleteObjectARB`, `glDeleteProgram`, `glDeleteProgramPipelines`, `glDeleteProgramPipelinesEXT`, `glDeleteQueries`, `glDeleteQueriesARB`, `glDeleteRenderbuffers`, `glDeleteRenderbuffersEXT`, `glDeleteSamplers`, `glDeleteSemaphoresEXT`, `glDeleteShader`, `glDeleteSync`, `glDeleteTextures`, `glDeleteTexturesEXT`, `glDeleteTransformFeedbacks`, `glDeleteVertexArrays`.

### P1 executable interface

Exact OpenTK link, program-binary, interface-block and signed integer unit setters use common
StateCache execution. Available unit/block snapshots update only affected values; managed assignments
publish supplied values and external assignments use narrow getters. Link/binary results retain executable
reflection at completion. Unit setters classify represented sampler/image locations before reading
metadata; ordinary numeric uniforms do not refresh it. Bound setters resolve the active program,
including the active program of a selected pipeline. Selector-only commands retain binding knowledge
separately from resource revisions. Canonical snapshots carry resource properties, block stage flags
and atomic-counter buffer descriptors required by fixed-slot preparation.

Manager publication reconciles legacy layout caches and revalidates prepared resource tables from
canonical facts. Native executable changes withdraw compiled preparation until the owner's existing
preparation workflow validates a replacement; later unit/block updates cannot revive that old table.
Temporary programs and shader stages retain their common resource owners through deferred completion,
rejection and transfer. Initial program registration uses the guaranteed empty executable of a fresh
native object; completion inspection stays at the existing dependency boundary.

Observed bindless assignments at represented locations withdraw unit metadata for the original
registration. Ordinary readback cannot establish whether a value represents a unit or an opaque handle:
[ARB_bindless_texture](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_bindless_texture.txt)
specifies conversion of the most recently assigned value. Replacing the native program through its
owner's preparation workflow establishes a fresh supported unit representation. Subsequent reads or
link attempts on that same allocation do not clear this conservative withdrawal. Actual ARB bound/named
sampler and image handle assignments are covered by the retained native tests; these assign/read handles
without claiming resident-handle sampling or rendering qualification.

VGE's terrain sampler/block hooks use the same StateCache route. Authored unit-setting helpers can skip
a stable assignment certified by available canonical unit facts, without a duplicate uniform cache.
Exact external adapters still forward each native call unchanged. ProgramUniform is selected on
core/ARB separate-shader-object support independently of DSA. Numeric scalar/vector/matrix data use
the existing packed-uniform storage model and do not introduce texture-unit metadata work. Program
pipeline creation selects named creation or restoring fallback realization; explicit-ID stage/active-program
configuration does not bind the target pipeline unnecessarily.

Desktop EXT separate-shader-object selectors also invalidate current-program knowledge. ActiveProgramEXT
selects the bound uniform destination; UseShaderProgramEXT can change stage selection while leaving
CURRENT_PROGRAM unchanged. A subsequent managed UseProgram must therefore restore the full program
selection instead of suppressing the call based on that unchanged query alone. Query recovery establishes
the uniform destination only; independent binding knowledge certifies a completed full-program installation.

Selector spellings include `glUseProgram`, `glUseProgramObjectARB`, `glBindProgramPipeline`,
`glBindProgramPipelineEXT`, `glActiveProgramEXT` and `glUseShaderProgramEXT`.

`glAttachObjectARB`, `glAttachShader`, `glBindAttribLocation`, `glBindAttribLocationARB`, `glBindFragDataLocation`, `glBindFragDataLocationEXT`, `glBindFragDataLocationIndexed`, `glCreateShaderProgramEXT`, `glCreateShaderProgramv`, `glCreateShaderProgramvEXT`, `glDetachObjectARB`, `glDetachShader`, `glLinkProgram`, `glLinkProgramARB`, `glProgramBinary`, `glProgramParameteri`, `glProgramParameteriARB`, `glProgramParameteriEXT`, `glShaderStorageBlockBinding`, `glTransformFeedbackVaryings`, `glTransformFeedbackVaryingsEXT`, `glUniformBlockBinding`.

Retained sampler/image unit facts also require `glUniform1i`, `glUniform1iARB`, `glUniform1iv`,
`glUniform1ivARB`, `glProgramUniform1i`, `glProgramUniform1iEXT`, `glProgramUniform1iv`,
`glProgramUniform1ivEXT`. Classify their destination location; numeric uniform writes add no metadata work.
The unsupported bindless equivalent must make unit facts unavailable: `glUniformHandleui64ARB`,
`glUniformHandleui64vARB`, `glProgramUniformHandleui64ARB`, `glProgramUniformHandleui64vARB`.
All spellings are present in the retained registry snapshot.

### R1 renderbuffer storage

Core named creation/storage/query behavior follows
[ARB_direct_state_access](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_direct_state_access.txt).
Named storage requires an initialized object; the direct creation path uses CreateRenderbuffers,
while the bound fallback realizes generated names through binding. Controlled sample requests use
cached supported-count normalization; only the proposal's fallback requires allocation-time sample readback.
The EXT coverage-sample overload remains supported even though the feature has vendor origins.
External unknown storage acquires RENDERBUFFER_SAMPLES; controlled storage uses its resolved supported
count or the narrowly justified fallback result. No unconditional descriptor readback is required.

`glNamedRenderbufferStorage`, `glNamedRenderbufferStorageEXT`, `glNamedRenderbufferStorageMultisample`, `glNamedRenderbufferStorageMultisampleCoverageEXT`, `glNamedRenderbufferStorageMultisampleEXT`, `glRenderbufferStorage`, `glRenderbufferStorageEXT`, `glRenderbufferStorageMultisample`, `glRenderbufferStorageMultisampleEXT`.

### T1 image definitions

Controlled allocation derives complete images from validated initialization inputs.
Multisample allocation uses cached target/format counts or the approved one-time sample-count-only
fallback when those capabilities are unavailable. Other dimensions and format remain input-derived.
Exact external overloads execute in StateCache; a known single-image definition recovers only its affected level/face.
Whole-storage/import commands and unavailable original descriptors retain external acquisition.
Interrupted native calls leave snapshots unavailable without performing completion queries.

`glCompressedMultiTexImage1DEXT`, `glCompressedMultiTexImage2DEXT`, `glCompressedMultiTexImage3DEXT`, `glCompressedTexImage1D`, `glCompressedTexImage1DARB`, `glCompressedTexImage2D`, `glCompressedTexImage2DARB`, `glCompressedTexImage3D`, `glCompressedTexImage3DARB`, `glCompressedTextureImage1DEXT`, `glCompressedTextureImage2DEXT`, `glCompressedTextureImage3DEXT`, `glCopyMultiTexImage1DEXT`, `glCopyMultiTexImage2DEXT`, `glCopyTexImage1D`, `glCopyTexImage1DEXT`, `glCopyTexImage2D`, `glCopyTexImage2DEXT`, `glCopyTextureImage1DEXT`, `glCopyTextureImage2DEXT`, `glEGLImageTargetTexStorageEXT`, `glEGLImageTargetTextureStorageEXT`, `glMultiTexImage1DEXT`, `glMultiTexImage2DEXT`, `glMultiTexImage3DEXT`, `glTexImage1D`, `glTexImage2D`, `glTexImage2DMultisample`, `glTexImage3D`, `glTexImage3DEXT`, `glTexImage3DMultisample`, `glTexStorage1D`, `glTexStorage1DEXT`, `glTexStorage2D`, `glTexStorage2DEXT`, `glTexStorage2DMultisample`, `glTexStorage3D`, `glTexStorage3DEXT`, `glTexStorage3DMultisample`, `glTexStorageMem1DEXT`, `glTexStorageMem2DEXT`, `glTexStorageMem2DMultisampleEXT`, `glTexStorageMem3DEXT`, `glTexStorageMem3DMultisampleEXT`, `glTextureImage1DEXT`, `glTextureImage2DEXT`, `glTextureImage3DEXT`, `glTextureStorage1D`, `glTextureStorage1DEXT`, `glTextureStorage2D`, `glTextureStorage2DEXT`, `glTextureStorage2DMultisample`, `glTextureStorage2DMultisampleEXT`, `glTextureStorage3D`, `glTextureStorage3DEXT`, `glTextureStorage3DMultisample`, `glTextureStorage3DMultisampleEXT`, `glTextureStorageMem1DEXT`, `glTextureStorageMem2DEXT`, `glTextureStorageMem2DMultisampleEXT`, `glTextureStorageMem3DEXT`, `glTextureStorageMem3DMultisampleEXT`.

### T2 mip generation

Immutable generation has no descriptor work. Known mutable storage checks only descendants of its
selected base through the generated maximum level; existing image descriptors determine that range.
Unchanged descriptors preserve storage revisions and view certificates. Unknown storage uses recovery.

`glGenerateMipmap`, `glGenerateMipmapEXT`, `glGenerateMultiTexMipmapEXT`, `glGenerateTextureMipmap`, `glGenerateTextureMipmapEXT`.

### T3 texture associations

[ARB_texture_buffer_range](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_buffer_range.txt)
and [OpenGL 4.6 section 8.9](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf)
define native association/range queries and rejected-command behavior. Preserve the managed
whole-buffer convention without inferring it solely from a range equal to the backing buffer size.
Range acquisition must account for the native-width descriptor and the narrower texture query
results; truncated or rounded query values cannot certify requested offsets or sizes.
The association observer preserves native-width command arguments. It verifies the resulting backing
and format, uses native buffer capacity and cached offset alignment to establish range admission,
and cross-checks exactly representable offsets/sizes through native range queries. A throwing native
dispatch leaves metadata unavailable. Whole-buffer mode follows the accepted command, because
queries cannot distinguish whole-buffer binding from an explicit range of the same initial size.
OpenGL 4.6 section 8.18 defines view-relative selection and the source-derived immutable level count.
External view discovery reads the exposed view level count before enumerating images; controlled
view creation uses its explicit range and known source metadata. The source allocation's level count
must not widen the view's canonical image descriptors.

The EXT direct-state-access renderbuffer texture commands retain their coverage obligation even
though the underlying target is defined by NV_explicit_multisample. No NV command adapters are added.
The [target specification](https://registry.khronos.org/OpenGL/extensions/NV/NV_explicit_multisample.txt)
defines no texture levels or texture parameter queries for this target. Canonical acquisition retains
the queried renderbuffer association and an empty level array; dimensions, format and samples belong
to the backing renderbuffer's canonical entry. Manager publication admits that dependency before
initial texture availability, checks backing availability, propagates backing storage revisions and
withdraws dependent availability after deletion. This does not add a renderbuffer sampler abstraction
or fabricate framebuffer-attachable mip images. Native EXT named/explicit-unit association and backing
dependency tests exercise this target on supporting hardware; ordinary target discovery cannot assume
unsupported parameter queries are legal.

Manager-owned weak-key dependencies retain each original backing registration. Ordinary readback
cannot reconnect a retained deleted backing name to a replacement allocation with the same numeric
ID. Certified explicit association commands may renew that relationship; buffer commands certify their
native ranges, while EXT renderbuffer association renewal requires the established target and a live
tracked backing. No resource key gains an incarnation or generation field.

`glMultiTexBufferEXT`, `glMultiTexRenderbufferEXT`, `glTexBuffer`, `glTexBufferARB`, `glTexBufferEXT`, `glTexBufferRange`, `glTextureBuffer`, `glTextureBufferEXT`, `glTextureBufferRange`, `glTextureBufferRangeEXT`, `glTextureRenderbufferEXT`, `glTextureView`.

### T4 texture parameters

Controlled base/max changes publish the supplied value. External setters read only the changed
represented parameter when other facts are available; they never rescan known image storage.
Unrepresented parameters and invalid multisample mip parameters have no metadata observation work.

`glMultiTexParameterfEXT`, `glMultiTexParameterfvEXT`, `glMultiTexParameteriEXT`, `glMultiTexParameterIivEXT`, `glMultiTexParameterIuivEXT`, `glMultiTexParameterivEXT`, `glTexParameterf`, `glTexParameterfv`, `glTexParameteri`, `glTexParameterIiv`, `glTexParameterIivEXT`, `glTexParameterIuiv`, `glTexParameterIuivEXT`, `glTexParameteriv`, `glTextureParameterf`, `glTextureParameterfEXT`, `glTextureParameterfv`, `glTextureParameterfvEXT`, `glTextureParameteri`, `glTextureParameteriEXT`, `glTextureParameterIiv`, `glTextureParameterIivEXT`, `glTextureParameterIuiv`, `glTextureParameterIuivEXT`, `glTextureParameteriv`, `glTextureParameterivEXT`.

### V1 vertex configuration

Exact OpenTK vertex-configuration adapters advance the existing VAO metadata revision without
introducing a layout mirror. `BindBuffer` has a conditional effect: element-array targets revise the
current VAO; other targets only invalidate affected binding knowledge. Wrapper configuration uses
the same StateCache observation path, with capability-selected named calls and bound fallbacks.
`GpuVertexAttribBinding` retains its owning wrapper and checks the manager-owned revision. Named
binding queries recover selected facts after changes; fallback authored binding descriptions reject
stale reuse. Helper commands update that helper value's revision, while independently retained copies
remain subject to revalidation. Native selector, partial multi-bind and helper revalidation receipts are
retained in `artifacts/gpu-resource-phase4/vertex-selection-focused.trx`,
`vertex-buffer-bindings-focused.trx` and `vertex-helper-focused.trx`. API signature inspection establishes
mapping coverage separately from the native operations exercised on available hardware.

`glBindBuffer`, `glBindBufferARB`, `glBindVertexBuffer`, `glBindVertexBuffers`, `glDisableVertexArrayAttrib`, `glDisableVertexArrayAttribEXT`, `glDisableVertexAttribArray`, `glDisableVertexAttribArrayARB`, `glEnableVertexArrayAttrib`, `glEnableVertexArrayAttribEXT`, `glEnableVertexAttribArray`, `glEnableVertexAttribArrayARB`, `glVertexArrayAttribBinding`, `glVertexArrayAttribFormat`, `glVertexArrayAttribIFormat`, `glVertexArrayAttribLFormat`, `glVertexArrayBindingDivisor`, `glVertexArrayBindVertexBufferEXT`, `glVertexArrayElementBuffer`, `glVertexArrayVertexAttribBindingEXT`, `glVertexArrayVertexAttribDivisorEXT`, `glVertexArrayVertexAttribFormatEXT`, `glVertexArrayVertexAttribIFormatEXT`, `glVertexArrayVertexAttribIOffsetEXT`, `glVertexArrayVertexAttribLFormatEXT`, `glVertexArrayVertexAttribLOffsetEXT`, `glVertexArrayVertexAttribOffsetEXT`, `glVertexArrayVertexBindingDivisorEXT`, `glVertexArrayVertexBuffer`, `glVertexArrayVertexBuffers`, `glVertexAttribBinding`, `glVertexAttribDivisor`, `glVertexAttribDivisorARB`, `glVertexAttribFormat`, `glVertexAttribIFormat`, `glVertexAttribIPointer`, `glVertexAttribIPointerEXT`, `glVertexAttribLFormat`, `glVertexAttribLPointer`, `glVertexAttribLPointerEXT`, `glVertexAttribPointer`, `glVertexAttribPointerARB`, `glVertexBindingDivisor`.
