# OpenGL resource metadata API coverage

This is the API-derived planning matrix for [the proposal](Rendering.GpuResourceHandles.Proposal.md).
Current callers do not define or limit coverage within the supported API scope. Every command in a family, every supported desktop extension
variant and every managed overload has the implementation and verification obligation specified below.

## Explicit API exclusions

APPLE and NV extension APIs are outside the supported interception and metadata-tracking scope.
Do not add adapters, discovery mappings or verification obligations for those extension groups.
Core operations remain required even when similar functionality also exists in an excluded extension.
All other applicable extension groups retain their existing coverage obligations.

## Authority and expansion rules

Use the [Khronos OpenGL registry](https://registry.khronos.org/OpenGL/) and
[gl.xml](https://github.com/KhronosGroup/OpenGL-Registry/blob/main/xml/gl.xml), including desktop GL
core/compatibility features and extensions whose supported API includes `gl` or `glcore`. ES-only APIs
are a separate API; a desktop extension is included even if its name has an OES suffix, subject to the explicit APPLE/NV exclusions above. The retained
registry snapshot has SHA256 `B9CA2CFA5C676E901C20D34AF3407F1687CDE0F1336A5FF7A8974D04C7494AD3`.
The [expanded matrix](../artifacts/gpu-resource-handles-baseline/api-command-matrix.csv) preserves native
signatures from the retained baseline expansion; [the expansion script](../artifacts/gpu-resource-handles-baseline/classify-api.ps1) and
[registry snapshot](../artifacts/gpu-resource-handles-baseline/gl.xml) make that enumeration reproducible.
These are API inspection artifacts, not reflection tests or a whitelist of callers. The retained baseline
artifacts enumerate a broader API set; apply the explicit exclusions before interpreting support obligations. The expanded names
below also include subsequent field-to-command reconciliation; retain the baseline artifacts unchanged.

The family rules are authoritative over the helper's name matching. Review semantics for aliases and
indirect effects; a missing spelling must be added, never inferred to be out of scope. Future registry
additions require the same classification. The command list below records the current expansion.

Implementation means a StateCache route that executes the native command, identifies affected object
registrations, and forwards the actual effect to GpuResourceManager to update or invalidate canonical metadata. Bound operations
resolve selectors; DSA uses explicit names; multi-texture operations use explicit units; multi-bind and
list operations process all affected objects. Proxy texture targets alter proxy state, not a texture
object, and must not notify a bound object. Preserve errors, partial success, and existing binding effects.

Every OpenTK representation of each command is required: signed/unsigned IDs, pointer/IntPtr, ref/out,
array and generic overloads, and nested extension classes. Normalize to one semantic handler without
double notification. Extend exact adapter mapping and MethodSpecification discovery for generic calls;
no fixed set of observed element types is the coverage boundary. Missing binding entry points need a
capability-gated native delegate with the specification's ABI. Capability absence blocks native execution,
not adapter implementation. Existing assembly interception boundaries remain explicit; arbitrary native
code bypassing the StateCache is not silently tracked.

## Canonical destinations and publication route

The [canonical schema mapping](Rendering.GpuResourceHandles.Inventory.md#canonical-metadata-schemas-and-field-migration)
is the field-level destination for this matrix. T1–T5 target GpuTextureMetadata (including buffer/view
associations); B1 targets GpuBufferMetadata; R1 targets GpuRenderbufferMetadata; F1 targets framebuffer
attachment/default metadata while routing remains in StateCache. V1 invalidates VAO entry revision and
helper assumptions; it does not introduce a full layout mirror. P1 refreshes native program facts and
invalidates derived preparation. L1 retires entries/aliases and reconciles affected dependencies. C1/I1
apply those same destinations only where their execution has a represented metadata effect.

Every route is native command -> StateCache observation -> context's GpuResourceManager -> canonical
struct update -> typed wrapper snapshots -> existing dependent validation/publication. Read canonical
facts once per change, not once per wrapper. Texture and renderbuffer changes also project selected
GpuImageMetadata to associated framebuffer images. Each kind has one canonical struct even when several
wrapper classes expose different aspects. No borrowed registry, StateCache metadata registry, abstract
per-wrapper query handler or generation-based resource key is introduced.

All previously enumerated API families/variants remain in scope. Moving fields to canonical structs
does not narrow API coverage. Empty per-kind structs and owner-only fields retain the explicit semantic
dispositions below. Initial fixed shader type acquisition requires no new mutable shader-storage family.
The metadata revision used to invalidate stale assumptions is registration bookkeeping, not GPU contents.

## Families, implementation and verification

| ID | Metadata / API family | Implementation obligation | Verification obligation |
| --- | --- | --- | --- |
| T1 | Image definitions: TexImage 1D/2D/3D/4D, compressed image definitions, CopyTexImage, immutable/multisample storage, DSA and EXT multi-texture equivalents, external-memory and sparse allocation | Refresh affected dimensions, format, samples, level allocation and immutable-storage facts. Copy definitions change storage even though their source is pixels. Same object retains identity. Unsupported typed dimensionality must become explicitly unavailable, never falsely represented by a 2D/3D snapshot. | Allocate/redefine every supported target/dimension, nonzero mip and cube face; rejected allocation retains actual facts; cover pointer/array variants, DSA, explicit unit and extension aliases. |
| T2 | GenerateMipmap, GenerateTextureMipmap, GenerateMultiTexMipmap and aliases | Refresh the generated mip range; preserve base/max-level semantics. | Missing levels become allocated; same registration/revision change; base level other than zero; failed/incomplete cube generation. |
| T3 | Texture buffer whole/range attachment, texture views and renderbuffer-backed textures | Refresh source association, format, range and selected levels/layers; a view has its own native identity. Preserve whole-buffer sentinel conventions and backing dependencies. | Change backing/range; delete or resize backing; view source identity versus view identity; reject invalid ranges without assumed success. |
| T4 | All texture parameter setters, bound/DSA/multi-texture and integer/float/vector forms | Classify pname effect. Refresh represented facts if affected; retain owner-only policy distinctions. Track legacy automatic-mipmap enablement and parameters used to resolve affected levels. Do not invent wrapper metadata for unrepresented parameters. | Parameter-family coverage and pname-dependent effects; automatic-mipmap/base/max-level cases; unchanged storage does not spuriously replace identity. |
| T5 | Subimage/copy-subimage/compressed-subimage variants under compatibility automatic mip generation | Ordinary texel writes are content. When legacy GENERATE_MIPMAP is enabled, the resulting generated level allocation is metadata and must receive T2 handling. | With automatic generation off, no metadata work; with it on, refreshed derived level storage. Preserve producer content certificates independently. |
| B1 | BufferData, BufferStorage, named variants, external/memory-backed storage | Refresh actual capacity/usage and represented storage facts; preserve mapping ownership and authored EBO interpretation. | Bound/DSA capacity changes, zero size, immutable rejection, all overload forms and external-memory capability cases. |
| R1 | RenderbufferStorage, named storage, multisample/coverage/advanced-sample aliases | Refresh actual dimensions, format and sample values, propagate to dependent attachments. | Bound/DSA allocation, rounded sample counts, rejection and alias refresh. |
| F1 | Framebuffer texture/renderbuffer attachment, face/layer/multiview variants, default-parameter and draw/read-routing variants | Refresh attachment identity/subresource and derived target metadata; preserve ownership. Route draw/read-buffer commands through the existing routing cache. Classify default-FBO parameters separately from window-system surface publication. | All attachment forms, detach via zero, split read/draw targets, partial configurations and affected publication revisions. |
| V1 | Vertex attribute pointer/format/binding/divisor/enables, vertex-buffer multi-bind, element-buffer binding, compatibility array configuration | Revise represented VAO configuration and stale helper assumptions. Resolve current VAO and latched buffer dependencies. Compatibility-only attributes absent from wrappers have an explicit no-represented-field effect, not an omitted adapter. | Bound/DSA, multi-bind partial failure, generic/integer/double formats, enablement/divisor, EBO association, stale helper use and unrelated VAOs. |
| P1 | Link/binary installation, shader-program creation, interface block bindings, sampler/image unit assignments and link-input configuration | Invalidate reflected executable layout on actual relink/binary/interface changes; use existing preparation flow. Uniform1i/1iv and ProgramUniform1i/1iv affect retained native unit facts only at sampler/image locations. Bindless-handle assignment at those locations makes the unsupported unit representation unavailable. Ordinary numeric uniforms remain content. Attach/detach/bind-location/transform-feedback declarations affect the next link rather than inventing an immediate new executable. | Successful/failed relink, binary replacement, block/unit-binding changes, sampler/image array locations, ordinary numeric writes excluded, bindless unavailability, deferred link-input effects, prepared submission revalidation. |
| L1 | Deletion of every represented kind, all aliases and array/ref/pointer forms | Retire the captured registration; preserve deferred native destruction and resource-specific unbinding. ARB generic object deletion resolves shader/program kind. Propagate affected attachment/VAO metadata. | Each kind, lists and duplicate/zero names, queued old identity, same-name replacement, aliases and retained native references. |
| C1 | Display-list/command-list execution and compatibility state restoration | Account for metadata commands executed indirectly. Compilation alone must not publish execution effects. At execution, reconcile potentially affected represented metadata when exact effects are unavailable; preserve binding restoration. | Compile versus execute, repeated list execution, restored parameter/client-array state, changed metadata observed before dependent use. |
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

Deletion-side dependency changes, compatibility PopAttrib/PopClientAttrib and display-list execution
must not escape through these exclusions. Window-system context/surface replacement remains on the
existing context registry/surface publication boundary. GL API coverage does not claim interception of
unrelated WGL/GLX/native interop code that bypasses the established boundary.

Semantic references consulted: [EXT direct state access](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_direct_state_access.txt),
[OpenGL 3.1 mipmap generation](https://registry.khronos.org/OpenGL/specs/gl/glspec31.pdf),
[OpenGL 1.4 automatic mip generation](https://registry.khronos.org/OpenGL/specs/gl/glspec14.pdf),
and [OpenGL 4.3 vertex binding model](https://registry.khronos.org/OpenGL/specs/gl/glspec43.core.pdf).
Individual extension definitions in the registry control capability/ABI/behavior for their variants.



## Expanded command names

### B1 buffer storage

`glBufferData`, `glBufferDataARB`, `glBufferStorage`, `glBufferStorageExternalEXT`, `glBufferStorageMemEXT`, `glNamedBufferData`, `glNamedBufferDataEXT`, `glNamedBufferStorage`, `glNamedBufferStorageEXT`, `glNamedBufferStorageExternalEXT`, `glNamedBufferStorageMemEXT`.

### C1 indirect compatibility mutation

`glCallList`, `glCallLists`, `glClientAttribDefaultEXT`, `glPopAttrib`, `glPopClientAttrib`, `glPushClientAttribDefaultEXT`.

### F1 framebuffer attachments

`glFramebufferDrawBufferEXT`, `glFramebufferDrawBuffersEXT`, `glFramebufferParameteri`, `glFramebufferParameteriMESA`, `glFramebufferReadBufferEXT`, `glFramebufferRenderbuffer`, `glFramebufferRenderbufferEXT`, `glFramebufferTexture`, `glFramebufferTexture1D`, `glFramebufferTexture1DEXT`, `glFramebufferTexture2D`, `glFramebufferTexture2DEXT`, `glFramebufferTexture3D`, `glFramebufferTexture3DEXT`, `glFramebufferTextureARB`, `glFramebufferTextureEXT`, `glFramebufferTextureFaceARB`, `glFramebufferTextureFaceEXT`, `glFramebufferTextureLayer`, `glFramebufferTextureLayerARB`, `glFramebufferTextureLayerEXT`, `glFramebufferTextureMultiviewOVR`, `glNamedFramebufferDrawBuffer`, `glNamedFramebufferDrawBuffers`, `glNamedFramebufferParameteri`, `glNamedFramebufferParameteriEXT`, `glNamedFramebufferReadBuffer`, `glNamedFramebufferRenderbuffer`, `glNamedFramebufferRenderbufferEXT`, `glNamedFramebufferTexture`, `glNamedFramebufferTexture1DEXT`, `glNamedFramebufferTexture2DEXT`, `glNamedFramebufferTexture3DEXT`, `glNamedFramebufferTextureEXT`, `glNamedFramebufferTextureFaceEXT`, `glNamedFramebufferTextureLayer`, `glNamedFramebufferTextureLayerEXT`, `glNamedFramebufferTextureMultiviewOVR`.

### I1 external storage and residency

`glBufferPageCommitmentARB`, `glNamedBufferPageCommitmentARB`, `glNamedBufferPageCommitmentEXT`, `glTexPageCommitmentARB`, `glTexturePageCommitmentEXT`.

### L1 resource lifetime

`glDeleteBuffers`, `glDeleteBuffersARB`, `glDeleteFramebuffers`, `glDeleteFramebuffersEXT`, `glDeleteMemoryObjectsEXT`, `glDeleteObjectARB`, `glDeleteProgram`, `glDeleteProgramPipelines`, `glDeleteProgramPipelinesEXT`, `glDeleteQueries`, `glDeleteQueriesARB`, `glDeleteRenderbuffers`, `glDeleteRenderbuffersEXT`, `glDeleteSamplers`, `glDeleteSemaphoresEXT`, `glDeleteShader`, `glDeleteSync`, `glDeleteTextures`, `glDeleteTexturesEXT`, `glDeleteTransformFeedbacks`, `glDeleteVertexArrays`.

### P1 executable interface

`glAttachObjectARB`, `glAttachShader`, `glBindAttribLocation`, `glBindAttribLocationARB`, `glBindFragDataLocation`, `glBindFragDataLocationEXT`, `glBindFragDataLocationIndexed`, `glCreateShaderProgramEXT`, `glCreateShaderProgramv`, `glCreateShaderProgramvEXT`, `glDetachObjectARB`, `glDetachShader`, `glLinkProgram`, `glLinkProgramARB`, `glProgramBinary`, `glProgramParameteri`, `glProgramParameteriARB`, `glProgramParameteriEXT`, `glShaderStorageBlockBinding`, `glTransformFeedbackVaryings`, `glTransformFeedbackVaryingsEXT`, `glUniformBlockBinding`.

Retained sampler/image unit facts also require `glUniform1i`, `glUniform1iARB`, `glUniform1iv`,
`glUniform1ivARB`, `glProgramUniform1i`, `glProgramUniform1iEXT`, `glProgramUniform1iv`,
`glProgramUniform1ivEXT`. Classify their destination location; numeric uniform writes add no metadata work.
The unsupported bindless equivalent must make unit facts unavailable: `glUniformHandleui64ARB`,
`glUniformHandleui64vARB`, `glProgramUniformHandleui64ARB`, `glProgramUniformHandleui64vARB`,
,
. All spellings are present in the retained registry snapshot.

### R1 renderbuffer storage

`glNamedRenderbufferStorage`, `glNamedRenderbufferStorageEXT`, `glNamedRenderbufferStorageMultisample`, `glNamedRenderbufferStorageMultisampleAdvancedAMD`, `glNamedRenderbufferStorageMultisampleCoverageEXT`, `glNamedRenderbufferStorageMultisampleEXT`, `glRenderbufferStorage`, `glRenderbufferStorageEXT`, `glRenderbufferStorageMultisample`, `glRenderbufferStorageMultisampleAdvancedAMD`, `glRenderbufferStorageMultisampleEXT`.

### T1 image definitions

`glCompressedMultiTexImage1DEXT`, `glCompressedMultiTexImage2DEXT`, `glCompressedMultiTexImage3DEXT`, `glCompressedTexImage1D`, `glCompressedTexImage1DARB`, `glCompressedTexImage2D`, `glCompressedTexImage2DARB`, `glCompressedTexImage3D`, `glCompressedTexImage3DARB`, `glCompressedTextureImage1DEXT`, `glCompressedTextureImage2DEXT`, `glCompressedTextureImage3DEXT`, `glCopyMultiTexImage1DEXT`, `glCopyMultiTexImage2DEXT`, `glCopyTexImage1D`, `glCopyTexImage1DEXT`, `glCopyTexImage2D`, `glCopyTexImage2DEXT`, `glCopyTextureImage1DEXT`, `glCopyTextureImage2DEXT`, `glEGLImageTargetTexStorageEXT`, `glEGLImageTargetTextureStorageEXT`, `glMultiTexImage1DEXT`, `glMultiTexImage2DEXT`, `glMultiTexImage3DEXT`, `glTexImage1D`, `glTexImage2D`, `glTexImage2DMultisample`, `glTexImage3D`, `glTexImage3DEXT`, `glTexImage3DMultisample`, `glTexImage4DSGIS`, `glTexStorage1D`, `glTexStorage1DEXT`, `glTexStorage2D`, `glTexStorage2DEXT`, `glTexStorage2DMultisample`, `glTexStorage3D`, `glTexStorage3DEXT`, `glTexStorage3DMultisample`, `glTexStorageMem1DEXT`, `glTexStorageMem2DEXT`, `glTexStorageMem2DMultisampleEXT`, `glTexStorageMem3DEXT`, `glTexStorageMem3DMultisampleEXT`, `glTexStorageSparseAMD`, `glTextureImage1DEXT`, `glTextureImage2DEXT`, `glTextureImage3DEXT`, `glTextureStorage1D`, `glTextureStorage1DEXT`, `glTextureStorage2D`, `glTextureStorage2DEXT`, `glTextureStorage2DMultisample`, `glTextureStorage2DMultisampleEXT`, `glTextureStorage3D`, `glTextureStorage3DEXT`, `glTextureStorage3DMultisample`, `glTextureStorage3DMultisampleEXT`, `glTextureStorageMem1DEXT`, `glTextureStorageMem2DEXT`, `glTextureStorageMem2DMultisampleEXT`, `glTextureStorageMem3DEXT`, `glTextureStorageMem3DMultisampleEXT`, `glTextureStorageSparseAMD`.

### T2 mip generation

`glGenerateMipmap`, `glGenerateMipmapEXT`, `glGenerateMultiTexMipmapEXT`, `glGenerateTextureMipmap`, `glGenerateTextureMipmapEXT`.

### T3 texture associations

`glMultiTexBufferEXT`, `glMultiTexRenderbufferEXT`, `glTexBuffer`, `glTexBufferARB`, `glTexBufferEXT`, `glTexBufferRange`, `glTextureBuffer`, `glTextureBufferEXT`, `glTextureBufferRange`, `glTextureBufferRangeEXT`, `glTextureRenderbufferEXT`, `glTextureView`.

### T4 texture parameters

`glMultiTexParameterfEXT`, `glMultiTexParameterfvEXT`, `glMultiTexParameteriEXT`, `glMultiTexParameterIivEXT`, `glMultiTexParameterIuivEXT`, `glMultiTexParameterivEXT`, `glTexParameterf`, `glTexParameterfv`, `glTexParameteri`, `glTexParameterIiv`, `glTexParameterIivEXT`, `glTexParameterIuiv`, `glTexParameterIuivEXT`, `glTexParameteriv`, `glTexParameterxOES`, `glTexParameterxvOES`, `glTextureParameterf`, `glTextureParameterfEXT`, `glTextureParameterfv`, `glTextureParameterfvEXT`, `glTextureParameteri`, `glTextureParameteriEXT`, `glTextureParameterIiv`, `glTextureParameterIivEXT`, `glTextureParameterIuiv`, `glTextureParameterIuivEXT`, `glTextureParameteriv`, `glTextureParameterivEXT`.

### T5 conditional automatic mip generation

`glCompressedMultiTexSubImage1DEXT`, `glCompressedMultiTexSubImage2DEXT`, `glCompressedMultiTexSubImage3DEXT`, `glCompressedTexSubImage1D`, `glCompressedTexSubImage1DARB`, `glCompressedTexSubImage2D`, `glCompressedTexSubImage2DARB`, `glCompressedTexSubImage3D`, `glCompressedTexSubImage3DARB`, `glCompressedTextureSubImage1D`, `glCompressedTextureSubImage1DEXT`, `glCompressedTextureSubImage2D`, `glCompressedTextureSubImage2DEXT`, `glCompressedTextureSubImage3D`, `glCompressedTextureSubImage3DEXT`, `glCopyMultiTexSubImage1DEXT`, `glCopyMultiTexSubImage2DEXT`, `glCopyMultiTexSubImage3DEXT`, `glCopyTexSubImage1D`, `glCopyTexSubImage1DEXT`, `glCopyTexSubImage2D`, `glCopyTexSubImage2DEXT`, `glCopyTexSubImage3D`, `glCopyTexSubImage3DEXT`, `glCopyTextureSubImage1D`, `glCopyTextureSubImage1DEXT`, `glCopyTextureSubImage2D`, `glCopyTextureSubImage2DEXT`, `glCopyTextureSubImage3D`, `glCopyTextureSubImage3DEXT`, `glMultiTexSubImage1DEXT`, `glMultiTexSubImage2DEXT`, `glMultiTexSubImage3DEXT`, `glTexSubImage1D`, `glTexSubImage1DEXT`, `glTexSubImage2D`, `glTexSubImage2DEXT`, `glTexSubImage3D`, `glTexSubImage3DEXT`, `glTexSubImage4DSGIS`, `glTextureSubImage1D`, `glTextureSubImage1DEXT`, `glTextureSubImage2D`, `glTextureSubImage2DEXT`, `glTextureSubImage3D`, `glTextureSubImage3DEXT`.

### V1 vertex configuration

`glArrayObjectATI`, `glBindBuffer`, `glBindBufferARB`, `glBindVertexBuffer`, `glBindVertexBuffers`, `glBinormalPointerEXT`, `glClientActiveTexture`, `glClientActiveTextureARB`, `glColorPointer`, `glColorPointerEXT`, `glColorPointerListIBM`, `glColorPointervINTEL`, `glDisableClientState`, `glDisableClientStateiEXT`, `glDisableClientStateIndexedEXT`, `glDisableVertexArrayAttrib`, `glDisableVertexArrayAttribEXT`, `glDisableVertexArrayEXT`, `glDisableVertexAttribArray`, `glDisableVertexAttribArrayARB`, `glEdgeFlagPointer`, `glEdgeFlagPointerEXT`, `glEdgeFlagPointerListIBM`, `glElementPointerATI`, `glEnableClientState`, `glEnableClientStateiEXT`, `glEnableClientStateIndexedEXT`, `glEnableVertexArrayAttrib`, `glEnableVertexArrayAttribEXT`, `glEnableVertexArrayEXT`, `glEnableVertexAttribArray`, `glEnableVertexAttribArrayARB`, `glFogCoordPointer`, `glFogCoordPointerEXT`, `glFogCoordPointerListIBM`, `glIndexPointer`, `glIndexPointerEXT`, `glIndexPointerListIBM`, `glInterleavedArrays`, `glMatrixIndexPointerARB`, `glMultiTexCoordPointerEXT`, `glNormalPointer`, `glNormalPointerEXT`, `glNormalPointerListIBM`, `glNormalPointervINTEL`, `glReplacementCodePointerSUN`, `glSecondaryColorPointer`, `glSecondaryColorPointerEXT`, `glSecondaryColorPointerListIBM`, `glTangentPointerEXT`, `glTexCoordPointer`, `glTexCoordPointerEXT`, `glTexCoordPointerListIBM`, `glTexCoordPointervINTEL`, `glVariantArrayObjectATI`, `glVariantPointerEXT`, `glVertexArrayAttribBinding`, `glVertexArrayAttribFormat`, `glVertexArrayAttribIFormat`, `glVertexArrayAttribLFormat`, `glVertexArrayBindingDivisor`, `glVertexArrayBindVertexBufferEXT`, `glVertexArrayColorOffsetEXT`, `glVertexArrayEdgeFlagOffsetEXT`, `glVertexArrayElementBuffer`, `glVertexArrayFogCoordOffsetEXT`, `glVertexArrayIndexOffsetEXT`, `glVertexArrayMultiTexCoordOffsetEXT`, `glVertexArrayNormalOffsetEXT`, `glVertexArraySecondaryColorOffsetEXT`, `glVertexArrayTexCoordOffsetEXT`, `glVertexArrayVertexAttribBindingEXT`, `glVertexArrayVertexAttribDivisorEXT`, `glVertexArrayVertexAttribFormatEXT`, `glVertexArrayVertexAttribIFormatEXT`, `glVertexArrayVertexAttribIOffsetEXT`, `glVertexArrayVertexAttribLFormatEXT`, `glVertexArrayVertexAttribLOffsetEXT`, `glVertexArrayVertexAttribOffsetEXT`, `glVertexArrayVertexBindingDivisorEXT`, `glVertexArrayVertexBuffer`, `glVertexArrayVertexBuffers`, `glVertexArrayVertexOffsetEXT`, `glVertexAttribArrayObjectATI`, `glVertexAttribBinding`, `glVertexAttribDivisor`, `glVertexAttribDivisorARB`, `glVertexAttribFormat`, `glVertexAttribIFormat`, `glVertexAttribIPointer`, `glVertexAttribIPointerEXT`, `glVertexAttribLFormat`, `glVertexAttribLPointer`, `glVertexAttribLPointerEXT`, `glVertexAttribPointer`, `glVertexAttribPointerARB`, `glVertexBindingDivisor`, `glVertexPointer`, `glVertexPointerEXT`, `glVertexPointerListIBM`, `glVertexPointervINTEL`, `glVertexWeightPointerEXT`, `glWeightPointerARB`.
