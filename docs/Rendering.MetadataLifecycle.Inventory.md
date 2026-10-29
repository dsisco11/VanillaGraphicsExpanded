# Resource lifecycle binding inventory

Planning input for [the approved lifecycle design](Rendering.MetadataLifecycle.Proposal.md) and
[its dedicated implementation plan](Rendering.MetadataLifecycle.todo), which feeds subphase 8i of
[the RHI implementation plan](Rendering.GpuCommands.todo). This maps lifecycle/configuration coverage
and migration dispositions. Runtime qualification is reported separately from source coverage.

## Source and limits

Fresh MSBuild `ResolveReferences` and the reproducible PE probe in `artifacts/metadata-lifecycle/probe/`
select `G:/Vintagestory/Lib/OpenTK.Graphics.dll`, version 4.9.4.0, MVID
`56fce2c6-82e9-454c-8c16-a88f90e06a2d`, SHA-256
`5B57957CA9E4C5F7BFB7EC495B991E6B88CFAC52863A19E405492AB8C33CB1D2`.
The project references `$(VINTAGE_STORY)/Lib/OpenTK.Graphics.dll`; `resolved-references.json` records
the evaluated reference. The fresh probe recovered 1,981 symbols and 9,693 nongeneric calli overloads.
`artifacts/metadata-lifecycle/bindings.json` retains per-overload owner, slot and managed/native signatures. The durable
[binding table](Rendering.MetadataLifecycle.Bindings.tsv) records 435 selected factory, realization,
configuration, output and excluded-candidate symbols with exact owners, ABI, migration phase and current
observer source. Selection combines the API family matrix, current native observers and factory/implicit
realization rules below; current observer presence does not limit required coverage.

All selected ABIs use StdCall in this assembly. `int`/`uint` are 32-bit, `byte` is GLboolean,
`nint`/pointers are native-width, and `long`/`ulong` are 64-bit. Counted name factories write `uint*`;
shader/program factories return `int`, FenceSync returns `nint`. Arrays are read only during delivery.
Managed convenience signatures are not the ABI: excluded CL-event binding arguments decode as `nint*`,
and ImportSyncEXT as `(int,nint,uint) -> nint`. Generic marshaling remains OpenTK-owned. This is PE/source
coverage evidence, not native execution of every signature or extension.

Only GL roots and their Core/Ext/Arb owners were enumerated. An extension suffix does not establish
an eligible owner. Legacy assembly programs, display lists and EXT vertex-shader symbol allocation
remain excluded even when exposed under an eligible owner. Platform imports listed below remain
subject to the existing supported capability and native-handle contracts.

The tables list native entry points, collapsing managed convenience overloads. `GL` means
`OpenTK.Graphics.OpenGL.GL`; `GL4` means `OpenTK.Graphics.OpenGL4.GL`. A plus suffix denotes
the nested binding owner. Observer presence is source evidence only, not effect correctness.

## Creation, reservation and import candidates

| Native symbol | Binding owners | Current observation / disposition |
| --- | --- | --- |
| `glCreateBuffers` | `GL`, `GL4` | Creation observer implemented; see maintained buffer routes below |
| `glCreateFramebuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateMemoryObjectsEXT` | `GL+Ext` | Creation observer implemented; see maintained buffer routes below |
| `glCreateProgram` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateProgramObjectARB` | `GL+Arb` | No creation/import observer declared |
| `glCreateProgramPipelines` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateQueries` | `GL`, `GL4` | Implemented; see maintained synchronization routes below |
| `glCreateRenderbuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateSamplers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShader` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShaderObjectARB` | `GL+Arb` | No creation/import observer declared |
| `glCreateShaderProgramEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glCreateShaderProgramv` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShaderProgramvEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glCreateSyncFromCLeventARB` | `GL+Arb`, `GL4+Arb` | Excluded from supported lifecycle scope; see resolved exclusions below |
| `glCreateTextures` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateTransformFeedbacks` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateVertexArrays` | `GL`, `GL4` | Creation observer implemented; see maintained buffer routes below |
| `glFenceSync` | `GL`, `GL4` | Implemented; see maintained synchronization routes below |
| `glGenBuffers` | `GL`, `GL4` | Creation observer implemented; see maintained buffer routes below |
| `glGenBuffersARB` | `GL+Arb` | Creation observer implemented; see maintained buffer routes below |
| `glGenFramebuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenFramebuffersEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenLists` | `GL` | Excluded legacy feature |
| `glGenProgramPipelines` | `GL`, `GL4` | No creation/import observer declared |
| `glGenProgramPipelinesEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glGenProgramsARB` | `GL+Arb` | Excluded legacy feature |
| `glGenQueries` | `GL`, `GL4` | Implemented; see maintained synchronization routes below |
| `glGenQueriesARB` | `GL+Arb` | Implemented; see maintained synchronization routes below |
| `glGenRenderbuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenRenderbuffersEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenSamplers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenSemaphoresEXT` | `GL+Ext` | Implemented; see maintained synchronization routes below |
| `glGenSymbolsEXT` | `GL+Ext` | Excluded legacy feature |
| `glGenTextures` | `GL`, `GL4` | No creation/import observer declared |
| `glGenTexturesEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenTransformFeedbacks` | `GL`, `GL4` | No creation/import observer declared |
| `glGenVertexArrays` | `GL`, `GL4` | Creation observer implemented; see maintained buffer routes below |
| `glGenVertexShadersEXT` | `GL+Ext` | Excluded legacy feature |
| `glImportMemoryFdEXT` | `GL+Ext` | Implemented; see maintained buffer and memory-object routes below |
| `glImportMemoryWin32HandleEXT` | `GL+Ext` | Implemented; see maintained buffer and memory-object routes below |
| `glImportMemoryWin32NameEXT` | `GL+Ext` | Implemented; see maintained buffer and memory-object routes below |
| `glImportSemaphoreFdEXT` | `GL+Ext` | Implemented; see maintained synchronization routes below |
| `glImportSemaphoreWin32HandleEXT` | `GL+Ext` | Implemented; see maintained synchronization routes below |
| `glImportSemaphoreWin32NameEXT` | `GL+Ext` | Implemented; see maintained synchronization routes below |
| `glImportSyncEXT` | `GL+Ext` | Excluded from supported lifecycle scope; see resolved exclusions below |

## Deletion candidates

| Native symbol | Binding owners | Current observation / disposition |
| --- | --- | --- |
| `glDeleteBuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteBuffersARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteFramebuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteFramebuffersEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteLists` | `GL` | No observer declared; reconcile feature scope before adding |
| `glDeleteMemoryObjectsEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteNamedStringARB` | `GL+Arb`, `GL4+Arb` | No observer declared; reconcile feature scope before adding |
| `glDeleteObjectARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteProgram` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteProgramPipelines` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteProgramPipelinesEXT` | `GL+Ext`, `GL4+Ext` | No observer declared; reconcile feature scope before adding |
| `glDeleteProgramsARB` | `GL+Arb` | No observer declared; reconcile feature scope before adding |
| `glDeleteQueries` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteQueriesARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteRenderbuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteRenderbuffersEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteSamplers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteSemaphoresEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteShader` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteSync` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteTextures` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteTexturesEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteTransformFeedbacks` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteVertexArrays` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteVertexShaderEXT` | `GL+Ext` | No observer declared; reconcile feature scope before adding |

## Lifecycle migration decisions

The design dispositions below identify the full migration scope. Owning phase numbers refer to
`Rendering.MetadataLifecycle.todo`; the maintained buffer/memory routes below identify implemented
changes. Factory rows above and the binding table distinguish installed observers from pending coverage.
The binding table supplies exact signatures for the family decisions below.

### Activation and adoption provenance (Phase 2)

`VanillaGraphicsExpandedModSystem.Start` installs table hooks. The installation/context observation
boundary establishes the manager before delivery; `GpuResourceManagerModSystem.StartClientSide` attaches
the renderer to that same service. `FindObservedResource` is lookup-only; creation observers establish
covered identities. Missing buffer entries cannot authorize acquisition after covered creation.

Use the existing context registration/service composition boundary to publish its one manager before
enabling resource observation for that context. Do not wait for the first RHI command or renderer tick.
Separate service construction from renderer-event registration so early observation can use that same
manager when StartClientSide later attaches its renderer. Do not create a cache-side registry or queue
native output pointers for later consumption. Context recreation retires the old manager and establishes
the replacement before covered creation; binding reload replaces addresses without replacing identities
when the native context is unchanged. Loader callbacks retain always-forward behavior. A readiness defect
is diagnosed and cannot silently grant later discovery. Capability-independent creation must not require
capability queries or a fully admitted RHI backend; optional/limit-dependent effects use captured facts.

Pre-install adoption needs an explicit integration-owned provenance record for the original context,
kind and identity known to exist before resource observation was enabled. Capture this at the engine
owner/setup boundary, before activation, using its existing resource references; do not enumerate native
objects or treat every absent ID as old. Resolve it through the existing manager registration, consume
initial discovery once, and retain original association/deletion semantics. A caller-supplied `external`
label or unrestricted `allowQuery` flag is insufficient. If an owner cannot establish this provenance,
fail managed adoption as a tracking/provenance defect rather than guess. This creates no general second
identity registry: the evidence belongs to that explicit integration handoff and then the canonical entry.
Objects created after activation need no adoption receipt: creation already established their entry.
Recovery instead names an existing unavailable original entry, the failed effect and required missing
fact; it cannot manufacture an entry for a missing covered object or renew a retired backing identity.
Local tools use their supplied RHI initialization snapshot; absence of a global manager is not provenance.

Phase 2 tests must call native factories before the first explicit RHI command, after context recreation
and after binding reload, then associate wrappers with no compensating descriptor reads. They must also
reject a missing post-activation identity and a reused/deleted pre-install adoption identity.

### Realization and defaults

Normative sources: [core 4.5 specification](https://registry.khronos.org/OpenGL/specs/gl/glspec45.core.pdf),
[ARB direct state access](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_direct_state_access.txt),
[EXT direct state access](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_direct_state_access.txt),
[sampler objects](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_sampler_objects.txt),
[external objects](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_external_objects.txt).
The core object chapters and state tables control defaults; DSA issue discussions alone do not replace
per-command rules. Zero/default binding objects do not get ordinary manager registrations.

| Kind / phase | Reservation and realization | Complete represented default / storage distinction |
| --- | --- | --- |
| Buffer / 2 | GenBuffers and ARB reserve; legal BindBuffer, BindBufferBase/Range realize; CreateBuffers realizes. EXT named-buffer operations follow EXT creation semantics, unlike core named commands requiring an existing object. Multi-bind does not generally create names. | Capacity 0, Usage StaticDraw, ImmutableStorage false, StorageFlags None. Storage allocation replaces these fields; content writes do not. |
| MemoryObject / 2 | CreateMemoryObjectsEXT establishes objects; imports attach external payload to the same identity. | Explicit empty GpuMemoryObjectMetadata; import/handle policy stays owner/backend-owned. No descriptor query. |
| Query / 3 | GenQueries/ARB reserve; BeginQuery/ARB, BeginQueryIndexed and QueryCounter realize with their target; CreateQueries realizes for its supplied target. | Empty GpuQueryMetadata; target/activity acceptance belongs to execution knowledge, result availability/value is a driver output. |
| Sync / 3 | FenceSync returns a realized pointer identity or null. | Empty GpuSyncMetadata; signaling is not a cached descriptor. |
| Semaphore / 3 | GenSemaphoresEXT reserves names; external semaphore state is associated on import, on the same original registration. | Empty GpuSemaphoreMetadata on established realization. A generated name is not an imported usable semaphore; payload success is not implied by a nonzero handle argument. |
| Texture / 4 | GenTextures/EXT reserves; valid BindTexture/EXT and explicit-unit EXT bind establish target; CreateTextures supplies target and realizes. EXT named texture commands may establish typed objects. TextureView consumes an unused generated name and establishes its view target. BindTextures and BindTextureUnit require already established texture objects. | Target from realization, empty allocated-image collection, ImmutableStorage false, ImmutableLevels 0, BaseLevel 0, MaxLevel 1000 for targets supporting mip parameters, no backing/view association. Target-inapplicable parameters are not queried. Allocation defines images; reservation never means usable empty texture. |
| Renderbuffer / 4 | GenRenderbuffers/core/EXT reserve; BindRenderbuffer/core/EXT or eligible EXT named storage realizes; CreateRenderbuffers realizes. | Width/height/samples 0, initial internal format Rgba. This is empty storage, not an allocated zero-size attachment. |
| Framebuffer / 4 | GenFramebuffers/core/EXT reserve; BindFramebuffer/core/EXT or EXT named creation-capable operations realize; CreateFramebuffers realizes. | Empty attachment map, default width/height/layers/samples 0, fixed-sample-locations true. Draw routing is ColorAttachment0, read selection ColorAttachment0, with other draw slots None; routing is execution-owned. Default framebuffer is a surface. |
| Shader / 5 | CreateShader/CreateShaderObjectARB immediately realize the returned stage-specific identity. | GpuShaderMetadata.Type is supplied stage. Compilation status/log/source remain explicit compiler inputs/outputs, not new canonical fields. |
| Program / 5 | CreateProgram/CreateProgramObjectARB immediately realize; combined shader-program factories also attempt compilation/linking. | Fresh ordinary program: ExecutableAvailable false, empty Resources/TextureUnits, null geometry/tessellation/local-size outputs. Combined factories require executable completion; a returned name alone cannot publish a linked interface. |
| ProgramPipeline / 5 | GenProgramPipelines reserves; BindProgramPipeline and UseProgramStages create as specified; CreateProgramPipelines realizes. | Empty canonical struct. Stage/active-program selections remain existing execution facts, initially no programs. |
| VertexArray / 6 | GenVertexArrays reserves; BindVertexArray realizes; CreateVertexArrays realizes. EXT named generic-attribute commands use their specified creation rules. | Empty canonical struct. Execution defaults include no element/vertex buffers, attributes disabled, zero offsets/divisors, default attribute formats and attribute-to-binding assignments. Preserve the distinction between separate-binding stride defaults and legacy pointer stride interpretation. |
| TransformFeedback / 6 | GenTransformFeedbacks reserves; BindTransformFeedback realizes; CreateTransformFeedbacks realizes. | Empty canonical struct; no indexed buffer bindings, inactive and unpaused execution state. |
| Sampler / 6 | GenSamplers reserves names; first BindSampler, SamplerParameter*, GetSamplerParameter* or IsSampler establishes default state. CreateSamplers realizes immediately. Multi-bind follows its specified equivalent per-element bind behavior. | Empty canonical struct on realization; existing sampler policy is not a native parameter mirror. Observe realization even on getters/IsSampler without issuing an extra query. |

Realization must occur before applying that command's configuration effect, once on the original entry.
Generated names and unallocated objects are not interchangeable. Bind-created desktop names permitted by
the native specification must be admitted even without a preceding observed Gen call; commands that
require a generated/existing name must not gain this exception. For core DSA invalid names, preserve
rejection instead of creating an entry. EXT object-creation rules are evaluated by actual command,
target and profile; do not apply one suffix-based rule to every EXT method.

EXT DSA specifically initializes eligible unused named objects before subsequent command error checks.
Thus a rejected storage/parameter update may still have realized a default object: publish the creation
effect while preserving its unchanged defaults, rather than classifying the entire call as no-op.
The binding table includes named EXT content and query operations for this reason (named buffer
subdata/map/unmap/getters/copy, texture subimages/getters, framebuffer status/getters and vertex-array
getters). Their content/query result is not a new descriptor, but their realization is observable.
EXT generic vertex-array offset operations can also realize a nonzero backing buffer: Phase 2 must
include that buffer-side effect before Phase 6 migrates VAO certification. Core named getters do not
inherit EXT creation semantics. Legacy fixed-function EXT array operations remain excluded.

### Optional and excluded candidates

`glCreateSyncFromCLeventARB` is OpenCL-event interoperability, not the supported fence insertion API
([ARB_cl_event](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_cl_event.txt)). `glImportSyncEXT`
imports an X11 synchronization fence and is restricted to suitable X11 contexts
([EXT_x11_sync_object](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_x11_sync_object.txt)).
Neither has an existing managed operation or imported-sync consumer in this checkout. Both are excluded
from this migration; their mere presence in eligible OpenTK owners does not add CL/X11 integration.
Existing FD/Win32 memory and semaphore imports remain in Phases 2/3, including platform aliases even
when the current machine cannot execute them. No imported descriptor query exception is established.

Exclude glGenLists/glDeleteLists, glGenProgramsARB/glDeleteProgramsARB, glGenSymbolsEXT,
glGenVertexShadersEXT/glDeleteVertexShaderEXT as legacy features. glDeleteNamedStringARB deletes a
shader include string, not a GpuResourceKind allocation; it has no canonical resource registration.
EXT-suffixed pipeline-object Gen/Delete/Bind/ActiveShaderProgram variants and CreateShaderProgramvEXT
belong to the ES separate-shader-object API and are excluded from desktop coverage. Desktop
glCreateShaderProgramEXT and core glCreateShaderProgramv remain Phase 5. Desktop EXT direct-state-access
ProgramUniform methods remain covered; this is not a blanket EXT exclusion.
The same ES-only disposition applies to UseProgramStagesEXT. The distinct
[desktop](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_separate_shader_objects.gl.txt) and
[ES](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_separate_shader_objects.gles.txt)
specifications establish this distinction. BindTextureUnitParameterEXT is excluded EXT vertex-shader
state, not modern texture binding.

### Field provenance, acceptance and consumers

`I` = supplied input, `D` = guaranteed native default, `V` = derived from known inputs, `O` = driver output.
The records below cover every field in Rendering/Metadata, including nested image/interface records.
Entry state, revisions and availability are manager bookkeeping, never queried native descriptors.

| Schema fields / phase | Provenance and cached acceptance | Consumers / read disposition |
| --- | --- | --- |
| GpuBufferMetadata Capacity, Usage, ImmutableStorage, StorageFlags / 2 | D above; I allocation size/usage/access, V storage mode. Require legal target/size/flags, live realized identity, immutable replacement/mapping restrictions from established execution facts. | GpuBufferObject capacity, RHI ranges, buffer views, engine geometry. No ordinary reads; pre-install or explicit failed-effect recovery only. |
| GpuTextureMetadata Target / 4 | I typed creation/first valid realization, immutable thereafter. | BorrowedTexture, sampled/image validation; no target discovery for covered objects. |
| Levels: Level, Face, Width, Height, Depth, Format, Samples, Allocated / 4 | I image definitions; V immutable mip extents (array layers do not shrink, 3D depth does), cube faces and view-relative geometry; D no allocated levels. Format I when sized, O only unsized resolution. Samples V cached supported counts or approved one-time O fallback. Require legal dimensions/level/format/target, cached limits, mutable/immutable rules and source completeness for generation. | Texture owners, GpuImageMetadata projections, framebuffer/pass compatibility and feedback checks. Subimage/copy-subimage/clear preserve descriptors. CopyTexImage defines storage and is not content-only. |
| ImmutableStorage, ImmutableLevels, BaseLevel, MaxLevel / 4 | D defaults above; I/V storage and view creation; I valid parameter conversions/clamps. Mutable mip generation derives descendants from known complete base, bounded by max/geometry. | View validation, mip selection and projections. Replace parameter/image readback with known facts; uncertain conversion withdraws affected certification. |
| Buffer: BufferId, Format, Offset, Size, WholeBuffer / 4 (buffer prerequisite 2) | I association command, V whole-buffer convention (Size=0 sentinel), I explicit native-width range. Require original live backing, legal format and cached alignment/capacity. | GpuBufferView/GpuBufferTexture and sampled validation. No range getter can establish whole-buffer intent or reconnect a deleted backing. |
| View: BaseLevel, LevelCount, BaseLayer, LayerCount; Renderbuffer / 4 | I view/range/association command, V clamped legal source ranges and image geometry. Retain source registration/storage dependency separately. | GpuTextureView, source certificates and renderbuffer texture consumers. Source name is not queryable from a native texture view; never invent it. |
| GpuRenderbufferMetadata Width, Height, Format, Samples / 4 | D empty storage; I size/format; V/O sample rule above. Require legal format, nonnegative bounds, cached extent/sample support. | Attachments and renderbuffer-backed texture projections; retain only narrow output reads. |
| GpuFramebufferMetadata Attachments; image Kind, Id, MipLevel, Layer, CubeFace, Layered, BaseView, ViewCount, Mode / 4 | D empty; I attachment arguments and selection mode, V packed depth/stencil dual slots. Require valid target/slot/mip/layer and original backing facts. Zero detaches; default-FBO commands do not create entries. | GpuFramebuffer retained composition, RenderPassTargets, attachment validation. Preserve known command mode; queries cannot manufacture ambiguous multiview evidence. |
| DefaultWidth, DefaultHeight, DefaultLayers, DefaultSamples, DefaultFixedSampleLocations / 4 | D above; I valid framebuffer parameters, cached maxima. | Attachmentless pass targets; no full/default-field read after known setter. |
| GpuImageMetadata Width, Height, Depth, Format, Samples / 4 | V selected backing snapshot and GpuImageSelection Level/Layer/Face/Layered/BaseView/ViewCount (wrapper-local I). | GpuFramebufferAttachment; no independent registration or image query at validation. |
| GpuShaderMetadata Type / 5 | I factory stage, reject unsupported stage by cached capability. | Shader owner and linking; read only proven pre-install shader type. |
| GpuProgramMetadata ExecutableAvailable, Resources, GeometryInput, TessellationOutput, ComputeLocalSize / 5 | D fresh unlinked, O link/binary completion and interface reflection. | RHIShader/PSO, GpuProgramLayout and GpuPreparedBindings; reflection only executable change/completion or justified adoption. |
| Resource Interface, Index, Name, Type, Location, ArraySize, BlockIndex, BufferDataSize, ReferencedStages, LocationIndex / 5 | O executable interface, nullable when absent/inapplicable. BufferBinding starts from O link output and updates from I block assignments. | Prepared blocks, active slots and shader input validation; no full reflection after assignments. |
| TextureUnits Location, Unit / 5 | Location O; Unit initial executable O, then I signed unit setters at represented locations. Ignore numeric/unused locations before interruption; cached unit limits and count validity. | Prepared sampler/image slots; opaque bindless assignment invalidates unsupported unit representation, not guessed conversion. |
| GpuVertexArrayMetadata, GpuTransformFeedbackMetadata, GpuSamplerMetadata / 6; GpuProgramPipelineMetadata / 5; GpuQueryMetadata, GpuSyncMetadata, GpuSemaphoreMetadata / 3; GpuMemoryObjectMetadata / 2 | Explicit empty snapshots on realization. Configuration revisions/activity/selection remain existing owners. | References, lifecycle and native operations; an empty schema never justifies scanning unrepresented native state. |

Execution facts follow the same I/D/V policy: selectors from legal binds, indexed buffer ranges from
accepted per-slot inputs, VAO EBO/stream dependencies from configuration, framebuffer draw routing from
route setters, pipeline uniform destination/stages from selection commands, transform-feedback activity
from legal transitions, and context limits from explicit admission. Missing cached limits make only the
dependent effect uncertain. Native observers cannot query limits on demand. Multi-bind rejection is
per element where specified; it never justifies invalidating every slot or publishing every element.
Deletion updates existing dependency/selector owners alongside original-entry retirement.
Buffer mapping acceptance belongs to the existing execution-knowledge owner, not a new metadata schema:
MapBuffer/MapBufferRange/MapNamedBuffer and ARB/EXT variants establish mapping on a non-null returned
pointer; UnmapBuffer/UnmapNamedBuffer update that knowledge according to their outcome. Flush operations
do not end mapping. Phase 2 covers these routes and their invalid-argument cases. If history cannot
establish mapping, only the dependent storage effect becomes uncertain; it does not trigger discovery.

### Outcome and read causes by family

All families preserve proven rejection, publish established effects and withdraw only uncertain affected
certification. An external command is always forwarded first. No GetError/Is probe is added to prove
success; stale output arrays, invalid counts, null/zero results and already-live factory names cannot
overwrite a registration. Native output arrays are consumed synchronously, never retained.

| Phase | Established / rejected / uncertain examples | Remaining legitimate reads |
| --- | --- | --- |
| 2 | Valid buffer allocation supplies storage; immutable reallocation/invalid size preserves prior facts; missing mapped-state/capability evidence withdraws affected storage without querying automatically. Memory import preserves identity. | Proven pre-install buffer; explicit named failed-effect recovery; deletion outcomes. None for empty memory descriptors or import handles. |
| 3 | Valid query realization/fence result establishes realization; semaphore Gen reserves identity; invalid target/count/null fence does not. Unknown import acceptance does not invent payload state. | Query results, waits, deletion outcome, one targeted IsSemaphore outcome at the RHI import boundary for wait/signal admission. External imports do not probe. No descriptor scan. |
| 4 | Valid allocation/attachment/range updates supplied facts; invalid mip/immutable replacement/backing bounds preserves prior facts; unknown optional external-image storage cannot fabricate descriptors. | Unsized resolved format; one-time samples if cached resolution unavailable; proven pre-install discovery; specific failed field recovery; deletion. EGL-image storage has no approved new import-query exemption: unsupported unknown descriptors stay unavailable unless an actual consumer proves a needed exception. |
| 5 | Valid unit/block assignment patches known interface; ignored/numeric writes do nothing; link submission withdraws installed executable certification pending output; bindless representation remains unsupported. | Compile/link status/log/reflection, binary acceptance/output; pre-install stage/program; narrow failed assignment recovery; deferred deletion outcomes. |
| 6 | Valid configuration advances original certificates; invalid indices/strides preserve them; uncertain VAO changes invalidate only affected certificate. | Pre-install certification only when actually reusing that original VAO; otherwise use authored private realization below. Explicit feedback result read only if recorded history cannot establish active/paused outcome needed by execution. No sampler descriptor reads. |

### Reader and consumer migration ledger

Paths are relative to `VanillaGraphicsExpanded/Rendering`. A row naming several methods gives each the
same explicit disposition, not permission to omit an overload. Shared acquisition gates migrate in 2;
family-specific callers and readers migrate in the indicated complete unit. The backend query bodies
remain below RHI and are removed when no permitted caller remains.

| Named methods / current source | Final disposition and consumer | Owner |
| --- | --- | --- |
| GpuResourceManager.AcquireResource, RecoverResource (Registry.cs) | Separate available association from proven pre-install discovery and original-entry recovery; remove missing-entry/unavailable automatic read. Used by all following acquisitions. | 2; caller closure 2–6 |
| AcquireWrapperMetadata, RecoverWrapperMetadata, CanAcquireWrapperMetadata (MetadataAcquisition.cs) | Same provenance gate, no no-manager fallback. Keep original-wrapper/context checks and supplied local handoff. | 2; families 2–6 |
| RefreshMetadata, PublishMetadata, PublishMetadataCore (MetadataPublication.cs) | Refresh only explicit allowed read cause; supplied publication never acquires backing implicitly. Retain one complete publication and original-identity checks. | 2 foundation; 4 backing closure |
| ObserveWrapperMetadata (MetadataObservation.cs) | Retain executable output completion only where needed; remove generic mutation readback usages. | 5; image callers 4 |
| AcquireExternalFacts (ExternalFacts.cs) | Shared provenance gate in 2; actual callers CloudNativeMapAdapter.TryCaptureExecutable and framebuffer capture consume maintained program/framebuffer facts in 5/4. No absence-of-manager exemption. | 2 gate; 4/5 callers |
| AcquireBufferMetadata (BufferAcquisition.cs) | No current production caller; remove unused entry rather than retaining a generic query escape. EngineGeometryInputs.BufferRecipient uses AcquireWrapperMetadata directly. | 2 |
| AcquireBufferTextureInput (BufferTextureCreation.cs), AcquireBufferTextureBacking (BufferTextureDependencies.cs) | Resolve original maintained buffer and capacity; no publication-triggered missing backing discovery. Supplied local source stays supported. | 2 dependency input; 4 association |
| AcquireRenderbufferTextureBacking (RenderbufferTextureDependencies.cs) | Resolve original renderbuffer from maintained facts; retain deletion/replacement certificate and no implicit renewal. | 4 |
| AcquireExternalImageTexture, AcquireImageMetadata (ImageAcquisition.cs) | Associate covered texture/backing and project selected image; raw local ID is not discovery provenance. Existing owner snapshots remain query-free. | 4 |
| AcquireFramebufferTexture, AcquireFramebufferRenderbuffer (FramebufferBackings.cs) | Replace recover bool's blanket read permission with explicit provenance/cause; reuse one backing snapshot. | 4 |
| DiscoverFramebufferAttachment (FramebufferImport.cs), ResolveFramebufferAttachments (FramebufferImages.cs) | Consume maintained attachment/backing facts, preserve original composition/ownership; explicit initial adoption resolves unknown descriptors once. | 4 |
| AcquireTextureCommandInput (TextureCommandInputs.cs) | Require available maintained/supplied snapshot; remove automatic read before owned mutation. | 4 |
| RefreshTextureParameter (TextureParameters.cs) | Remove ordinary setter readback; supplied converted base/max updates. Targeted recovery only named missing field. | 4 |
| RefreshTextureImages (TextureImages.cs), RefreshGeneratedTextureMetadata (TextureGeneration.cs) | Replace definition/generation reads with supplied/derived images; keep narrow unsized/sample result services distinct. | 4 |
| RefreshFramebufferField (FramebufferFields.cs) | Replace normal slot/default read with supplied field publication; retain explicit initial/failed-field recovery only. | 4 |
| AcquireProgramMetadata (ProgramAcquisition.cs), RefreshProgramFields (ProgramFields.cs) | Maintained interface reuse; signed array setters pass each value instead of querying. Reflected executable output remains explicit. | 5 |
| AcquireEngineGeometryConfiguration, IsEngineGeometryConfigurationCurrent (GeometryAcquisition.cs) | Reuse a producer certificate and revision, or realize a private array from authored layout/retained streams. Unknown external VAO layout alone cannot justify routine query. See closure below. | 6 |
| AssignImageMetadata, ProjectTextureImage, ProjectLayeredCubeImage, ProjectRenderbufferImage (ImageMetadata.cs); PublishOwnerImages (OwnerImages.cs) | Preserve query-free projection/notification, including intentional owner replacement. Resolve unavailable selections without recapturing. | 4 |
| GpuBufferMetadataReader.TryRead → IRHIResourceExecution.TryReadBufferMetadata | Keep only proven initial adoption/explicit failed storage recovery; GpuBufferObject and geometry ordinary paths stop calling it. | 2 |
| GpuTextureMetadataReader.TryRead → ReadExternalTexture; GpuTextureImageReader.TryRead → ReadExternalTextureImage | No covered target/image scans. Retain permitted initial or targeted recovery only. | 4 |
| GpuRenderbufferMetadataReader.TryRead → ReadExternalRenderbuffer; GpuRenderbufferTextureMetadataReader.TryRead → ReadExternalRenderbufferTexture | Same explicit causes; association arguments establish backings ordinarily. | 4 |
| GpuFramebufferMetadataReader.TryRead → ReadFramebufferMetadata; ReadFramebufferField | Initial adoption or named unresolved field only. Ordinary imported framebuffer publication consumes maintained descriptors. | 4 |
| GpuShaderMetadataReader.TryRead → ReadShaderMetadata | Initial adoption stage only; covered factory stage is input. | 5 |
| GpuProgramMetadataReader.TryRead → ReadProgramMetadata | Keep driver executable/reflection completion and justified pre-install adoption; remove assignment-triggered full reflection. | 5 |
| ReadExternalTextureParameter, ReadExternalTextureLevelParameter | Remove ordinary parameter/storage dispatch to these; preserve narrow resolved-format/sample output and exact recovery field where applicable. | 4 |
| OpenGlStateCache.TryObserveBufferAssociation, ReadBufferTextureParameter (NativeBufferTexture.cs) | Replace ordinary buffer/range association readback and implicit backing acquisition with supplied association and maintained capacity; only named initial/recovery causes may query. | 4 |
| OpenGLBackend.ReadExecutableStageMetadata (ShaderStageQueries.cs) | Retain explicit linked-executable output for stage metadata; not an ordinary setter validation query. | 5 |
| ReadExternalProgramUnit, ReadExternalProgramBlockBinding | Remove normal assignment readback; retain only explicit unresolved field recovery. | 5 |
| OpenGLBackend.CertifyGeometryConfiguration, ReadGeometryInteger (Geometry/OpenGLBackend.GeometryAcquisition.cs) | No covered configuration scanning merely because canonical VAO metadata is empty; migrate original-array reuse decision as below. | 6 |
| IRHIResourceExecution.IsExternalBuffer, IsExternalTexture, IsExternalRenderbuffer, IsProgramResource, IsShaderResource | Not ordinary metadata admission probes. Keep only a demonstrated permitted initial-discovery/deletion purpose or remove unused methods with caller. | 2/4/5 |
| ReadFormatCapabilities (GpuSupport cache through RHI); ReadWindowSurface | Keep explicit capability/surface admission, outside resource descriptor acquisition and native callbacks. Surface zero has no creation interception. | 2 activation; 4 consumers |
| RHICommandContext and OpenGLBackend AcquireBorrowedTexture, AcquireAttachmentImage, AcquireExternalAttachmentTexture, AcquireProgramInterface | Retain semantic boundary; propagate explicit acquisition cause to manager, never choose query policy from raw ID/missing entry. | 4/5 |
| RefreshFramebufferAttachments, GpuFramebuffer.RefreshWrappedFramebuffer, PublishRenderPassMetadata | Wrapper/integration composition from maintained facts; refresh spelling cannot authorize full rediscovery. | 4 |
| GpuProgramInterface.RefreshResources, constructors; GpuProgramLayout interface access; GpuPreparedBindings construction | Keep supplied preparation and fixed interface identity; only admitted executable completion/adoption may fetch facts. | 5 |
| GpuTextureRef.TryGetMetadata, GpuBufferRef metadata access, GpuFramebufferAttachment.ValidateSlot, ShaderInputValidation and ShaderPreparedSubmission | Stay query-free; unavailable metadata rejects use. No repair hidden in reference conversion, validation or binding suppression. | 2/4/5/6 |

`GpuVertexBindingReader` and `GpuResourceManager.AcquireVertexBindingFacts`, named in older analysis,
are absent from current source. Their historical entries are not extra implementations to retain.
Current geometry discovery is `OpenGLBackend.CertifyGeometryConfiguration` and its helper above.
Likewise the old BeginNative*/Complete adapter transaction pairs are not migration targets; the current
post-call Observe* methods and manager completion exclusion are the relevant code.
`GpuVertexAttribBinding` itself is also absent; do not resurrect it to satisfy a historical inventory.

### Reentry and complete migration units

The concrete production reentry path is `NotifyImageDependents` →
`GpuFramebufferAttachment.NotifyDependents` → `GpuFramebuffer.OnAttachmentChanged` →
`PublishAttachmentsChanged` → the `AttachmentsChanged` event. Subscribers can dispose or replace
associations during delivery, so retain original-entry/context checks around that callback boundary.
`GpuResource.HandleMetadataChanged` is nonvirtual snapshot assignment, not a callback boundary.
The program notification/preparation chain alone does not establish arbitrary reentry. Do not retain
blanket exclusion on pure candidate arithmetic or ordinary method calls based on synthetic reader tests.
`BeginMetadataCompletion`/`RequireUninterruptedCompletion` are private publication exclusion, not an
external begin/end protocol; keep them only across demonstrated callback boundaries. Explicit native
read completion still checks original identity/revision before publishing, without asserting an
unproven production callback path inside every query.
All Observe* handlers classify relevance/rejection before InterruptResourceOperation or BeginRefresh.

| Migration unit | Blocking dependencies to include; no later cleanup allowance |
| --- | --- |
| 2 buffers + memory | Activation/provenance gate, existing engine geometry buffer acquisitions and buffer-view capacity/dependency consumers. A shared gate change must migrate all affected call signatures immediately; unchanged family internals remain explicitly unmigrated. |
| 3 sync/query/semaphore | Existing wait/query tickets and imported handle ownership; no CL/X11 API expansion. Memory prerequisites already complete in 2. |
| 4 textures/renderbuffers/framebuffers | Storage observers, base/max conversion, mip derivation, all original backing/view certificates, attachment projection/composition, cloud/depth borrowers, local tools and pass consumers. Buffer storage notifications must invalidate affected image dependencies without querying them. |
| 5 executable/pipeline | Deferred completion, binary installation, unit/block updates, authored binding preparation, fixed RHI shader/PSO references and engine adoption. Assignments cannot revive withdrawn executable generations. |
| 6 geometry/feedback/sampler | Engine mesh imports and authored streams, helper certificates, selected VAO/EBO dependencies and feedback activity. For an uncertified external array, build the existing private VAO from supplied layout/streams instead of querying to prove reuse; reuse the original only when a producer certificate already establishes its configuration. This avoids both a full VAO mirror and routine discovery. Pre-install original-array reuse may explicitly certify once. |

No dependency pushes consumer cleanup to Phase 7. Phase 7 verifies the completed units together.

## Maintained buffer and memory-object routes

`GpuResourceRegistration.IsRealized` is independent of availability and terminal retirement. Generated
buffer names remain reserved, and core creation or a legal realization publishes explicit empty-buffer
defaults on the same entry. Native output arrays are consumed during the generated post-call callback;
zero, invalid counts and already-live returned names cannot replace an allocation.

`GpuResourceManagerSystem.EnsureCurrentContext` establishes the existing manager before observation;
`OpenGLBindingInterception.Install` and `OpenGLObservationContext` activate it without backend or
capability admission. The renderer attaches to that service instead of creating another manager.
Context recreation retires the original service and attaches its replacement; binding reload retains
registrations. Local RHI tools without an installed manager retain their supplied initialization snapshots.
A missing service or registry entry cannot authorize buffer descriptor discovery. An owned context-local
buffer whose explicit storage operation failed can request recovery against its captured creation context
while no service is installed; its unavailable supplied snapshot, original owner and explicit recovery
method establish that cause. This neither adopts a raw ID nor creates a registration. Ordinary local
initialization and available-snapshot access do not invoke this path.

`RecordPreexistingBuffer` records integration-owned identity before activation, in the canonical entry.
`AcquireResource` consumes this initial-discovery cause once; ordinary covered association only reuses
available facts. `RecoverResource` requires an existing original entry and is explicit failed-effect
recovery. Reserved covered names cannot invoke recovery to manufacture realization. There is no
separate pre-install name registry. The unused `AcquireBufferMetadata` entry is removed.
`AcquireBufferTextureInput`, backing publication and engine geometry recipients use this same gate;
raw backing names and whole-buffer range intent never trigger compensating descriptor reads.

Buffer allocation observers pass size, usage, storage mode and flags into
`OpenGlStateCache.ObserveBufferStorage`. Proven invalid requests and immutable replacement leave facts
and operation exclusions intact. Established requests publish once and renew storage dependencies;
content writes do not publish capacity. EXT named content/get/map/unmap/flush operations observe their
specified implicit initialization without changing established descriptors. Core named operations and
multi-bind require existing objects. Mapping results remain in the cache's executed mapping knowledge;
null map results establish no mapping, unmap termination does not revise storage, and flushing does not
end mapping. Mutable replacement discards a previous mapping as specified by core 4.5 section 6.2.
Sparse commitment and core flush commands have no represented metadata or realization effect.

Limit-dependent realization without captured capabilities, and vertex updates that may initialize a
reserved buffer before rejecting their scalar arguments, retain initialization uncertainty in the same
entry's existing pending metadata effect. Known usable descriptors are preserved. A subsequent legal
bind can publish guaranteed empty defaults if no storage definition intervened; a potentially accepted
allocation instead retains a pending storage effect and suppresses guessed empty defaults. Only explicit
recovery of that original identity may resolve the result. No second lifecycle registry or field-level
mask is introduced, and ordinary association never triggers a compensating read.

Native range classification preserves rejected zero-name negative offsets and transform-feedback size
alignment before realization/publication. EXT offset binding derives a rounded fixed range from known
capacity. EXT Base binding retains the generic selector but leaves indexed sizing mode unknown where
its historical versus promoted semantics do not establish a portable restoration representation;
it does not silently claim core whole-buffer sizing. Missing optional driver slots are not invoked by
qualification: deterministic native-boundary stand-ins qualify callback/effect delivery, with actual
native EXT execution recorded separately as unavailable on this test driver.


Core single vertex-buffer bindings can realize generated buffers (core 4.5 section 10.3.2), unlike
multi-bind and named element-buffer assignment. Their array-name prerequisites are included with buffer
migration: Gen/Create/BindVertexArray retain identity/default facts only; full geometry certification,
configuration classification and consumer migration remain with the geometry unit. EXT generic offset
commands retain their separate initial-default effects. The binding table assigns these prerequisites
to 2 explicitly rather than leaving a completed buffer dependent on unobserved array creation.

Memory-object creation publishes `GpuMemoryObjectMetadata` once. FD, Win32-handle, Win32-name import
and parameter observations preserve that original empty schema without certifying opaque payloads or
closing caller-owned handles. Invalid/repeated import cannot create another allocation identity.
Memory-backed/external buffer storage still depends on facts that the opaque payload does not certify
(size/alignment/platform resource validity): proven rejection is preserved; an unresolved effect withdraws
buffer certification without querying it or manufacturing import success. There is no descriptor query
exception for imported resources and no new payload mirror. Existing explicit recovery remains a
consumer decision about an unavailable original buffer, not automatic acquisition at its next use.

Owned buffer/memory/array factories, memory setup and buffer mapping/deletion use generated saved-original
calls. Their existing manager handoff publishes once. Temporary initial-content staging uses that same
owned buffer handoff, not an unregistered private buffer. Native callback exceptions remain contained.
The driver generator handles pinned `out` outputs and GLboolean-to-managed-bool results.

The exact observer paths for these routes are in Rendering.MetadataLifecycle.Bindings.tsv. Families not
yet migrated retain their ledger dispositions above; this does not claim their queries are removed.

## Maintained synchronization routes

Query generation in both roots and the ARB alias reserves the original identity. Core target-specific
creation, legal BeginQuery/ARB/BeginQueryIndexed and QueryCounter publish empty GpuQueryMetadata once.
EndQuery/ARB/EndQueryIndexed and conditional-render intervals maintain execution activity, not canonical
descriptors. Boolean occlusion types remain interchangeable while all occlusion targets share one active
slot. Target conflicts, invalid counts/indices and stale live factory outputs preserve the original entry.
Optional statistics and stream limits use admission-time cached capabilities. Uncertain realization stays
unavailable; a caller's positive IsQuery/ARB result can establish the empty snapshot without adding a
probe. No query target/activity/result mirror is added to canonical metadata.

OpenGlStateCache.QueryLifecycle owns target and issued-result knowledge using weak original-registration
keys. Active intervals retain their original entry until end even after name deletion. Context selection
clears execution facts. Result tickets compare their captured issued generation, so external reissue
cannot supply a different result through an old ticket. Result changes do not revise descriptors.

FenceSync observes its nonzero native-width return, established condition and zero flags, and publishes
GpuSyncMetadata once without status discovery. CL-event and X11 sync imports remain excluded as resolved
above. Query, fence and semaphore association rejects missing covered entries and unavailable reservations
without invoking readers; empty-schema recovery requires an already realized original registration.
There is no pre-install descriptor consumer for these empty schemas, and no unused discovery API is added.

GenSemaphoresEXT reserves names without claiming a payload. The private handoff returns an owned
disposable reservation with unavailable metadata. Native FD/Win32 handle/name imports preserve identity
but cannot establish payload success from opaque inputs or void returns. They issue no queries. An
explicit caller IsSemaphoreEXT result can establish realization. The RHI import boundary performs one
approved IsSemaphore outcome check: the required fact is import realization for wait/signal admission.
It publishes the empty schema once on positive outcome; failed import cleans only its original candidate.
Unimported owners cannot wait or signal. Local owners retain immutable import intent and the same factory
outcome check before exposure, without a second registry or payload descriptor. FD transfer occurs only
on native success; Win32 handles/names remain caller-owned and observers never close them.

Owned query/fence factories and semaphore generation/import/deletion use saved originals and one private
manager handoff. Kind-specific deletion outcomes remain separate from acquisition, including reservation
release, active query name retirement, pointer-width sync deletion and name reuse. Existing memory-object
identities and buffer dependencies are unchanged; semaphores provide no storage allocation descriptor.
The binding table records the exact factory, realization, explicit-result and import ABIs/owners.
External-producer imports require separate hardware qualification; deterministic stand-ins exercise the
real loader/generated callback boundary, not operating-system payload acceptance.

## Evidence boundary

The compiler-reference probe, source reconciliation and independent planning audit establish migration
coverage only. Each owning family must qualify its implementation, exact forwarding, defaults,
name reuse, failures and context lifecycle before its own completion markers change.

Buffer and memory-object implementation qualification passed canonical shader-enabled Debug and ordinary
Release builds, 173/173 focused tests per configuration with zero skips, and 35/35 generator tests per
configuration. Second review and independent completion audit found no unresolved current-scope issues.
Receipts are in `artifacts/metadata-lifecycle/phase2/` (`*adoption-final*` and generator Debug/Release
receipts). Optional EXT slots absent on the test driver use deterministic native-boundary stand-ins;
these establish callback/effect delivery, not hardware EXT execution. Installed-game startup, appearance
and performance are not qualified. Remaining families retain their explicit pending migration scopes.

Synchronization lifecycle and combined prerequisite qualification passed canonical shader-enabled Debug
and ordinary Release builds, 231/231 focused tests per configuration with zero skips, and the unchanged
generator suite passed 35/35 per configuration. Second source review and independent completion audit
passed. Closed receipts are in artifacts/metadata-lifecycle/phase3/ (phase3-*-closed.trx and *-closed.log;
generator Debug/Release receipts). Actual query/fence and supported empty semaphore/memory
creation/disposal routes were exercised. External-producer FD/Win32 import effects use deterministic
native-boundary stand-ins; real cross-API payload imports, installed-game rendering and performance
are not qualified. Image, executable and remaining empty-schema families keep their later migration scopes.
