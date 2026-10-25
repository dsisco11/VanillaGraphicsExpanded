# RHI commands and command lists

Status: proposed for approval. Design deliverable for Phase 8 of
[Rendering.GpuResourceHandles.todo](Rendering.GpuResourceHandles.todo). This document proposes
an execution boundary; it does not implement commands or rename `StateCache`.

Use uppercase `RHI` in all interface-layer type, namespace and directory names. Existing GPU resource names remain unchanged; `OpenGlStateCache` names the backend-specific cache.

## The proposed boundary

Rendering code should express **what operation to perform on which resources**. The OpenGL backend
should decide which native calls implement it. `OpenGlStateCache` should retain executed binding knowledge
and reconcile command effects, while `GpuResourceManager` remains the sole metadata authority.

For example, an upload helper requests `UploadBuffer(destination, offset, bytes)`. It does not select
`NamedBufferSubData` versus `BufferSubData`, bind a temporary target or restore that binding afterward.
One operation may require several native calls; it is not an OpenTK overload renamed as a command.

Unreal's RHI provides the useful separation: commands express platform-independent work, and an API
command context translates it with backend state handling. We adopt that separation, not Unreal's
thread scheduling or engine infrastructure. See Epic's [parallel rendering overview](https://dev.epicgames.com/documentation/en-us/unreal-engine/parallel-rendering-overview-for-unreal-engine).
Its [command-list API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/RHI/FRHICommandListBase)
also distinguishes immediate behavior and list-owned argument storage; its
[dynamic RHI](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/RHI/FDynamicRHI)
illustrates the backend boundary. These are architectural references, not dependencies.

```mermaid
flowchart TD
    W[Wrappers, upload helpers, render submission] --> C[Semantic commands: immediate or recorded]
    C --> B[OpenGL backend operation]
    B --> P[Validate and consult OpenGlStateCache]
    P --> N[Execute OpenGL 4.5 operation]
    N --> R[Reconcile OpenGlStateCache]
    R --> M[GpuResourceManager publishes metadata effects]
    E[Exact engine GL adapters] --> B
```

| Component | Owns |
| --- | --- |
| `RHICommand` | Immutable operation code and typed argument location in a recorded list. No native dispatch, metadata or lifetime authority. |
| `RHICommandList` | Ordered recording, argument retention, closing, submission state and reset. Provides named operations rather than public arbitrary callbacks. |
| `RHICommandContext` | Immediate execution facade and synchronous submission of a closed list to its originating backend/context. |
| `IRHIBackend` / OpenGL implementation | Semantic operations implemented directly with core 4.5 calls; optional extension operations, outcome handling and explicit operation composition. |
| `OpenGlStateCache` (renamed `StateCache`) | Existing executed binding/routing knowledge, suppression decisions, boundary restoration facts and forwarding established resource effects to the manager. No public native execution surface in the final design. |
| `GpuResourceManager` | Creation handoff, original associations, canonical metadata, operation candidates, availability, revisions, dependency validation and retirement queues. |
| Existing wrappers/renderers | Ownership, allocation/growth/streaming policy, content readiness, pass orchestration and ordering. |

Backend operations and cache are collaborators, not two dispatchers. Ordinary named resource operations
leave selectors untouched and need no binding scope or restoration plan. Cache restoration remains for
render/engine boundaries and operations whose semantics actually change bindings. Native metadata readers
remain manager-owned services, using core named queries wherever available through the backend.
No second binding cache, resource registry or metadata hierarchy is introduced.

## Public operations and resource inputs

The OpenGL backend requires an OpenGL 4.5-or-newer context and uses `OpenTK.Graphics.OpenGL4`
bindings for managed RHI execution. This is the approved minimum, not an optional fast path.
Validate the version and required entry-point availability once at backend initialization; reject
unsupported contexts rather than providing an older-version backend. No platform/adoption survey is
required to adopt this policy. It does not require changing the host engine's context profile.
Core DSA and multi-bind are baseline facilities. Optional features beyond 4.5 retain explicit capability
checks. Exact engine adapters retain their original OpenTK signatures at the external boundary.

The command API uses project-owned descriptors/enums for formats, dimensions, access, pipeline state,
vertex layout and synchronization intent. No public operation accepts an OpenTK enum, binding target,
DSA flag, GL method name or native overload selector. Migration proceeds through complete operation-family
replacements against this final contract, as defined below.

Use the implemented `GpuTextureRef` unchanged for texture consumption. Its native accessors and
OpenTK-valued metadata are an existing compatibility surface, not fields exposed in command descriptors.
Commands pass the reference opaquely; the backend/manager resolves it. This permits another backend
implementation later without claiming the existing native wrappers already support one.

Introduce analogous consumer-only values as needed: `GpuBufferRef`, `GpuSamplerRef`,
`GpuRenderbufferRef`, `GpuFramebufferRef`, `GpuVertexArrayRef`, `GpuProgramRef`, `GpuShaderRef`,
`GpuProgramPipelineRef`, `GpuQueryRef`, `GpuSyncRef`, `GpuTransformFeedbackRef`,
`GpuSemaphoreRef` and `GpuMemoryObjectRef`. Each retains its existing typed resource base, exposes
no ownership API and uses manager validation; none captures another lifetime token or metadata copy.
Pipeline bindings reference immutable graphics/compute descriptions and the existing executable owner.
Framebuffer references remain useful for the OpenGL integration; portable passes describe attachments,
not read/draw framebuffer binding targets. The default surface is an explicit surface input, not ID zero.

Consumption does not grant resize, deletion or ownership transfer. Mutating wrapper methods use an
internal owner-bearing execution route; destroy requests originate only from existing ownership and
retirement services. Buffer uploads/copies are explicit write operations admitted through existing
usage guards, not methods on the reference itself. Imported resources keep their current ownership rules.

| Operation family | API intent and execution disposition |
| --- | --- |
| Creation/destruction | Manager creation retains legal-input initialization records and nullable metadata handoff. Backend creation executes immediately; no unresolved resource placeholders in lists. Owner disposal schedules existing retirement; deletion is internal and never a consumer `Destroy(ref)` API. Covers every existing resource kind, including views and external-memory/semaphore objects. |
| Storage/configuration | Allocate buffer/image/renderbuffer storage; configure attachments, vertex input, samplers and executable interfaces. Owner operations execute immediately and commit one coherent result. Immutable storage restrictions remain enforced. |
| Shader work | Compile, link/binary installation and pipeline-stage configuration remain explicit immediate operations. Existing deferred link completion owns reflection/results; it is not command-list GPU completion. |
| Upload/copy/clear/mips | Typed buffer ranges, texture regions, row/slice pitches and view-relative subresources. Upload, copy and clear can be recorded. Mip generation is immediate initially because mutable generation can change storage facts. Contents never become canonical metadata. |
| Binding/passes | Set pipeline, bind resource table, set dynamic state, begin/end render pass. Include explicit image level/layer/access/format and attachment load/store/clear intent. Preserve full-input validation and alias feedback checks. Recordable. |
| Draw/dispatch | Direct, indexed, instanced and indirect draw/dispatch with explicit offsets/counts. Vertex/index semantics stay above GL addressing. Recordable; no reordering. |
| Synchronization | Resource access barriers, fence insertion, GPU waits and external semaphore operations. Backend maps supported access intent; no inferred render graph. CPU waits are explicit immediate calls. Imported-handle ownership is never inferred from a command. |
| Queries/readback | Begin/end timestamp or statistics operations and enqueue readback into owned staging storage are recordable. Typed result requests expose `Pending`, `Ready`, `Failed` or `Cancelled`; polling/waiting/mapping is explicit and immediate. Raw client pointers and mapped spans are never recorded. |

Expose semantic limits and optional features through `RHICapabilities`, projected from the existing
context capability/limit data rather than creating a second discovery system. Baseline operations call
their core implementation directly: no per-operation variant table, DSA enable flag, backend fallback
strategy objects or runtime core/ARB/EXT resolver. `IRHIBackend` selects the graphics backend, not an
OpenGL version strategy. The command opcode dispatcher only interprets recorded operations.

Optional operations, such as external memory/semaphore imports, have explicit feature checks and
focused extension implementations. Reject unsupported requests before dispatch with a feature reason;
do not invent a general fallback registry for them. Format-dependent limits and sample counts remain
cached. Native entry-point loading is still required, but it is not semantic implementation selection.

## Immediate execution and recorded lists

Recommend immediate execution as the default, using the same backend operations as list submission.
This preserves synchronous creation, current exception boundaries and existing rendering order. A list
is an optional CPU recording container, not an OpenGL display list or a new GPU queue.

Illustrative call shape (names are proposed, not existing APIs):

```text
context.UploadBuffer(bufferRef, offset, bytes)      // executes now
list.UploadBuffer(bufferRef, offset, bytes)         // copies bytes; no GL or publication
list.BeginRenderPass(passDescription)
list.SetPipeline(pipeline)
list.BindResources(bindings)
list.DrawIndexed(drawDescription)
list.EndRenderPass()
list.Close()
submission = context.Submit(list)                   // executes in order on this thread
```

Lists transition `Recording -> Closed -> Executing -> Executed` or `Failed`. Cancellation is allowed
before execution and releases owned arguments/references. During synchronous submission there is no
concurrent cancellation. Reset is permitted after execution, failure or cancellation, never while
executing. Reset returns to empty recording; lists are single-use recordings, not replayable snapshots.

The first implementation uses immediate execution and single-threaded recording/submission on the
originating context thread. Recording must nevertheless be independent of a current native context by
contract, rather than relying on a future refactor to permit concurrency:

- Each recording list has one exclusive owner. Its command storage and mutable recording state are
  list-local; no shared mutable recorder, ambient current context or global scratch storage is allowed.
- Recording consumes supplied values, copied payloads and retained immutable descriptions/references.
  It does not inspect mutable wrapper metadata, resource validity, the manager registry or executed cache
  state. Resource-dependent checks remain at execution; recording checks only supplied argument shape.
- Closing freezes the recording. Ownership transfer must publish the complete recording before another
  thread consumes it, with exclusive ownership of submission, cancellation and reset. Retaining a managed
  reference does not make its resource thread-safe or prevent explicit retirement.
- Execution remains on the originating context thread. The orchestrator specifies submission order;
  future worker completion order must never implicitly determine rendering order. Recorded work retains
  its intended context identity without requiring that context to be current during recording.

These are required architectural boundaries now. Worker scheduling, concurrent recording integration,
asynchronous submission, shared-context execution, nested lists and a cross-list scheduler remain outside
the initial implementation. A dedicated RHI execution thread and parallel native translation are separate
design decisions, not implied by enabling CPU-only parallel recording.

Record commands in reusable typed payload pages with an ordered `RHICommand` header stream. Store
managed references in list-owned tables, not an unmanaged union. Copy spans/arrays and mutable binding
descriptions at recording; freeze immutable executable/descriptive objects by retaining them. Do not
record closures or require one heap object per command. Pooling is a candidate implementation to measure,
not a performance claim. Immediate calls need no recording allocation.

References retain wrappers but do not prevent explicit disposal or replacement. Resolve current resource
identity and metadata at execution, consistent with `GpuTextureRef`: an intentional owner replacement
before submission is visible, while retired borrowers/views remain invalid. Copying descriptors freezes
arguments, not resource contents or native identity. Callers needing old storage must retain a distinct
owner; the command list does not invent snapshot semantics. Revalidate bounds, executable compatibility,
manager usability and pass feedback before use, even when reference equality is unchanged.

Recording performs argument-shape and balanced-pass checks without native queries, cache updates,
guards or metadata publication. Submission rechecks context, required optional features and resource limits,
and validates each complete
binding set before publishing it. Pass guards start on execution and end on execution/unwind. Lists must
contain complete passes; they cannot leave an active pass for another list or straddle an engine callback.
They are not implicitly state-isolated: commands leave their specified state, while temporary operation
bindings and explicit pass/engine boundary scopes restore their contracted surrounding state.

Successful `Submit` means CPU native dispatch completed, not GPU completion. It neither calls Finish nor
inserts a fence automatically. Explicit fence/readback tickets own staging or persistently mapped memory
until the GPU is finished; list reset releases only CPU recording storage. Ordinary upload bytes may be
released after their native upload call consumes them. Resources follow existing GL deferred destruction
and deletion queues; CPU completion is not permission to recycle GPU-used staging regions.

A recorded fence marker returns a pending completion ticket, not a usable `GpuSyncRef`. Execution
creates its native fence through the existing manager creation path; cancellation before execution
creates nothing. This result-bearing synchronization operation is distinct from deferred general resource
creation. Query resources are created before recording their begin/end commands. Mapping a readback
requires a ready ticket; waiting is explicit and may block, polling never promises immediate availability.

On failure stop at the offending command, preserve its original exception and report the command index.
Already executed work is not rolled back or retried. Unexecuted result requests become failed/cancelled;
already enqueued readbacks/fences keep their own completion ownership. Unwind pass/binding scopes and
withdraw uncertain knowledge. Context retirement cancels outstanding CPU work and marks result requests
unavailable without issuing GL into a replacement context.

## Backend dispatch and publication

Every operation has one execution owner:

1. Check context, resource usability, legal inputs and existing usage/reentry guards. Baseline operations
   use their fixed core implementation; only optional operations check their extension requirement.
2. Ask `OpenGlStateCache` whether an eligible known-equal state change can be suppressed. Allocation,
   content upload, draw, link and synchronization are not suppressible just because metadata matches.
3. Capture original associations and required restoration facts. For metadata mutations use the existing
   manager operation to withdraw affected availability and assemble a complete candidate.
4. Execute native calls in the backend. Explicit internal steps share the owning operation; they do not
   independently publish. Intercepted calls remain individual operations.
5. Reconcile established binding and lifecycle effects through `OpenGlStateCache`; deliver metadata effects
   to the manager for exactly-once canonical commit and typed notification. Named resource operations
   do not invalidate unrelated selectors. Where a binding scope is actually required, restore it through
   backend calls and the same cache, including exceptional paths.

This replaces caller-facing Begin/End pairs with complete semantic operations. Internal operation state
remains necessary for original identity, grouped work and interruption; disposal is cleanup, not evidence
of success. There is no ambient collector or arbitrary callback that can join another operation.

Preserve the approved query policy: controlled inputs/defaults/derivations are authoritative under existing
failure handling. No universal GetError drain, Is-object query, full metadata readback or duplicate
requested/actual cache. Retain narrowly justified sample-count/resolved-format reads, shader results and
interfaces, external discovery/recovery and kind-specific deletion outcomes. Reported rejection does not
publish success; uncertain effects withdraw only affected knowledge. This does not claim detection of
every silent GL rejection. Metadata publication failure never retries native work.

| Example | OpenGL 4.5 execution | Required reconciliation |
| --- | --- | --- |
| Buffer allocation/upload | Core named buffer storage/data/subdata | Preserve surrounding selectors without temporary binds. Storage commits once; subdata does not publish capacity. |
| Cube/mip allocation | Core named immutable texture storage when immutable storage is requested | Derive face/mip images and commit once. Retained mutable image definition uses the required bound API; preserve active unit and target binding. Never substitute immutable storage for mutable semantics. |
| Framebuffer attachment | Core named attachment operation | Preserve distinct read/draw selections without temporary binds. Routing and attachment metadata have distinct owners. |
| Vertex input | Core named VAO format/binding/EBO operations | Preserve surrounding VAO/buffer state. Publish configuration revision once, without a new full-layout metadata mirror. |

Use core 4.5 DSA wherever it expresses the operation. Remove older-context named/bound fallback branches
and redundant core/ARB/EXT alternatives for baseline operations. Binding is still required for drawing,
exact external calls and operations without an equivalent core named API; these are semantic requirements,
not older-version fallbacks. Keep their binding/restoration mechanics inside the backend.
A throw or partial execution is failure, never a signal to try another path. Remove `GpuBufferUploader`'s
try-named/catch-and-fallback policy when replacing that family. Optional extension operations select a
supported implementation before dispatch or reject as unsupported. Format-specific limits remain cached;
intercepted calls never drain errors to discover capabilities.
Cache-restoration failure marks affected selectors unknown and preserves the original failure.

Managed creation uses core `Create*` entry points where available so named operations receive realized
objects. Do not introduce a generate-name/temporary-bind realization fallback. Kinds without `Create*`
retain their specific core creation contract. Exact intercepted `Gen*` calls still preserve reserved-name
semantics; acquiring external resources must not pretend those names are already realized.

Managed metadata acquisition likewise uses named core queries where equivalent. Remove older-context
reader switches and their bound-query implementations in the same family replacement. Keep discovery
for unknown external state and binding-based queries only where the required fact has no equivalent
named query. OpenGL 4.5 availability does not turn mutable storage into immutable storage, make content
immutable, or make unknown external facts query-free.

## Engine integration and migration boundaries

Keep compiler-checked `EngineStateCallMap` adapters and exact call replacement. An intercepted operation
uses an internal OpenGL compatibility entry point on the backend: preserve the original overload,
arguments, side effects and error behavior, execute it exactly once, then use the shared reconciliation
contract. It is not converted to a suppressible managed command and is never deferred into a list.
Allowed intercepted owners remain GL/Core, Ext and Arb; legacy exclusions and the built-in-assembly interception
boundary remain unchanged. Backend native calls are outside rewritten engine call sites, preventing
recursion. Raw unsupported external inputs retain conservative handling.
Keep exact forwarding modules separate from managed core operation modules. Their required older-shaped
signatures preserve engine behavior on the same 4.5 context; they do not provide a lower-version RHI.

Define the final RHI interfaces and dependency rules before migrating callers. Migration proceeds through
complete operation-family replacements: update every affected caller, move native execution and capability
selection into the OpenGL backend, and remove superseded entry points in the same replacement.
Temporary compatibility facades and forwarding shims are prohibited. Unmigrated families may retain their
existing implementation only while explicitly tracked as incomplete; a completed family cannot depend on
a temporary compatibility layer. If a dependency prevents complete replacement, include it in the same
migration unit. Final acceptance requires removing every internal execution bypass of the RHI boundary.

Each replacement must verify behavior, exactly-once publication, binding suppression and restoration,
and inspect callers/dependencies for residual old entry points or native dispatch outside the backend.
Remove that family's baseline capability branches, variant tables, retry-based fallback state, temporary
DSA-emulation binding scopes and obsolete fallback-only tests together. Cover wrappers, upload helpers,
manager creation, native readers and fixtures, not only the public command facade. Replace forced
DSA-disabled tests with direct core-operation/state-preservation tests; retain tests for necessary bound
semantics and exact external calls. Do not preserve an old backend implementation just to satisfy fixtures.
The compiler-checked engine adapters above remain a permanent external integration boundary, not a
temporary migration exception. Existing resource/reference accessors are retained contracts, not permission
for internal callers to issue native operations or select backend paths.

The final split moves native execution from `StateCache` into focused OpenGL backend modules, then
renames the remaining class `OpenGlStateCache`. Each operation has exactly one execution owner.
Manager creation calls the backend through the execution context; backend effects use existing manager
operation/publication services without recursively invoking creation. Preserve wrapper allocation policy,
ownership and helper responsibilities; update GL-shaped signatures and their callers where the final
contract requires it rather than retaining obsolete overloads. Readers keep manager acquisition ownership.

`ShaderPreparedSubmission` retains complete-set validation and compatibility history; its publication
target becomes command operations. `GpuPreparedBindings` continues preparing supplied program metadata
only. Generated submissions retain immutable desired arguments for recording; successful-upload history
must advance on execution, never merely when commands are recorded. On cancellation/failure, preserve
dirty CPU uniform state and invalidate only affected execution history. Prepared tables/executable owners
are revalidated at execution so a recorded layout cannot be used against a different linked interface.
Commit only the captured uniform version that actually executed; never clear a newer CPU edit. Reject
an incompatible executable/table change and require re-recording rather than replaying old slot indices
against a new interface. Submission-time equality checks cannot discard a needed upload based solely
on a successful recording that has not executed.

Render pass scopes retain clear/load/store behavior, feedback guards and boundary restoration. Draw and
dispatch enter the same backend; no direct native draw escape remains in migrated consumers. Retirement
queues retain original entries/context and run backend deletion only after their existing admission.
Recorded references never authorize a queued delete to reacquire an entry by reused ID.

Organize source by responsibility: `Rendering/RHI/Commands` for command contracts/recording,
`Rendering/RHI/Backends/OpenGL` for native operations/dispatch and cache modules, and existing manager,
metadata, initialization and wrapper domains for their current responsibilities. Separate argument
storage, list lifecycle, core operation families and optional extension modules rather than making one large dispatcher.
Other backends implement the semantic interface; optional features can reject unsupported requests.
No second backend, shader-language translation, render graph or global resource-model rewrite is proposed.
PSO multi-bind fits inside the binding backend using baseline support and existing slot knowledge;
individual calls remain useful for ineligible semantics or measured batching policy, not older contexts.
This design does not require multi-bind to wait for deferred recording. The separately scheduled PSO
work may precede this backend migration; its existing-runtime capability path is removed when that family
is replaced under the 4.5 contract.

## Representative outcomes and verification

| Path | Required outcome / focused verification |
| --- | --- |
| Cube with several mips | Existing constructor supplies its allocation request. Manager/backend creates privately, derives all face/mip facts, publishes once, then hands off identity. Partial allocation failure cleans only the provisional object; no usable partial wrapper. |
| Buffer upload | Core named operations produce expected contents while sentinel bindings remain unchanged; no temporary bind, per-call capability query, extra metadata refresh or retry after native failure. |
| Framebuffer reattachment failure | Capture original association, apply configuration as an explicit operation. A detected partial failure leaves affected metadata unavailable, restores surrounding selectors and prevents drawing from stale attachments. No rollback claim; explicit repair/recovery remains possible. |
| Draw/dispatch | Bind complete validated inputs and pipeline, execute work in order, preserve shader algorithms and content readiness. Stable bindings suppress native calls without bypassing current resource checks. |
| External deletion | Exact engine overload executes once, per-object outcomes retire original entries and repair affected binding knowledge. Duplicate/zero names and retained shader/program native references keep their existing semantics. |
| Queued retirement | Dispose withdraws managed usability under existing guards. Drain validates original entry/context before deletion; name reuse cannot redirect it. A list retaining a retired wrapper fails use, rather than keeping it artificially live. |

Test immediate and recorded paths against the same outputs and sentinel state, including native views,
view-relative selections, aliases, owner replacement, unavailable metadata, context-local tools and
context retirement. Cover list close/reset/cancel, copied array mutation, partial execution, staged
readback ownership, shader reload and deferred link completion. Test baseline admission/rejection and
core DSA for buffers, images, framebuffers and VAOs. Test binding preservation for operations that still
require bound semantics. Test optional features enabled/disabled separately, retaining unexecuted native
cases as explicit gaps. External exact-forwarding tests must count one native execution and one
publication, preserve errors and prove no recursion. Keep hardware coverage gaps explicit.

Measure immediate baseline versus command facade and recorded submission: CPU time per operation/list,
recording allocation bytes, retained capacity, native binds, metadata queries and publication counts.
Use unchanged/changed/sparse binding sets, small/large payloads and representative cloud/depth passes.
Reuse stable snapshots without added queries; measure dispatch overhead before selecting page sizes or
pooling policy. No promised frame-time gain. Debug instrumentation remains DEBUG-only; no instrumented
Release product variant. Future implementation qualification includes delegated regression tests,
canonical build/package identity and user-run visual checks, distinct from this document review.

Verify recording with no current native context and supplied inputs only; verify closing prevents further
mutation and that reset/cancellation release retained CPU data without native work. These checks establish
the recording boundary, not full concurrent execution qualification. Before proposing worker recording,
measure preparation, payload copying, recording, validation and native dispatch separately. Compare the
parallelizable portion against scheduling, handoff and ordering costs; command recording alone may be
too cheap to justify workers. Add concurrency integration and its race/lifetime tests only when those
measurements justify it and that additional scope is approved. No CPU-time result alone proves a frame-time gain.

## Approval and controlling contracts

Recommended decisions: semantic backend operations; immediate default with initially single-threaded,
single-use recorded lists and the required context-independent recording contract; execution-time resource resolution; immediate creation/storage/lifecycle
operations; direct OpenGL 4.5 execution with explicit optional features; existing manager publication
and error policy.
These choices are proposed together for review. No unresolved external specification blocks the design;
API naming and measured storage tuning remain implementation details, not permission to change semantics.

Current authority: [GPU resources](Rendering.GpuResources.md),
[prepared resource submission](Rendering.ShaderBindingState.md#prepared-resource-submission),
[resource proposal](Rendering.GpuResourceHandles.Proposal.md),
[inventory](Rendering.GpuResourceHandles.Inventory.md) and
[API coverage](Rendering.GpuResourceHandles.ApiCoverage.md). The approved creation/publication contract
is retained at `3f307365:docs/Rendering.CommandPublication.Proposal.md`; the completed texture-reference
proposal is retained at `b9fca04c:docs/Rendering.GpuTextureRef.Proposal.md`. Consult those Git versions,
not their removed live paths. The current source remains authoritative for implemented APIs.

Approval of this proposal is required before deriving implementation phases or changing runtime code.
