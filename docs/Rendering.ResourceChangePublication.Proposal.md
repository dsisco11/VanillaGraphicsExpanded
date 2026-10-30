# Resource snapshot and change publication

Status: approved. Implementation plan: [Rendering.ResourceChangePublication.todo](Rendering.ResourceChangePublication.todo).
Design for
[Rendering.GpuCommands.todo, 9.2](Rendering.GpuCommands.todo).

## Contract

`RHIResourceManager` remains the sole authority for resource facts, availability and revisions.
Publication has two responsibilities: deliver the current snapshot, then report meaningful changes.
Receiving an equivalent snapshot does not itself mean that the resource changed.

Use one internal change contract for all resource families. Existing wrappers and dependency owners
react to that contract; there is no separate category of “derived-state event.” Their cached layouts,
attachment references and image projections are consequences of resource changes.

This refines the publication contract in [RHI contracts](Rendering.GpuCommands.Contracts.md) and
[resource ownership](Rendering.GpuResources.md), under the [RHI proposal](Rendering.GpuCommands.Proposal.md).
Preserve [prepared resource submission](Rendering.ShaderBindingState.md#prepared-resource-submission),
original registration identities, explicit recovery, requested-value suppression and the existing
query policy. No extra native query, registry, resource hierarchy or public event bus is proposed.

## Completion ownership

The implemented current-state contract is [resource publication](Rendering.GpuResources.md#snapshot-and-change-publication).
Entry classification, manager dependency completion and snapshot assignment precede the single internal
resource callback. Program preparation and framebuffer attachment helpers consume that callback;
explicit owner events retain their separate owner-completion meaning. Canonical entries own tracked
availability and revisions; weak owner/image relationships cover intentional replacement and local tools.

## One completion result

Replace the split notification routes with an internal `RHIResourceChange` value delivered through
one manager-owned completion path. It carries change reasons, resulting availability and the existing
revision values. Delivery is associated with the original registration, never a fresh lookup by native ID.

The reasons are `Metadata`, `Storage`, `Executable`, `Availability` and `Retired` in a project-owned
`ResourceChangeKind` flags enum. Reasons describe established changes, not commands that merely ran.

| Situation | Result |
| --- | --- |
| First complete publication | Assign snapshots; report initial metadata/availability. Reserved names remain unavailable until their kind's realization rules succeed. |
| Identical complete republication | Deliver to newly associated or missing recipients as needed; no change notification or revision increment. |
| Descriptor or selection changed | Report `Metadata`, including changed original backing identity even if its native name matches. |
| Storage replaced with the same dimensions/format | Report `Storage` regardless of descriptor equality. Preserve existing storage revision and view/backing invalidation rules. |
| Program executable replaced with an identical interface | Report `Executable` regardless of interface equality; preserve executable-generation rejection and rebuild preparation. |
| Facts become unavailable or recover | Report `Availability` even when the recovered descriptor is identical. Consumers may not reuse unavailable preparation. |
| Resource retirement | Report `Retired` and loss of availability once for the original lifetime. Later deletion of an already retired resource does not repeat that terminal event. |
| Upload, clear, draw or other content-only work | No metadata event. Existing content-readiness, query-result and synchronization services retain their own responsibilities. |

Existing `GpuMetadataChange` inputs become evidence for classification: explicit storage/executable
effects must survive equality checks; a generic “metadata may have changed” flag is not proof of change.
Preserve accumulated uncertain effects through explicit recovery. A recovery read must not erase a
possible storage replacement merely because its resulting dimensions match.

Use the existing `MetadataRevision` as the registration's change revision: advance once for an actual
published transition, including availability and terminal transitions. Advance `StorageRevision` only
for established or conservatively recovered storage replacement. Keep existing executable generation
ownership; do not introduce another generation token. Update revision consumers together.

## Equality and resource coverage

Make canonical record equality reflect its represented values. For collection-bearing records,
implement matching structural equality and hashing rather than adding `HasSameConfiguration` variants.
Dictionary insertion order and immutable collection allocation identity are irrelevant. Texture images
compare by mip/face identity; program resources by interface/index and unit assignments by location.
Default/unavailable collections must not be silently equated to a known empty interface.

Equality covers facts. It cannot establish that a native operation was accepted or that storage or an
executable was preserved. Original backing registrations and their established storage certificates
remain part of dependency classification even where they live outside the metadata struct.

| Resource family and canonical struct | Changes and consumers |
| --- | --- |
| Buffer — `GpuBufferMetadata` | Capacity/usage/storage flags; storage replacement; backing texture ranges and recorded resource validation. |
| Texture, buffer texture, native texture view, renderbuffer-backed texture — `GpuTextureMetadata` | Images, base/max levels and associations; source storage/availability; selected `GpuImageMetadata` projections, views and binding validation. |
| Renderbuffer — `GpuRenderbufferMetadata` | Extent/format/samples and storage replacement; image projections and associated textures. |
| Framebuffer — `GpuFramebufferMetadata` | Attachment selections/defaults, original backing identity and availability; retained attachment helpers and pass consumers. |
| Shader — `GpuShaderMetadata` | Stage facts and lifetime. Compilation results retain their existing result contract. |
| Program — `GpuProgramMetadata` | Executable/interface, sampler/image-unit and block assignments; prepared binding layouts and immutable pipeline generation checks. |
| Vertex array — `GpuVertexArrayMetadata`; transform feedback — `GpuTransformFeedbackMetadata`; sampler — `GpuSamplerMetadata`; program pipeline — `GpuProgramPipelineMetadata` | Currently empty canonical schemas: realization, availability and retirement. Attribute/binding/sampler configuration already owned by backend state knowledge stays there; do not invent duplicate metadata merely to emit events. |
| Query — `GpuQueryMetadata`; fence — `GpuSyncMetadata`; semaphore — `GpuSemaphoreMetadata`; memory object — `GpuMemoryObjectMetadata` | Currently empty schemas: established realization/import usability, availability and retirement. Query execution revisions, result readiness and opaque payload outcome checks retain their existing owners. |

`GpuImageMetadata` is a projection, not a fifteenth registered resource kind. Propagate both changed
projection values and backing storage/availability changes; equal width/height alone cannot suppress them.

## Publication and delivery order

1. Admit the original resource/context and classify the candidate against the last committed facts,
   prior externally visible availability and explicit operation effects. Temporary operation exclusion
   is not itself an unavailable/available event pair for every successful command.
2. Commit complete facts, revisions and dependency associations. Assign wrapper snapshots and affected
   image projections before calling consumer code. Snapshot assignment is a manager-internal operation,
   renamed from `HandleMetadataChanged` to make its limited purpose clear.
3. Reconcile dependent registrations through the same classification path, preserving original backing
   associations. Finish each affected dependency chain's facts before its consumer callbacks run.
4. Finalize the operation's committed outcome, then deliver one `RHIResourceChange` per affected recipient
   for that transition through an internal resource callback. Wrappers without derived work need no
   handler. Family-specific handlers invalidate only what the reported reasons affect.

All producers use this path: owned composition, intercepted single calls, program assignment updates,
explicit discovery/recovery and deletion/context retirement. Context-local delivery uses the same
classification rules with its existing owner snapshots; it does not acquire a second persistent registry.

Keep weak recipient ownership. Preserve the existing owner/image fallback for intentional owner
replacement, but feed it through the same completion semantics and exclude duplicate entry delivery.
An owning wrapper can follow its explicit replacement; borrowers and native views retain their original
association and cannot silently follow a reused name.

Explicit framebuffer resize/rebuild/import completion remains an owner-level publication event, including
when selections are identical. Carry the initiating owner through completion so that its explicit event
and metadata reaction do not both emit the same completion notification. Other aliases still receive
the resource change. This is the same origin policy for owned operations, not a framebuffer field in
generic operation state.

Consumers may read committed facts during notification. Managed same-resource mutation remains subject
to the existing operation/reentry guard. If a callback causes an unavoidable intercepted mutation or
retirement, recheck original identity, revision and availability before further delivery; do not deliver
an obsolete completion over newer facts. Do not veto the native call or add asynchronous event scheduling.

Attempt delivery to remaining valid recipients after a consumer failure. Preserve the native operation's
original exception on failure paths; interception contains observation exceptions. A callback exception
after commit must never make cleanup treat successful storage as an uncommitted allocation or retry GL.
Retirement closes usability immediately, then completes CPU-side notification using captured recipients
before clearing them; no GL calls occur in a replacement context. Off-thread disposal must use the
existing lifecycle handoff for context-affine consumer work rather than executing it on the disposing thread.

## Implementation and verification

Migrate the existing publication, withdrawal, owner-image, program, framebuffer and retirement callers
together. Remove their superseded notification entry points in the same change; no forwarding shims.
Keep native state suppression separate and do not expand the canonical schemas of empty-metadata kinds.

Use CPU-only registry tests for equality, revision classification, delivery ordering, recipient failures,
reentry and terminal events. Add focused GPU integration cases only for native interception, same-shape
storage replacement, executable replacement and original backing/name reuse. Preserve existing explicit
owner-event checks. No full-suite rerun or pixel-buffer scan is needed to validate this design step.

Resource/binding contracts describe the implementation. The implementation plan retains focused
verification and independent review receipts; integrated qualification remains in parent 9.3.
