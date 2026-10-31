# Owner-guaranteed resource lifetimes and change-driven execution

Status: proposed; awaiting review. This document does not authorize implementation or define an implementation task list.

## Objective

Remove repeated resource validation and lifetime bookkeeping from rendering by establishing correctness at
resource creation, operation preparation and owner-controlled lifecycle boundaries. Apply this design across
the resource system: shaders, pipelines, bindings, geometry, passes, transfers, synchronization and retirement.
Shader binding motivates the redesign but does not limit its scope.

Owners guarantee that resources remain alive and suitably configured for their consumers' required use
intervals. Consumers use non-owning resource references. Rendering relies on that contract instead of
re-proving every resource's lifetime, metadata and compatibility on every draw.

This changes the current defensive execution contract. Ordinary Release execution is not required to detect
and reject every owner/consumer contract violation. Development validation diagnoses such violations;
correct production behavior comes from compliant ownership and operation scheduling. Operational failures
and genuinely dynamic inputs still require handling.

## Current behavior and document boundaries

The current contracts are described in [RHI submission](Rendering.GpuCommands.Contracts.md#submission-and-failure-rules),
[prepared bindings](Rendering.ShaderBindingState.md#prepared-resource-submission),
[resource lifecycle and publication](Rendering.GpuResources.md), and the
[RHI proposal](Rendering.GpuCommands.Proposal.md). This proposal specifies changes to those contracts;
they must be reconciled when implementation is approved, rather than treated as descriptions of an already
implemented design.

The binding path currently includes:

- `OpenGLBackend.ResourceBindings.BeginBindingExecution`, validating and retaining active inputs.
- `OpenGLBackend.ResourceBindingValidation`, `ShaderPreparedSubmission` and `ShaderInputValidation`,
  repeatedly consulting resource availability, compatibility and buffer metadata.
- `OpenGLBindingPlan` and `OpenGLBackend.BindingReuse`, suppressing unchanged native publication while
  retaining consumer-side validation and uniform candidate/retention/commit walks.
- `OpenGlStateCache.BindingPublication`, expiring broad publication receipts on external observation.

The problem is the repeated execution policy around resource references, not their non-owning semantics.
The other operation families require the same architectural review; this binding inventory is not a claim
that every family currently has identical duplication.

## Ownership and non-owning references

`GpuResourceRef` denotes the consumer-reference concept here; existing concrete types include
`GpuTextureRef` and `GpuBufferRef`. These references must remain non-owning. Holding or copying one does not
acquire native ownership, prevent disposal, reserve storage, expose mutation or transfer lifecycle authority.
Keeping a managed wrapper reachable is distinct from keeping its native resource alive.

The resource owner is responsible for:

- Successful initialization before exposing the resource to consumers.
- Keeping the referenced resource and required configuration valid throughout its declared use interval.
- Scheduling incompatible replacement, mutation and disposal outside that interval.
- Rebuilding or withdrawing affected prepared consumers before they execute again after a relevant change.
- Retiring native storage through the existing context and GPU-completion mechanisms.

A cached description may remain stored while inactive, but it must not execute after its dependencies are
retired or incompatibly changed without being rebuilt. An owner retaining a command for future execution
must either preserve its dependencies or withdraw the command before releasing them.

Recorded commands have an explicit owner responsibility: resources must remain valid until those commands
have executed or have been discarded. Recording a non-owning reference does not extend that lifetime.
An immediate command needs its resources through execution; a render pass needs its attachments throughout
the pass. Mapping and asynchronous operations retain the intervals defined by their owning APIs.
CPU command completion does not establish GPU completion: deferred deletion, allocator last-use records,
fences and completion tickets retain their separate responsibilities.

Worker disposal requests must be scheduled through the owning lifecycle so they cannot race rendering.
Reentrant callbacks must not replace or destroy resources being used by the enclosing operation. These are
caller/owner obligations, not reasons to acquire a guard for every resource on every draw. Development
checks may diagnose violations; they must not become required Release lifetime machinery.

## Architectural responsibilities

| Owner | Responsibility |
| --- | --- |
| Resource owner | Initialization, consumer lifetime, replacement scheduling and release |
| RHI resource manager | Canonical associations and metadata, resource-change publication, native retirement coordination |
| Typed resource/view/pipeline | Established description and family-specific configuration invariants |
| Renderer or operation owner | Prepared draw/binding/geometry/pass descriptions and their refresh or withdrawal |
| RHI command interface | Semantic commands, sequencing and dynamic operation arguments |
| Backend | Native realization, changed-state application and executed-state cache |
| Allocator/completion owner | Physical storage reuse, upload commitment and GPU completion |

Do not introduce manager-owned admitted dependency sets, execution groups, group activation locks or a
universal admission state machine. Do not replace per-resource guards with an equivalent group-based guard
system. Resource metadata and existing dependency publication remain useful; they do not need to become an
execution permission service.

Resource-specific preparation belongs with the component that understands the operation. Reusable draw
preparation belongs above backend execution, using backend-neutral RHI descriptions. Native targets,
handles and descriptor realization remain backend details. There is no universal validator or untyped
operation container that switches over all resource families.

## Establish facts once and reuse them

Separate three kinds of information:

| Information | Establishment and refresh |
| --- | --- |
| Resource facts | Creation, import and observed configuration/lifecycle changes publish canonical metadata |
| Operation compatibility | Preparation establishes the relationships required by selected resources, views, shaders and passes; relevant changes rebuild affected results |
| Executed state | The backend cache tracks what is actually bound and applies changed slots or state |

Metadata tracking is retained. Buffer sizes, texture formats, view ranges, executable interfaces and other
facts remain available for preparation, diagnostics and operations that need them. Unchanged execution must
not repeatedly resolve registrations or retrieve the same metadata simply to prove established facts again.

Preparation resolves authored selections, required/optional rules, shader activity, layouts and stable
compatibility relationships. Inactive shader inputs contribute no binding work. Optional absence remains
an explicit slot-clear operation. A preparation failure produces no usable prepared result.

Prepared state stores only required derived results and non-owning selections. It does not duplicate the
canonical descriptor registry or make raw native names proof of lifetime. Backend realization can retain
native selection data under the owner's lifetime contract, with refresh when its actual dependencies change.

No requirement exists to allocate a persistent plan for every upload or unique transfer region. One-off
operations check their bounded arguments at the call boundary. Repeated operations may retain preparation
where an existing owner can manage its lifetime and updates.

## Change-driven updates

Use existing resource-change publication and dependency relationships to update consumers that actually
cache derived information. Add narrowly scoped consumer subscriptions only where needed; there is no
mandatory dependency node for every command or resource use.

At an owner-controlled change boundary, publish coherent resource facts and mark or rebuild affected
prepared state before subsequent execution. Changes to storage, attachments, views, shader interfaces or
native realization can require refresh. Ordinary content writes do not invalidate descriptor compatibility.
Their synchronization remains the caller's responsibility.

Refresh related facts together where needed, such as framebuffer attachment compatibility. Unrelated
resources and consumers must not be visited. Failed refresh withdraws the affected prepared operation;
it must not execute using an obsolete successful result. Local/tool resources obey the same owner contract.

Preserve the distinction between an owner selection that deliberately follows replacement and a borrower
or view tied to an original association. Explicit owner replacement must refresh affected selections.
Native-name reuse must never silently retarget an original borrower. Configuration changes can require
refresh even when some canonical descriptor values compare equal.

Notifications maintain cached results for legal lifecycle changes. They are not a replacement for owner
scheduling, a check/use race solution, or a promise to rescue arbitrary concurrent disposal.

## Resource and operation coverage

| Family | Establish at creation/preparation/change | Remaining execution work |
| --- | --- | --- |
| Creation and import | Validate descriptions, establish associations and publish successful initialization | Native calls, observable outcome checks and failed-candidate cleanup |
| Storage/configuration | Owner schedules legal mutation; update metadata and dependent preparation | Requested mutation and failure handling |
| Shaders/executables/pipelines | Interface, layout, stage and fixed-state compatibility for the selected generation | Select prepared objects and apply changed state |
| Shader resources | Active inputs, views, ranges and shader/resource compatibility | Apply required bindings and changed uniform data |
| Geometry and indirect inputs | Stream, attribute and index layout; stable buffer relationships and reusable bounds | Dynamic draw parameters; indirect content remains producer-owned |
| Framebuffers and passes | Attachments, dimensions, samples, routing and pipeline/pass relationships | Pass sequencing and requested load/store/clear effects |
| Upload/copy/clear/mips | Stable resource/format eligibility and reusable regions where useful | New regions, payloads and requested native work |
| Mapping/query/synchronization/readback | Object ownership and structural eligibility | Mapping intervals, issued generations, polls/waits and actual completion |
| Retirement/context teardown | Owners withdraw consumers and schedule release | Context-correct destruction and completion-safe storage reuse |

Geometry preparation may derive fetch limits from streams, strides and divisors. Dynamic argument checks
must use those established facts rather than reconstructing attribute compatibility. The exact necessary
checks depend on the operation; this is not a requirement for a universal bounds scan on every draw.

Cached compatibility never means a query has completed, a resource is unmapped, contents are unchanged,
a clear can be skipped, or GPU synchronization is unnecessary. Feedback compatibility is refreshed when
resource selections, attachments or pipeline write state change. Synchronization remains explicit caller
intent; no automatic barrier insertion or hazard scheduler is introduced.

## Uniform data and allocation

Separate stable resource selections from captured uniform versions. New uniform bytes must not force
unchanged textures and buffers through preparation again. Capture remains CPU-only and retains the required
version semantics for recorded commands.

The existing uniform publication owner and allocator own allocation, upload, commitment and retirement.
Refresh reusable allocation/publication data when its captured version, epoch or allocation changes.
Single-draw blocks still require their consumption behavior; single-frame and multi-frame policies retain
their intended reuse. Failed candidates preserve abort semantics and must not commit unpublished versions.

Keep necessary last-use tracking for GPU-safe allocation reuse, deduplicated at the meaningful frame or
submission boundary where possible. No resource execution group is involved. Internal samplers, buffers
and other implementation resources have explicit owners just as caller-provided resources do.

## Executed state and external integration

The OpenGL state cache remains the authority for executed state. Process dirty slots/tables/domains and
suppress redundant native calls. Known-equal assignments and unrelated observations must not expire all
publication state. Unknown outcomes affect the relevant domain conservatively, without blanket purges.

An external binding change normally affects executed-state publication only. A storage or lifecycle change
also updates metadata and affected prepared consumers. Keep these domains separate so restoring bindings
does not redo resource compatibility checks.

Engine-owned resources remain externally owned. Integration must establish the usable interval from the
engine's lifecycle and withdraw or refresh consumers at replacement/retirement boundaries. Interception
maintains metadata and cache knowledge; it cannot extend an external resource's lifetime. Unsupported
concurrent mutation is a contract violation, not justification for per-draw registry scans.

Interceptors continue forwarding native calls exactly once and containing observer failures. They must
not throw into or cancel native calls. Partial native execution is not retried automatically or certified
as successful publication. Context retirement discards context-owned prepared/native state.

## Validation and operational failure handling

Classify checks by purpose rather than moving every existing check to another layer:

- Creation/preparation checks establish valid resources and compatible operation descriptions.
- Dynamic checks handle new payload lengths, regions and operation state where required for the command,
  managed memory safety or an externally supplied input boundary.
- Operational handling covers failed creation/import, allocation failure, unavailable preparation,
  context loss and actual completion/readiness outcomes.
- Development validation diagnoses stale references, lifetime violations, wrong context/thread,
  incompatible use and internal metadata/cache inconsistencies.

Expensive redundant descriptor walks and lifetime guard acquisition are not ordinary Release requirements.
A diagnostic mode may independently verify prepared state but must not repair it, acquire ownership for
consumers or become necessary for valid execution. Managed memory-safety requirements remain mandatory;
removing diagnostics does not authorize unsafe CPU memory access.

Do not preserve a guarantee that arbitrary invalid use is always rejected in Release. Conversely, removing
that guarantee does not excuse ignoring expected operational failures or knowingly publishing a failed
resource. Public boundary validation must have a concrete purpose and must not be duplicated down the stack.

## Steady-state execution

For unchanged prepared inputs, execution selects prepared state, processes necessary changed bindings and
uniform work, handles actual dynamic arguments, issues native commands and records required last use.
It does not enumerate resources to re-prove their lifetime, reconstruct descriptor compatibility, resolve
unchanged registrations or acquire/release per-resource mutation guards.

This is not a requirement that all binding traversal disappear or every draw become constant-time.
Compact state comparisons, binding loops and genuine changed-data processing are acceptable where they
serve execution. Avoid introducing more dependency bookkeeping solely to remove every small loop.

## Replacement boundary and verification

Implementation must inventory all resource families and command entry points, assign explicit lifetime
owners, and classify existing checks as establishment, change-driven refresh, dynamic handling or diagnostics.
It must include immediate and recorded execution, cached consumers, external resources, local/tool resources,
worker disposal scheduling and teardown. Improving shader bindings alone does not complete this design.

Remove superseded per-draw validation, compatibility histories and temporary lifetime guards with their
callers in the same dependency-complete migration unit. Keep genuine mapping/completion/allocator protocols
where they perform real lifetime or operation work. Do not blanket-delete guards without identifying their
purpose and the owner contract replacing any defensive consumer protection.

Reconcile the linked architecture documents and tests with the changed contract. Do not retain tests that
require universal Release rejection of owner misuse as an accidental architectural constraint; move the
appropriate assertions to diagnostic validation and test valid owner sequencing directly.

No intermediate forwarding facades, parallel admission authorities or compatibility shims are permitted.
An implementation plan may be derived after approval of this proposal. No implementation plan is created here.

Acceptance should demonstrate:

- Consumer references remain non-owning and expose no disposal or mutation authority.
- Owners preserve resources through immediate, cached, recorded, pass and asynchronous use as applicable;
  replacement and teardown withdraw consumers before release, and GPU retirement remains correct.
- Unchanged execution avoids repeated metadata/registration resolution, compatibility reconstruction and
  per-resource guard acquisition, without requiring a universal execution-group mechanism.
- Relevant changes refresh actual dependent preparation; unrelated registrations and observations add no work.
- External binding changes restore executed state without rebuilding unchanged resource compatibility.
- Uniform versions, consumption, commitment and completion-safe allocation reuse remain correct.
- Failed creation or preparation cannot publish usable state; operational failures preserve cleanup and
  native forwarding rules. Diagnostic and ordinary Release modes agree for valid usage.
- Development validation identifies representative owner/consumer contract violations.
- All operation families follow these ownership and preparation principles, with family-specific behavior
  retained where it is necessary.

Use focused behavioral and operation-count evidence. No mandatory timing benchmark or measured performance
claim is part of this proposal. The main tradeoff is explicit owner responsibility in exchange for simpler
execution; correctness depends on implementing those owner lifetimes, not adding another defensive manager.
