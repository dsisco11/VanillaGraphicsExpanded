# Maintained resource metadata lifecycle

Status: approved; implementation is governed by
[the dedicated lifecycle plan](Rendering.MetadataLifecycle.todo).
Completion feeds subphase 8i of [the RHI implementation plan](Rendering.GpuCommands.todo). It extends the
[RHI proposal](Rendering.GpuCommands.Proposal.md),
[ownership contracts](Rendering.GpuCommands.Contracts.md) and
[native interception design](Rendering.OpenTKBindingInterception.Proposal.md).
The lifecycle is implemented; combined qualification and its limits are recorded in the dedicated plan.

## One authority, maintained from creation

Keep `GpuResourceManager` and its existing typed registry entries as the single canonical metadata
authority. Keep `OpenGlStateCache` as the executed-state authority. Resource wrappers receive complete
snapshots; references and consumers neither query nor repair them.

External calls execute unchanged once, followed by observation. Creation observers consume returned
names and results while native output pointers are still valid. Configuration observers pass supplied
facts to manager publication rather than discarding them and invoking readers. Backend-owned RHI
factories continue using saved-original dispatch and their existing private manager handoff, so they
do not also receive external creation publication.

Do not add metadata groups, a second requested/actual snapshot, a parallel registry, wrapper-local
tracking, a generic event bus or a replacement transaction framework.

## Identity and availability

Use the existing registration object as allocation identity. Add the distinction between a reserved
name and a realized object to that registration, separately from metadata availability and retirement.
The factory/realization rules are specific to each resource kind: a `Gen*` spelling alone does not
imply reservation for every kind, and `Create*` does not imply allocated image or buffer storage.

| Event | Canonical action |
| --- | --- |
| Generated reserved name | Retain its kind and original identity, without publishing a usable object snapshot. |
| Creation or later legal realization | Establish the object type and guaranteed represented defaults; publish a complete snapshot where the schema permits it. |
| Storage/configuration command | Apply its represented effect to the same entry; preserve unrelated facts and publish through the existing manager path. |
| Content-only operation | Preserve descriptor availability and revisions unless it actually defines or generates new image storage. |
| Uncertain represented effect | Withdraw affected certification; do not silently retain stale facts or launch a full descriptor scan. |
| Deletion or context retirement | Invalidate the original entry and its dependents through existing retirement handling. |
| Reused numeric name | Create a different registration after the old allocation is retired; old wrappers and dependencies never retarget. |

Zero results, invalid output counts and detected failed creation do not register an object. An observed
factory must not overwrite an existing live allocation because an output array contains an old value.
Classify applicable native preconditions before admitting output identities. This retains the existing
limitation that neither a void return nor an unchecked output buffer universally proves native success.

Keep the existing complete-snapshot availability contract. A reservation, incomplete adoption or
unresolved effect is not a usable default struct. Do not publish partial snapshots as complete or add
per-property knowledge masks to every metadata type. Preserve already-known data privately in the
same entry where it is needed to construct the next complete candidate.

The existing manager belongs to its originating context lifetime. Context-local objects never cross
that boundary. Native shareability does not make equal numeric names in different contexts equivalent;
this design adds no shared-context execution or share-group registry. Any future shared-context support
must explicitly establish that ownership rather than guessing it from names. Context-local tools retain
their existing explicit RHI initialization/adoption path without a second registry.
Absence of an installed manager alone does not justify rediscovering facts already supplied during
initialization; those tools use their existing supplied-snapshot handoff.

## Resource families and canonical facts

Only fields required by existing consumers belong in canonical metadata. Defaults are resource-specific
OpenGL guarantees, established explicitly rather than inferred from C# `default`.

| Resources | Canonical structs | Maintained facts and remaining outputs |
| --- | --- | --- |
| `GpuBufferObject`, `GpuVbo`, `GpuEbo`, `GpuUniformBuffer`, `GpuShaderStorageBuffer`, `GpuAtomicCounterBuffer`, `GpuIndirectBuffer`, `GpuPixelPackBuffer`, `GpuPixelUnpackBuffer` | `GpuBufferMetadata` | Realization defaults; capacity, usage, mutable/immutable mode and storage flags from allocation. Binding and mapping/activity knowledge stays with its existing execution owner; role and content extent stay wrapper policy. |
| `GpuTexture` and its owned subclasses, `BorrowedTexture`, `GpuTextureView`, `GpuBufferTexture`, `GpuBufferView` | `GpuTextureMetadata`; selected-image projection `GpuImageMetadata` | Target established at creation/realization; unallocated images and represented parameter defaults; dimensions, format, mips/faces/layers, storage mode, view ranges and backing associations from commands. Derive immutable mip geometry and mutable mip generation from known base images. Subimage uploads do not change descriptors. Resolve unsized formats and samples only under the narrow policies below. |
| Renderbuffer-backed texture associations and `GpuFramebufferAttachment` | Existing backing `GpuTextureMetadata` or `GpuRenderbufferMetadata`; projected `GpuImageMetadata` | Retain original backing identities and storage dependencies. Project selected images from known backing facts without another canonical image registration. |
| `GpuRenderbuffer` | `GpuRenderbufferMetadata` | Empty storage defaults; extent and requested format from storage commands; resolve samples as below. |
| `GpuFramebuffer`, `DynamicGpuFramebuffer`, `StaticGpuFramebuffer` | `GpuFramebufferMetadata` | Empty attachments and attachmentless defaults; apply individual attachment/subresource and default-parameter changes. Routing remains in the existing cache. Default framebuffer zero remains a surface, not a registered object. |
| `GpuShaderModule` | `GpuShaderMetadata` | Fixed shader stage from creation. Compilation status/logs remain driver-produced outputs, not invented metadata fields. |
| `GpuProgramObject`, `GpuProgramLayout` | `GpuProgramMetadata` | Fresh unlinked defaults; executable/interface results after link or binary installation; represented unit/block assignments patched from their commands. Link-input declarations affect the next executable, not the installed interface. Numeric uniforms do not refresh metadata. |
| `GpuProgramPipeline` | `GpuProgramPipelineMetadata` | Identity/default realization and configuration revisions; stage selection remains with existing execution knowledge. No new stage-layout mirror. |
| `GpuVao` | `GpuVertexArrayMetadata` | Identity/default realization and configuration revisions. Existing executed knowledge owns element bindings; authored geometry and original revision receipts drive private VAO realization without layout scans or a canonical layout mirror. |
| `GpuTransformFeedback` | `GpuTransformFeedbackMetadata` | Identity/default realization and relevant configuration revisions; binding/activity remains executed knowledge. |
| `GpuSampler` | `GpuSamplerMetadata` | Identity/default realization. Keep the empty canonical schema; sampler policy and any existing parameter knowledge retain their owners. No new parameter mirror solely for interception. |
| `GpuQuery`, `GpuFence`, `GpuSemaphore`, `GpuMemoryObject` | `GpuQueryMetadata`, `GpuSyncMetadata`, `GpuSemaphoreMetadata`, `GpuMemoryObjectMetadata` | Kind-specific creation/import identities and lifetime. Keep empty schemas; query/wait results and imported payloads do not become descriptor scans. |

Texture views retain a distinct object entry and the source allocation/storage dependency. A source
wrapper's intentional replacement does not retarget an existing view. Whole-buffer texture views retain
their existing range convention and original buffer dependency. Changes publish to existing aliases and
image dependents once, with storage revisions only for represented allocation changes.

## Native outcomes and permitted reads

Use the approved request-based policy without a universal success detector:

- Proven rejection from arguments and established context/object facts preserves prior metadata.
- An ordinary valid request whose represented effect is established by supplied inputs/defaults/derivation
  updates the canonical snapshot. Do not query it back to compare driver-normalized values.
- An outcome that cannot be established withdraws its affected certification. Record the specific
  unresolved fact and consumer before adding a targeted result read; do not infer success solely because
  a void call returned. Detected failures never publish success.

This policy does not promise detection of every silent allocation failure. Preserve native errors and
outputs; do not call `GetError`, probe every object with `Is*`, retry commands or introduce universal
post-call verification. Optional-feature/limit classification uses previously captured facts, not
capability discovery inside callbacks.

Borrowing or adopting means attaching a wrapper to an existing native object; it does not imply
metadata discovery. Resources created and configured under active interception already have maintained
facts, which later wrappers reuse without queries. An external-memory or interoperability handle import
alone does not justify descriptor discovery: subsequent intercepted storage/configuration commands
normally supply the represented dimensions, format and other allocation facts.

Permitted acquisition has an explicit cause:

1. Explicit initial discovery of an object created before hook installation, whose earlier setup was
   therefore not observed. An unknown identity first encountered through a supported EXT named operation
   with implicit-creation semantics also permits one discovery: that operation may have initialized an
   unused name or accessed an older object. Record this distinct cause on the existing registration,
   without publishing defaults or querying in the observer. Acquire the required facts only on explicit
   adoption; later available consumers reuse them. Failed discovery consumes the initial attempt and
   requires explicit recovery. Existing tracked reservations still realize from defaults without queries.
2. For external-memory texture storage, read `GL_TEXTURE_IMMUTABLE_FORMAT` once after the storage call
   when the texture was known mutable beforehand. The imported payload's acceptance cannot be established
   from the void command or opaque handle; borrowing, shader binding and framebuffer attachment consumers
   require an established allocation. The flag confirms the transition, while dimensions, format and levels
   come from the command. This is not a blanket imported-object descriptor query exception.
3. Driver-produced compilation/link results and executable reflection. Reflect after executable change,
   not after an ordinary unit/block assignment.
4. Resolved unsized image formats, and actual sample counts when cached target/format capabilities cannot
   determine them. Preserve the approved single sample-count fallback for renderbuffers and multisample
   textures; do not reread unrelated dimensions or image parameters.
5. A documented unresolved outcome or contained observer failure needing explicit recovery. Recover only
   the missing result needed by its consumer; retain existing original-identity and interruption checks.
6. Kind-specific deletion outcomes already required to distinguish name retirement from deferred native
   object destruction. These are lifecycle checks, not descriptor acquisition.

Semaphore generation is a name reservation, not an imported payload. An unimported RHI owner may be
disposed but cannot wait or signal. The managed import boundary performs one targeted `IsSemaphore`
outcome read before returning an imported usable resource: the missing fact is whether the void import
established a semaphore, and the consumers are wait/signal admission. This is an explicitly approved
lifecycle check, not imported descriptor discovery. External import observers issue no probe and do not
infer success from a handle; a caller's explicit positive `IsSemaphore` result can establish realization
on the original reservation. Query/wait results and semaphore payloads remain outside the empty schemas.

A missing registration or discarded command argument for a resource created under active interception
is a tracking defect, not an implicit acquisition request. Ordinary binding, validation, reference
conversion and snapshot access must neither hide that defect nor scan descriptors. Repair the producer.
The narrow EXT exception requires observation of an actual supported named realization boundary;
an arbitrary unknown ID, ordinary binding or caller-supplied label cannot grant discovery. It does not
assert whether the object predates installation or whether the requested update succeeded. Texture
discovery identifies the actual target independently of the intercepted command's potentially rejected target.
Adoption APIs must distinguish query-free association with maintained facts, explicit pre-installation
discovery, recorded EXT implicit-creation discovery and recovery of an existing unavailable identity. Neither wrapper construction nor a caller's
choice to call an object external or imported changes its metadata provenance.

## Migration and evidence

Before migrating each family, enumerate its exact factories, realization operations, configuration and
deletion aliases from the compiler-selected OpenTK bindings, including returned pointers and arrays.
Cover both GL roots and supported Core/Ext/Arb owners. Preserve legacy and auxiliary-owner exclusions.
Include extension imports/storage establishment and objects established by later binding operations.

Migrate each complete family together: observation, manager publication, defaults/derivations, readers,
consumers, aliases/dependencies, deletion and tests. Reuse existing publication and operation services;
remove superseded blanket withdrawal/readback paths in that unit without forwarding shims. Classify
irrelevant/rejected effects before interrupting a pending completion. Keep interruption protection only
where a remaining real reader or publication callback can invalidate an outer candidate.

Focused tests must exercise raw OpenTK and managed RHI creation through their real native routes with
descriptor readers configured to fail on unexpected reads. Cover delayed realization, aliases/views,
imports, failed commands, name reuse, pre-install adoption and context recreation. Verify original calls,
registration and publication occur once, binding suppression remains intact, and legitimate output reads
still occur. Delegate canonical Debug/ordinary Release validation; retain hardware gaps explicitly.
No benchmark, game launch, automatic staging or commit is part of this work.
