# Shared GPU resource handles and metadata notifications

Status: approved. Implementation plan: [Rendering.GpuResourceHandles.todo](Rendering.GpuResourceHandles.todo). This document defines the authoritative design and acceptance criteria; approval and planning do not begin implementation.

## Intent

Give owning and non-owning GPU wrappers a common resource identity and a common route for deletion and metadata notifications. Extract shared allocation responsibilities from `GpuResource` into `GpuResourceHandle`. Let StateCache observe native operations and notify the wrappers associated with the affected handle.

An owning texture and a borrowed texture referring to the same native object should share its identity and validity. Borrowed textures should require neither a dedicated registry nor a separate lifetime authority. Persistent borrowed metadata and texture binding caching then follow from the normal resource mechanism.

## Current behavior

- [GpuResource](../VanillaGraphicsExpanded/Rendering/GpuResource.cs) combines wrapper disposal, native identity, ownership policy, usage guards, context capture and deletion scheduling. Its validity check currently uses the wrapper's native ID and disposal state.
- [BorrowedTexture](../VanillaGraphicsExpanded/Rendering/BorrowedTexture.cs) derives from `GpuTexture`, queries texture metadata at construction, does not own storage and rejects ownership transfer. Its caller currently has to retire stale metadata.
- [StateCache.TextureLifetime](../VanillaGraphicsExpanded/Rendering/StateCache.TextureLifetime.cs) records the binding and restoration consequences of native texture deletion. It does not currently notify a general registry of resource wrappers.
- [EngineStateCallMap](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateCallMap.cs) maps supported GL signatures to `EngineStateCalls`. This supplies the existing redirection mechanism, but coverage is determined by its concrete adapters; it is not blanket interception of every GL operation. Storage-changing texture and buffer commands require coverage work.
- `GpuResource.DeleteOrEnqueue` already routes textures, buffers, vertex arrays and several other kinds through StateCache. Some kinds, including queries, shaders and sync objects, still use direct GL deletion paths. Common lifetime notification must include those paths before claiming support for every existing resource kind.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| `GpuResourceHandle` | Shared identity and validity for one native object incarnation, plus the allocation-wide state required by existing usage guards. |
| `GpuResource` | Wrapper-local disposal, ownership policy, deletion scheduling, association with a handle, and the abstract metadata-change contract. |
| Typed resource wrappers | Resource-specific operations and metadata, including implementation of metadata-change handling. |
| StateCache | Common resource lookup, deletion and context-retirement notifications, metadata-change dispatch, and existing native binding knowledge. |
| Rendering integrations | Selection of resources and their content/readiness contracts. |

There is no `GpuResourceLifetime` class or borrowed-texture-specific tracking system in this design.

## Shared handle identity

`GpuResourceHandle` is a non-owning managed reference to one native object incarnation. It carries resource kind, native ID and originating context identity, including context generation. Use a native-width ID so the representation also accommodates pointer-shaped GL handles.

StateCache maintains a common lookup by context identity, resource kind and native ID. Wrappers referring to the same live object share the same handle. Numeric equality across resource kinds or contexts does not imply shared identity.

Deletion permanently invalidates the handle and removes its live lookup entry. A later object using the same numeric ID receives a new handle. An old handle must never be retargeted or become valid again. Context retirement likewise invalidates its handles without issuing native deletion into another context.

Holding or releasing a handle does not control native ownership. The handle has no native-deleting disposal or finalizer; ordinary managed reference release is sufficient. The registry and notification associations must not indefinitely retain abandoned wrappers. Use non-owning wrapper associations with explicit removal on wrapper disposal and cleanup of expired associations.

Resource identity follows the project's existing registered-context model. Shared-context namespace generalization is outside this proposal; preserve current context restrictions.

Initial acquisition of an existing external object validates its kind and obtains current metadata. Continued validity relies on the covered StateCache command paths. Creation interception is useful where already present, but a producer-specific allocation certificate is not required merely to register a live external object whose subsequent mutation and deletion are observed. Unsupported external paths must not silently acquire persistent tracking guarantees.

## Ownership and wrapper lifecycle

`GpuResource` retains ownership policy. Owning abstractions such as `GpuTexture` and `GpuVao` continue to request native deletion through their existing deletion/scheduling flow. `BorrowedTexture` remains non-owning. Sharing a handle never grants another wrapper deletion authority or creates a second owner.

Disposing a borrowed wrapper removes that wrapper's association and retires its local state. It neither invalidates the shared handle nor deletes or unbinds the native resource. Other wrappers remain usable.

Disposing an owner preserves existing retirement checks and scheduling semantics. A scheduled deletion must prevent further managed use of the retiring allocation, while the eventual native delete still applies the existing StateCache binding effects. Queued work must retain the originating identity and must never retire a later incarnation that reuses its numeric name.

Native deletion observed independently of an owning wrapper invalidates the shared handle. A later owner disposal must not delete a replacement object using the old ID. Wrapper validity combines local disposal state with shared handle validity.

`Detach` continues to transfer ownership, not destroy the object. It retires the detaching wrapper while preserving the live object's identity for remaining references. Borrowed wrappers continue to reject ownership transfer. Subsequent deletion by the recipient must pass through the supported observation path for those references to remain tracked.

Existing pass and lifetime guards retain their rules. Move only the state that must be shared across aliases into the handle, and reuse existing enforcement points. This proposal does not introduce another guarding policy or duplicate pre-mutation checks.

## Metadata notifications

Add an abstract `HandleMetadataChange` contract to `GpuResource`. Supply a change description identifying the affected metadata family and, where available, values established by the command. Each resource type explicitly defines how it updates or refreshes its represented metadata; types without applicable mutable metadata explicitly handle that case.

StateCache dispatches notifications for metadata-altering commands to all live wrappers associated with the affected handle. Texture storage dimensions, format and allocated mip information; buffer storage size; and represented VAO configuration are examples. The exact coverage is derived from metadata actually held by the existing wrappers.

For bound-target commands, resolve the affected object from the applicable context binding state. For direct-state-access commands, use their explicit object ID. Preserve existing unknown-state reconciliation rather than guessing the target from stale cache entries.

Publish updated metadata after the native operation. Derive metadata from command arguments where the result is established; query only when required to determine the actual resulting metadata. A rejected operation must not publish an assumed successful result. Preserve existing error handling, and make metadata unavailable when a refresh cannot establish valid state. One failing refresh must not leave other associated wrappers silently presenting stale metadata as current.

Replacing storage on a surviving GL object keeps its object handle identity but changes its metadata revision. Wrappers must expose the current metadata before their next dependent operation. Existing shader/input validation history must account for that revision wherever it previously assumed metadata was immutable for a wrapper. Previously validated submissions cannot silently reuse incompatible dimensions or formats after a change.

Notifications cover metadata only. Producer content readiness and frame qualification retain their existing contracts.

## StateCache integration and coverage

Use one common resource mechanism for owned and borrowed wrappers. Keep resource-specific binding effects in the corresponding StateCache implementations. Deleting a texture still records its actual implicit unbinding and invalidates affected image/restoration knowledge; disposing a non-owning wrapper does not imply those native effects.

Extend the existing GL redirection adapters for the concrete metadata mutations and deletion signatures needed by the supported wrappers and native consumers. Reconcile VGE's own direct GL calls with that same route. Cover relevant overloads and list-deletion paths, preserving existing command ordering and behavior. Do not add producer teardown hooks to duplicate notifications available at the GL command boundary.

Notification registration, dispatch and retirement follow the existing rendering-thread and context rules. The resource lookup is separate from binding knowledge: a valid handle does not prove that an object is currently bound. Existing engine-boundary capture, invalidation and restoration remain authoritative.

## Consumer integration

Use the common handle mechanism to retain borrowed native cloud-map and depth wrappers across stable resource lifetimes. Share an existing owned material-depth texture directly where the consumer already accepts `GpuTexture`. Eligible raw sampler inputs can use the typed wrapper contract and ordinary texture binding cache.

Cloud publication, world/owner selection, upload completion and frame/readiness checks remain integration responsibilities. Review existing allocation hooks according to those responsibilities: remove lifecycle-only work made redundant by StateCache, while retaining observations still required to establish valid cloud content.

Preserve conservative handling for raw IDs whose mutation/deletion paths are not covered. Do not infer persistent tracking merely from a successful `GL.IsTexture` check.

## Scope and layout

The proposal covers the shared handle extraction, integration with existing `GpuResource` implementations, metadata notifications for represented mutable metadata, command-path coverage, and the cloud/depth consumers that motivated persistent borrowing. It preserves resource ownership, render order, shader algorithms and content publication semantics.

Keep handle identity, common StateCache registration/dispatch, typed metadata handling and native call adaptation in files with those individual responsibilities. Do not add a second binding cache, a parallel shader validation cache, or a producer-specific lifetime registry.

Permanent architecture documentation should be reconciled with implemented behavior when implementation occurs.

## Acceptance evidence

The implementation must demonstrate:

- Owning and borrowed aliases share identity; borrowed disposal leaves native storage and other aliases intact; releasing a handle performs no native deletion.
- Deletion invalidates all aliases, reused names receive fresh identity, kinds and context generations do not collide, and delayed disposal/deletion cannot affect a replacement.
- Existing pass, lifetime, deferred-deletion and detach behavior remains correct with shared handles.
- Covered metadata commands update every associated wrapper, including bound-target and direct-state-access paths where used. Failed operations and refresh failures cannot leave metadata falsely current.
- Metadata revisions invalidate dependent validation appropriately while stable resources retain existing texture-bind suppression.
- Context retirement, engine-boundary restoration, unknown binding state and unrelated bindings retain their existing correctness guarantees.
- Native integration coverage is exercised through the actual redirection mechanism, including the cloud/depth mutation and deletion paths. No dedicated borrowed-texture registry or lifecycle-only teardown hook is required.
- Retained wrapper counts remain bounded through disposal, collection and context retirement.

Measure actual metadata-query and native-bind counts for the migrated consumers against the current implementation. Distinguish those savings from registration/notification costs. Headless correctness and call-count evidence do not establish in-game appearance or frame-time improvements.

Builds and tests are executed through subagents under the repository workflow. Approval of this proposal precedes creation of its implementation checklist.
