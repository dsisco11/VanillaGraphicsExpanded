# Targeted resource dependency lookup

Status: approved. Implementation and qualification are tracked in
[Rendering.GpuCommands.todo](Rendering.GpuCommands.todo), subphase 9.2.1. This design corrects
dependency lookup inside the existing manager without introducing a new resource system.

The current [resource publication contract](Rendering.GpuResources.md#snapshot-and-change-publication)
and [RHI ownership contracts](Rendering.GpuCommands.Contracts.md#authority-and-ownership) remain controlling.

## Reproduction

The CPU-only [ResourcePublicationTraversalTests](../VanillaGraphicsExpanded.Tests/Unit/Rendering/ResourcePublicationTraversalTests.cs)
creates one VAO registration and unrelated buffer registrations, then publishes the same VAO facts eight
times. A scoped, test-only Harmony probe counts actual iterations of the existing recovery loop.
It adds no production counters, native GL work, timing threshold or game launch.

Observed on source commit `d3d652a3`, with the reproduction test added:

| Unrelated registrations | Successful equal publications | Total registry visits | Unrelated visits |
| ---: | ---: | ---: | ---: |
| 0 | 8 | 8 | 0 |
| 1,024 | 8 | 8,200 | 8,192 |
| 32,768 | 8 | 262,152 | 262,144 |

Every publication returned with usable, committed metadata, completion exclusion released and metadata
revision still one. The zero-unrelated-visits assertion passes for the control and fails for both
populated cases. These were intentional failing regression cases for that baseline, not a passing qualification claim.
For that reproduction, one ordinary Release build and only these three cases were run. Exact output and TRX are in
`artifacts/resource-publication-traversal/release-traversal.{log,trx}`.

The probe observes the current loop implementation. Adapt or replace this test-only capture when that
loop is removed; do not preserve the loop or introduce production instrumentation to satisfy the helper.

## Implementation qualification

Subphase 9.2.1 implements the approved lookup with Graffs 0.2.0. The updated reproduction observes
zero unrelated visits for every population above, including one actual dependent among the unrelated
registrations. Release focused qualification passed 144/144 cases plus 18/18 creation cases after an
audit-discovered exceptional cleanup correction. Exact receipts are recorded in the implementation plan.
This establishes targeted dependency traversal, not user-run loading or final integrated acceptance.

## Cause and affected paths

`PublishMetadataCore` commits the entry successfully, then unconditionally calls
`RecoverResourceDependencies`. That method searches every registry entry for textures backed by the
published entry. This also happens for equal publications and empty-schema VAO configuration.

For P publications and N registered objects, that recovery search alone performs P times N visits.
Repeated creation while N grows can produce quadratic total work. This reproduces the expensive
successful-publication mechanism; it does not establish an infinite retry or attribute every part of
the observed loading-screen stall.

| Existing path | Current search | Required lookup |
| --- | --- | --- |
| `DependencyAvailability.RecoverResourceDependencies` | Entire registry after publication | This original source's withdrawn dependents |
| `DependencyAvailability.WithdrawResourceDependencies` | Entire registry during withdrawal/retirement | This original source's dependents |
| `BufferTextureDependencies.PublishBufferTextureDependencies` | Entire registry after buffer storage replacement | Textures backed by this original buffer |
| `RenderbufferTextureDependencies.PublishRenderbufferTextureDependencies` | Entire registry after renderbuffer storage replacement | Textures backed by this original renderbuffer |
| `NativeTextureViews.WithdrawChangedTextureViews` | Entire registry plus each view's source chain | Views descending from this original source |
| `DeletionDependencies.InvalidateDeletionDependencies` | Entire registry after native deletion | Affected texture/framebuffer relationships |

The complete registry sweep during terminal context retirement is legitimate lifecycle work and remains.
Owner/image fallback already uses targeted weak owner relationships and does not need another registry.

## Proposed correction

Maintain a reverse dependency index inside `RHIResourceManager` using Graffs 0.2.0's
`MutableDirectedGraph<GpuResourceRegistration>` and reference equality:

```text
original source registration -> directly dependent registrations
```

This is derived lookup data over the existing registrations and associations. It owns no native names,
canonical metadata, availability, revisions or disposal authority. Key by original registration identity,
so a reused numeric name cannot select another lifetime's dependents. Allocate a dependent set only
for registrations participating in relationships. Ordinary publication with no dependents performs a direct
lookup and zero unrelated-entry visits.

The existing `textureBackingIdentities` remains the forward association for buffer textures,
renderbuffer-backed textures and native views. Centralize setting/removing those associations so both
directions stay consistent. Framebuffer reverse membership derives from canonical attachment descriptors
and their `OriginalBacking` registrations. Deduplicate a framebuffer's sources: detaching one of two
slots referencing the same source must not remove the surviving relationship.

Use Graffs' cached immutable neighborhood queries through `IDirectedGraph<T>` for stable traversal;
do not build whole-graph snapshots or query `Nodes`/`Edges` during publication.
Reconcile associations in the existing publication completion before dependency fanout and callbacks.
Keep snapshot assignment, meaningful-change classification and notification ordering intact. An equal
publication can still assign missing recipients; removing global searches must not become an early
return from the whole publication operation.

## Lifecycle and uncertainty rules

- Withdrawal retains relationships needed for recovery. Recover only source-caused withdrawal;
  preserve independent child uncertainty and native-view storage revision checks.
- Traverse nested views through direct source relationships. Reuse the current completion batch and
  callback coalescing; add no public event bus, scheduler or general graph execution framework.
- Retargeting/detaching a dependent removes its old reverse membership. Terminal dependent cleanup
  removes obsolete membership without making old borrowers or views follow replacements.
- Source retirement closes usability immediately. Preserve relationships needed for the later actual
  native deletion outcome, including bound-framebuffer auto-detachment; do not clear that information
  merely because deletion was queued. Final deletion and context teardown clear obsolete lookup data.
- Use the existing originating-context handoff for off-thread retirement. The index must not retain
  wrappers or introduce worker-thread callbacks or native work in replacement contexts.
- Stable traversal must tolerate association changes during nested completion. Continue checking
  original identity, revisions and availability before delivery, as the current contract requires.

Some retained descriptors have no captured original backing. Preserve their conservative deletion
behavior with a weak subset of unresolved dependent registrations, consulted only for that exceptional
deletion case. Its work scales with unresolved relationships. Do not invent a source registration,
authorize a query, or automatically attach an unresolved descriptor to a subsequently reused name.
An existing legitimate association/discovery operation can move it into the indexed set when identity
is established. Covered ordinary publications never search this subset or the entire registry.

## Scope and verification

Replace all dependency searches in the table together, including association removal and retirement.
Retain the single registry and the existing public RHI/resource APIs. No blanket cache invalidation,
new native query, change suppression rule or special VAO bypass is required.

Scanning only the existing backing table would reduce work when it is small, but still searches every
relationship for each source. Direct source lookup gives the intended cost even when dependency counts grow.

The reproduction becomes green when unrelated registrations are no longer visited. Extend the focused
structural checks to cover one real dependent amid unrelated registrations, source-only recovery versus
independent child failure, retargeting/detachment, nested views, multiple framebuffer slots, source
retirement followed by native deletion, name reuse and context teardown. Preserve existing output,
revision, callback order and no-query assertions in the affected dependency/lifetime tests.

Use a build and narrow CPU/native integration selections for implementation verification, reusing
applicable existing evidence. No timing benchmark or default complete-suite rerun is required for this
lookup correction. User-run loading is still needed to confirm the effect on the original game symptom.
