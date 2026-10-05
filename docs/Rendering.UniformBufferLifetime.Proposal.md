# Uniform-buffer lifetime and storage ownership

Status: implemented and verified on 2026-10-05. See [implementation and evidence](Rendering.UniformBufferLifetime.md).
Implementation plan: [Rendering.UniformBufferLifetime.todo](Rendering.UniformBufferLifetime.todo).

## Intent

Replace the assumption that every CPU uniform block uses frame-local ring storage with an explicit lifetime contract. Keep stable logical uniform-buffer instances while allowing their physical allocations to change on update. Use transient allocation for short-lived data and independently retained, pooled storage for data that survives across frames.

Preserve existing shader declarations, std140 packing, generated submission, mutation guards, numeric binding slots and authoritative state-cache ownership. A logical uniform buffer belongs to its data owner and can be shared by compatible shaders; it does not necessarily own a dedicated OpenGL buffer object.

Expected benefits are fewer uploads of unchanged long-lived data and allocation policies appropriate to actual lifetimes. CPU time, GPU time, memory consumption and driver synchronization must be measured separately. This proposal makes no frame-rate claim.

## Baseline before this implementation

| Existing owner | Current responsibility | Proposed evolution |
| --- | --- | --- |
| [CpuUniformBuffer](../VanillaGraphicsExpanded/Rendering/CpuUniformBuffer.cs) | Packed CPU contents, change revision and last successfully published ring range | Retain CPU packing; delegate physical publication lifetime to a logical uniform-buffer instance |
| [PackedUniformBuffer](../VanillaGraphicsExpanded/Rendering/PackedUniformBuffer.cs) | Complete external byte copies with change detection | Preserve exact-size validation and revision tracking |
| [GpuUniformRingBuffer](../VanillaGraphicsExpanded/Rendering/GpuUniformRingBuffer.cs) | Aligned transient pages, mapped or orphaned uploads, frame fences and allocation epochs | Remain the transient storage allocator |
| [GpuUniformRingSystem](../VanillaGraphicsExpanded/Rendering/GpuUniformRingSystem.cs) | Access to the current render-thread ring | Remain a narrow access boundary; integrate persistent storage under the same rendering/context lifetime owner |
| [ShaderPreparedSubmission](../VanillaGraphicsExpanded/Rendering/ShaderPreparedSubmission.cs) | Validate retained inputs, then publish through prepared binding slots | Resolve the logical buffer's current physical range without changing validation or binding authority |
| [GpuUniformBuffer](../VanillaGraphicsExpanded/Rendering/GpuUniformBuffer.cs) | Native uniform-buffer resource and range binding | Continue to represent physical storage, not logical lifetime policy |

Baseline reuse required identical CPU revision, ring identity, a live allocation and the same open allocation epoch. Every frame boundary invalidated reuse. This eliminated repeated copies within a frame, but unchanged data still had to be copied in subsequent frames.

The existing warmed liquid activation fixture removes 288 repeated allocations and 462,336 copied bytes across 32 nested surface/volume scopes. That result concerns unchanged same-frame submissions; it does not establish the benefit of persistent storage. See [ShaderAuthoring.md](ShaderAuthoring.md) and the existing `artifacts/WaterLagAnalysis/uniform-reuse-*` receipts.

## Lifetime contract

Introduce an instance-level `UniformBufferUsage` declaration:

| Usage | Lifetime promise | Storage policy |
| --- | --- | --- |
| `SingleDraw` | One draw or dispatch consumes a published version | Fast transient allocation; do not retain its publication for independent later submissions |
| `SingleFrame` | A published version may serve multiple draws during the current frame | Transient allocation with unchanged-version reuse within the open frame epoch |
| `MultiFrame` | A published version may serve multiple draws across frames | Retained allocation whose lifetime is independent of transient-page rotation |

These values describe permitted publication lifetime. They do not assert that contents are immutable within a frame. A `SingleFrame` instance may be updated several times during that frame, producing distinct physical versions. A `MultiFrame` instance may also be updated; its logical identity remains stable while its backing range changes.

Do not equate these categories with `FrameStable` and `PerDraw`. A separate write-frequency constraint could be added later if useful, but it is not required for this architecture. In particular, this overhaul must not introduce an implicit prohibition on changing frame-lifetime data after its first publication.

Usage is fixed when the logical instance is created. Changing it requires an explicit replacement and retirement operation, rather than silently moving a live instance between allocator policies. Existing callers initially retain `SingleFrame` behavior through an explicit compatibility default.

The declaration belongs to the data-owning instance or its typed factory. Shader binding declarations continue to describe interface compatibility, names and slots; they do not independently select conflicting lifetimes for a shared instance. Lifetime is runtime allocation policy and does not create shader or SPIR-V variants.

## Logical identity, layout and physical versions

Keep three concepts separate:

1. **Layout:** std140 member packing, total byte size and executable compatibility.
2. **Logical instance:** retained CPU contents, content revision, usage and ownership.
3. **Physical version:** a complete uploaded range, allocator identity, storage generation and retirement state.

Two shaders may bind the same logical instance only when they intentionally share its contents and have compatible layouts. Identical block names or layouts alone do not establish shared ownership. Separate instances with identical bytes remain separate unless their owning system explicitly shares them; do not introduce global byte hashing or deduplication.

A logical instance may use one range within a larger pooled native buffer. Dedicated native buffers remain a backend option for allocations that do not fit the pool policy. Neither shader authors nor callers should depend on native buffer IDs or offsets remaining constant.

Keep named compatibility publication and prepared numeric-slot publication connected to the same version-selection logic. Avoid maintaining separate upload caches for these entry points.

## Publication and update semantics

Assignments continue to modify CPU storage only. Generated submission validates the complete active input set before publishing resources. It then resolves each logical instance as follows:

- Reuse a published version when the content revision is unchanged and its allocator, storage generation and declared lifetime remain valid.
- Otherwise allocate a fresh compatible range and copy the complete block. Preserve alignment and block-size checks before binding.
- Bind the selected range through the existing uniform-buffer and state-cache owners, even when no upload was needed.
- Commit the instance's published revision/range and consume dirty work only after its publication succeeds.

Updating a logical instance must not overwrite storage that previously submitted GPU work may still read. Publish new contents into a different range, then retire the replaced version according to its last GPU use. The stable logical handle now resolves to the new version; earlier commands retain their old physical range.

If allocation, upload or binding fails, retain CPU contents, pending dirty work and the prior successfully published version. Do not silently draw with stale data: the failed submission remains failed through the existing activation contract. Any newly reserved but unpublished range must be released or retired by its allocator without becoming the instance's current publication.

Submission remains nontransactional across different resources. A later block failure does not roll back an earlier successful block publication; no draw follows a failed submission. Existing retry behavior must remain intact.

Single-draw usage requires a consumer whose publication boundary corresponds to its draw or dispatch. Repeated independent activations must not reuse a consumed single-draw publication. Scope restoration is still a binding operation and must not be mistaken for proof that another draw has or has not occurred. Validate this promise at existing owned execution boundaries where available; do not add a global engine draw hook merely to count consumption.

## Transient storage

Continue using the existing ring for `SingleDraw` and `SingleFrame` versions. Preserve alignment, capacity checks, persistent mapping with coherent or explicit-flush handling, and the orphan/subdata fallback.

Single-frame reuse requires the same open frame epoch. Repeated `BeginFrame` calls, including the same frame index, invalidate earlier publications. `EndFrame` closes transient reuse before fencing. Page existence alone never authorizes reuse after those boundaries.

Preserve immutable ranges within each epoch. Ring exhaustion must either fail through the existing publication path or use an explicitly designed additional-page mechanism; it must not silently wrap over live data. Growing the ring is not an automatic requirement of this overhaul.

## Persistent storage and retirement

Add a persistent uniform-storage allocator beside the transient allocator, under the existing render-context lifetime owner. It manages aligned ranges and reuses retired storage rather than creating one native object per shader activation.

Its required behavior is:

- A current `MultiFrame` version survives frame boundaries without another upload while its bytes and context remain unchanged.
- Every successful binding records that the physical version participates in the current submission/frame, including unchanged rebindings and scope restoration.
- Replaced or released versions become reclaimable only after all GPU work referencing them has completed.
- A current version is never returned to the free pool merely because its previous fence has signaled; the logical instance still owns that version.
- Storage generations invalidate stale range records when a pool page is recycled or replaced. Allocator identities prevent reuse across unrelated owners.

Use the existing fence and rendering submission boundaries to establish completion. A fixed number of elapsed frames is not sufficient proof of retirement. Group retirement behind submission/frame fences where possible instead of inserting a new fence per UBO update.

Recording last use is essential: a version created many frames ago may have been bound again just before replacement. Its creation frame or original upload fence cannot determine when that version is safe to reclaim.

The allocator may use size classes, free ranges or dedicated backing resources for exceptional sizes. Choose the concrete policy after measuring current block sizes and lifetimes. The public contract must not expose a pool layout or hard-code one shader's block size.

## OpenGL realization

Implement the same logical lifetime semantics for both supported upload mechanisms:

| Backend capability | Publication behavior |
| --- | --- |
| Persistent mapped storage | Copy into a newly reserved, non-overlapping range; apply coherent or explicit-flush requirements; retain it until safe retirement |
| Conventional buffer storage | Upload into a newly reserved range or fresh storage using existing buffer APIs; avoid overwriting a live version |

Do not orphan a pooled buffer that contains live persistent versions. Orphaning remains suitable for transient pages whose existing ownership contract permits it. Streaming/static usage hints are allocation hints, not synchronization or lifetime guarantees.

Retain `GpuUniformBuffer`, `GpuFence`, prepared binding metadata and `StateCache` as the native operation owners. No new per-draw GPU state queries or blanket state-cache invalidations are needed. Deletion and replacement must use the existing targeted resource-lifecycle notifications.

Persistent mapping is an upload mechanism; `MultiFrame` is a logical lifetime. Either usage can be implemented without persistent mapping, and using persistent mapping does not itself authorize cross-frame reuse.

## Sharing, reload and disposal

Shared instances have one explicit data/lifetime owner. Shaders borrow them; disposing or reloading one borrowing shader must not destroy storage still owned by the shared instance. Shader-owned instances are released with their owning shader. Avoid automatic ownership inference from the number of shader bindings.

Executable reload retains compatible CPU contents and logical instances. Revalidate binding compatibility with the new prepared interface. Reuse backing storage only if the layout and rendering context remain compatible; a slot change requires rebinding, not necessarily uploading. A changed layout requires an explicitly repacked or replaced instance.

Uniform allocators belong to one renderer/context lifetime and must be disposed while that context is current, before its teardown. Automatic context replacement and recovery are outside this work; StateCache context-replacement policy is deferred to separate work.

Disposing a logical instance prevents future publication and queues its owned physical versions for safe retirement. Keep the current CPU-packing type's compatibility behavior separate during migration: introducing terminal disposal on a new logical resource must not silently change existing `CpuUniformBuffer.Dispose` callers. Update those callers and tests explicitly before removing the compatibility adapter.

## Integration and source layout

Follow the responsibility-based naming and ownership rules in [Rendering.AuthoritativePipelineState.Proposal.md](Rendering.AuthoritativePipelineState.Proposal.md). Place new uniform-storage responsibilities under a dedicated rendering subdirectory/namespace, with one clearly named responsibility per file.

Separate the usage declaration, logical publication/version record, transient allocator integration, persistent allocator and retirement tracking. These are responsibilities, not a requirement to create an abstract interface for every class. Reuse existing resource wrappers and the existing frame/context composition root; do not introduce a competing shader registry or rendering lifecycle.

Keep `CpuUniformBuffer` responsible for packing and mutation tracking. Move allocator-specific cached publication facts behind the logical uniform-buffer owner. Preserve typed accessors, `PackedUniformBuffer` comparisons, write guards and the obligation to call `MarkDirty` after supported direct writes.

Generated binding code should continue to snapshot sources, validate active inputs and invoke the common publication boundary. Resource metadata and layout compatibility remain preparation-owned. Raw GPU uniform buffers used by explicit integration paths must remain supported without being mistaken for CPU-backed logical instances.

## Consumer migration

Inventory actual writes and ownership before selecting usage. Names such as “frame,” “shared” or “parameters” do not prove a lifetime.

| Candidate | Initial classification to verify |
| --- | --- |
| Liquid per-mesh draw parameters | Short-lived versions; preserve current single-frame behavior until one-draw submission semantics are established |
| Liquid frame and wave parameters | Single-frame publication; retain independent instances where surface and volume contents differ |
| View/pass parameters recreated for each frame | Single-frame publication |
| Material or configuration constants retained across frames | Multi-frame candidates when layout and ownership are stable |
| Data intentionally shared by several compatible shaders | One explicitly owned logical instance with usage determined by its actual lifetime |

Do not classify every shader parameter block as multi-frame merely because the shader object persists. A block updated every frame may receive no benefit from longer-lived storage and may incur more retirement bookkeeping.

Migrate conservatively: establish the logical contract over current transient behavior, add persistent allocation and retirement, then opt verified consumers into multi-frame lifetime. Retire duplicated caches only after both named and generated publication paths use the same owner. This document does not authorize a bulk rename or unrelated shader refactor.

## Verification and measurements

Preserve the contracts documented in [ShaderAuthoring.md](ShaderAuthoring.md), [GPU.ShaderDemandLoading.md](GPU.ShaderDemandLoading.md) and [GPU.UniformContractValidation.md](GPU.UniformContractValidation.md).

Required focused verification includes:

- Actual GPU-observed values for unchanged reuse, changed versions and restoration across graphics/compute owners.
- Zero repeated uploads for unchanged multi-frame contents across frame boundaries; fresh versions when contents change.
- Multiple updates within one frame while earlier submitted draws retain their original snapshots.
- Shared ownership, different binding slots, incompatible layouts and shader reload.
- Transient rollover, repeated frame indices, persistent-pool reuse and delayed GPU completion.
- Replacement after an unchanged version was used again in a later frame, proving retirement follows last use.
- Failed allocation/upload/binding, retry, disposal, renderer teardown without stale publications or leaks.
- Persistent-mapped and conventional upload paths, alignment, block sizes and allocator overflow.
- Existing CPU write guards and direct-write/MarkDirty paths; no source-text tests as a substitute for behavior.

Measure successful allocations, copied payload bytes, binds, resident storage, pending retirement and fence waits separately. Compare current transient behavior against the new policy for frequently changed draw data, per-frame data and genuinely stable multi-frame data. Include mixed water and non-water consumers.

Use matched workloads and unchanged shader optimization settings. Keep CPU preparation/upload measurements separate from GPU draws and live frame timing. Reuse the current profiling owners and fixtures; do not create a second general profiling framework. A reduction in copies is useful evidence but is not itself proof of a frame-time improvement.

## Completion criteria

The overhaul is complete when lifetime is explicitly declared, logical ownership is independent of physical allocation, transient and persistent policies preserve immutable submitted versions, and unchanged multi-frame data avoids redundant uploads. All publication paths must share the same validation and lifetime authority, with supported fallback storage and verified retirement behavior.

The final record must identify migrated consumers, unchanged compatibility paths, allocation/memory results and measured performance limits. Choosing which remaining consumers benefit from persistent storage may remain a measured follow-up; correctness of persistent lifetime and retirement may not.
