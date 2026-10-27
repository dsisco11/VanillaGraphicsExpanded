# Canonical GPU resource metadata and notifications

Status: approved direction, reconciled with the manager-owned metadata design. Implementation checklist:
[Rendering.GpuResourceHandles.todo](Rendering.GpuResourceHandles.todo). This is a design contract, not
an assertion that the runtime migration is implemented.

The approved command-publication refinement (`3f307365:docs/Rendering.CommandPublication.Proposal.md`) controls
creation and query policy: controlled inputs/defaults/derivations are authoritative under existing
failure handling; external unknown facts and specifically justified outputs are acquired. No universal
post-command verification or duplicate requested/actual metadata cache is required.

## Intent

Give owning and non-owning GPU wrappers canonical native metadata and a common notification mechanism.
`GpuResourceManager` owns the registry. OpenGlStateCache observes backend operations and forwards metadata
changes to the manager, which updates its canonical struct and publishes typed snapshots to wrappers.
Borrowed resources participate in this same mechanism, preserving normal binding caching.

## Current behavior

[GpuResource](../VanillaGraphicsExpanded/Rendering/GpuResource.cs) defines native identity, disposal,
ownership, usage guards, context capture and deletion scheduling. Resource wrappers derive from
`GpuResource<TMetadata>` and expose native facts through synchronized snapshots.
[GpuResourceManager](../VanillaGraphicsExpanded/Rendering/GpuResourceManager.cs) owns canonical entries,
weak wrapper associations, shared guards and queued retirement. [BorrowedTexture](../VanillaGraphicsExpanded/Rendering/BorrowedTexture.cs)
acquires initial metadata through that common registry without acquiring ownership.
[OpenGlStateCache](../VanillaGraphicsExpanded/Rendering/RHI/Backends/OpenGL/State/Observation/OpenGlStateCache.ResourceDeletion.Publication.cs) observes
supported native deletion commands and preserves their binding/restoration effects.
[EngineStateCallMap](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateCallMap.cs) redirects exact
supported GL signatures within the owner boundary below. Cloud and depth consumers retain non-owning
typed wrappers for stable native selections and share owned material depth directly. Integrated
qualification and its evidence limits are recorded in the implementation checklist.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| GpuResourceManager | Context-scoped registry, canonical metadata, registration validity, shared existing guard state, snapshot publication and deferred deletion. |
| GpuResource | Native identity, wrapper-local ownership/disposal and existing deletion scheduling contract. No metadata members or notification contract. |
| GpuResource<TMetadata> | Private snapshot, protected read-only access and public HandleMetadataChanged(TMetadata data) that assigns the snapshot. No registration, availability, revision or subscription state. |
| Typed resource wrappers | Resource-specific operations and metadata accessors; authored policy remains local. |
| OpenGlStateCache | Observes established backend effects and forwards changes to the manager; retains binding/restoration knowledge. Native execution belongs to the RHI backend. |
| Rendering integrations | Resource selection, producer content readiness and frame qualification. |

The readonly value `GpuResourceHandle` contains resource kind and native-width ID as a registry key.
It carries no ownership or lifetime state. There is no separate `GpuResourceLifetime` or borrowed-resource
registry; registration membership and the entries used by queued deletion stay inside the manager.

## Canonical metadata and registration

Each resource kind has a canonical struct. The manager owns one `ConcurrentDictionary<GpuResourceHandle,
GpuResourceRegistration>` for its context lifetime. Entries retain canonical metadata and a concurrent
collection of weak resource recipients. No context generation or incarnation counter belongs in the
resource key. Existing context checks remain at manager selection/native execution.

Wrappers of the same native object receive snapshots of the same canonical metadata. Deletion removes
its entry, invalidates associated wrappers and cancels obsolete queued deletion. A reused numeric ID
receives a fresh entry. Old wrappers never regain validity. Pending work retains its original entry
association and never reacquires by numeric ID at drain time. Context retirement discards the registry.
Registration neither grants native ownership nor deletes storage when a reference is released.

`GpuResource<TMetadata> : GpuResource where TMetadata : struct` owns a private snapshot and exposes it
to derived wrappers as `protected ref readonly TMetadata metadata`. Its public non-virtual
`HandleMetadataChanged(TMetadata data)` method assigns the complete private snapshot.
Existing resource abstractions inherit this base directly or through their existing typed superclass.
Native metadata properties are getter-only accessors. Derived wrappers cannot edit the snapshot.
Requested dimensions, formats and allocation hints remain command arguments or separate declaration policy.
The RHI backend executes metadata-changing commands and forwards established results through OpenGlStateCache to the manager; wrappers
do not capture, construct or publish replacement metadata. Initial acquisition is manager-owned, including
context-local tools without an installed registry. The generic base handles snapshot assignment;
the non-generic base has no metadata responsibilities. The manager owns registration validity,
availability, revisions and subscription membership. Metadata-dependent validation asks the manager
about the wrapper's original association; neither base stores that association or runs a tracking state machine.
Ownership, disposal, authored policy and selected subresources remain local.
Sampling, growth, streaming and attachment selection remain wrapper/helper policy. Manager creation
orchestrates initialization records and complete allocation operations through private RHI execution; existing
upload helpers retain policy while the admitted OpenGL 4.5 backend owns core native execution.
Backend operations explicitly compose their internal steps and supply a complete result for manager
publication. Interruption leaves affected metadata unavailable. The command-publication proposal defines
the operation-owned commit boundary and rejection of unrelated same-resource reentry.

The [inventory](Rendering.GpuResourceHandles.Inventory.md#canonical-metadata-schemas-and-field-migration)
specifies structs, field migration and the image projection for attachments that can reference textures
or renderbuffers. Collections inside snapshots are immutable after publication. Programs keep native
interface facts separate from authored preparation policy. Types without retained native metadata use
an empty per-kind struct. Default framebuffer zero uses existing surface publication, not a registry entry.

External acquisition validates kind and captures initial metadata once. Existing entries supply subsequent
snapshots without repeated queries. Created names may register before storage initialization, with metadata
unavailable until established. CPU declarations register only upon native realization. Unsupported external
mutation/deletion paths retain conservative handling; registration alone cannot promise continuing validity.
No producer-specific allocation certificate is needed for a live object with covered subsequent commands.

Recipients are weak and explicitly removed on disposal. Use a concurrent dictionary as a removable
recipient set; no per-wrapper association object or projection delegate is needed. Registration lookup
and recipient removal can run on other threads. Native acquisition, association initialization and
metadata publication remain on the originating context thread. Prune expired references on acquisition,
publication, unregister and teardown. Entries persist until native deletion or context retirement even
when wrappers are collected. Shared-context namespace generalization remains outside scope.
The entry finds recipients by wrapper identity for explicit removal; wrappers retain neither a weak slot
nor a registration back-reference. The manager retains original association identity through weak keys,
so assigning snapshot data or reusing a numeric name cannot restore a retired association.

## Ownership, retirement and guards

Owning abstractions retain deletion authority and the existing deletion/scheduling flow. Borrowed disposal
only removes that wrapper's association and local state; it does not delete, invalidate other aliases or
unbind native storage. Owner retirement makes the entry unavailable to managed use while queued deletion
waits for its original context. Old queued work cannot delete a replacement with the same numeric ID.
Observed external deletion also prevents subsequent owner disposal from deleting a replacement.

`Detach` transfers ownership and removes the transferring wrapper's association while preserving the
live entry for other aliases. Borrowed transfer stays rejected. Native replacement registers the new
object rather than retargeting old aliases. Context retirement issues no GL deletion into another context.

Existing pass/lifetime checks and releases remain authoritative. Move only alias-wide counters into the
manager entry; declaration-only counters remain local until realization. Do not duplicate pass/lifetime guards at the native adapter boundary. The command-publication
contract separately requires rejecting unrelated reentry while an explicit operation owns its result. Preserve static framebuffer locks and existing retirement scheduling.

## Metadata publication and validation

OpenGlStateCache forwards established backend metadata effects to the manager. Controlled commands build a complete
candidate from inputs/defaults/derived facts; readers acquire external unknown state and justified outputs.
The manager commits that candidate and assigns snapshots to all associated wrappers.
Wrappers do not independently query native metadata. Generic notification is only assignment; manager
publication maintains availability and revisions and coordinates existing dependent observers outside
that method. Pending owner retirement, deletion and context retirement make the original association
unusable in the manager; refresh failures leave its metadata unavailable until a complete recovery.

Publish after native operations using the approved controlled-input policy. Preserve existing error
handling; reported rejection must not publish success. This adds no universal detector for silent GL
failures. Unknown external effects use invalidation/recovery without consuming errors. Failed refresh leaves affected
metadata unavailable. Assign snapshots before exposing availability and notifying dependents; one failure
must not silently leave other affected consumers presenting stale data as current.

Surviving-object metadata changes preserve registration but advance appropriate metadata/storage revisions.
Existing shader input validation and prepared executable/attachment publication must account for these
revisions. Keep complete-input validation before resource publication and reuse the existing validation
history. A valid registration does not imply that the native resource is bound.

## Command coverage and consumers

Interception is restricted to the managed native owners GL, GL.Ext and GL.Arb.
The pinned OpenTK binding exposes core methods directly on GL; adapter classes named Core
refer to that owner. All other auxiliary owners are excluded, including Amd, Ati, Ibm, Intel,
Mesa, Oes, Ovr, Sgis, Sun, Apple and NV. This is an owner boundary, not a filter on feature origins,
command suffixes, extension capability checks or direct VGE resource operations.
Unmapped calls execute unchanged and do not provide continuing metadata or lifecycle observation.

All applicable overloads on the allowed owners remain in scope, including vendor-origin features.
Legacy pre-OpenGL 3.0 fixed-function/compatibility facilities are also excluded: matrix stacks,
display lists, server/client attribute stacks, client default resets and legacy client-array state.
Alpha testing, point smoothing, separate polygon face modes, line/polygon stipple and automatic mip generation are excluded too.
Earlier introduction alone does not exclude commands retained
by modern OpenGL, including ordinary texture/buffer operations, generic vertex attributes and
depth/blend state. These exclusions do not apply to the separate GpuCommandList RHI design.

The [API matrix](Rendering.GpuResourceHandles.ApiCoverage.md) covers ALL OpenGL commands capable of changing
represented metadata, independent of current callers: bound, DSA, multi-object, extension and managed
overload variants. Bound commands use existing unknown-state reconciliation; explicit commands use their
named objects/units. All deletion kinds and multi-resource deletion lists use the common notification route. Preserve
command ordering, actual binding effects and per-object failures. Missing binding APIs require explicit
adapter dispositions. Capability-limited hardware does not waive implementation support.

Only metadata effects belong here. Content uploads, readiness and frame qualification retain their
existing contracts. A command that also defines storage contributes that metadata effect. Preserve engine
boundary restoration, texture/image binding knowledge and independent routing caches. VGE direct calls
use the same notification route; no producer teardown hook duplicates observed GL deletion.

Retain cloud-map and native depth wrappers across stable lifetimes once their required commands are
covered. Share owned material depth directly. Migrate eligible raw sampler inputs to typed wrappers and
ordinary binding caching. Unsupported raw paths remain conservative. Preserve world/owner selection,
cloud publication, upload completion and consumer disposal authority. Keep allocation-hook observations
needed for content certificates while removing redundant lifecycle-only work.

## Layout and acceptance

Keep metadata schemas, manager registration/publication, wrapper operations and native adapters in files
with separate responsibilities. Preserve render order, shader algorithms, ownership and source conventions.
No second binding/validation cache or parallel lifetime registry is introduced. Permanent architecture
documents describe implemented behavior when migration occurs.

Acceptance requires canonical metadata and coherent snapshots for every existing wrapper, correct
ownership/detach/guard/queue/context behavior, deletion/name-reuse safety and bounded weak associations.
Verify all API matrix rows, failed commands, unavailable metadata, alias publication, dependent revalidation,
unknown state and stable binding suppression through actual native redirection where capabilities permit.
Explicitly record unexecuted native capability cases rather than claiming full native qualification.

Compare actual metadata queries/native binds and outputs/restored state with the retained baseline.
Measure registration/publication/retirement costs separately. Subagents run builds/tests and canonical
package checks. Remove temporary ResourceCallObserver tooling after matched measurements, retain replay
receipts, and verify the clean suite/package. Headless evidence proves neither live appearance nor frame-time
improvements; game visual acceptance remains user-run.
