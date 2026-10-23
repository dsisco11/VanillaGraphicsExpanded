# Lifecycle-aware borrowed texture binding caching

Status: approved. Implementation plan: [Rendering.BorrowedTextureCaching.todo](Rendering.BorrowedTextureCaching.todo).

## Intent

Preserve texture-unit cache knowledge for externally owned rendering inputs whose allocation lifetime VGE can reliably observe. An unchanged live allocation should use the existing StateCache comparison and avoid redundant OpenGL texture binds. Replacement, retirement and native state changes must continue to invalidate the applicable knowledge.

Reuse the existing typed texture contracts, resource lifetime guards, publication certificates and context-wide StateCache. Keep conservative invalidation for raw integer handles without a trustworthy lifetime contract. Expected benefits are fewer actual texture binds and less repeated borrowed metadata acquisition; GPU execution and frame-time benefits require measurement.

This document proposes the design and acceptance criteria. It does not authorize runtime changes or prescribe an implementation checklist.

## Current behavior

- [ShaderPreparedSubmission](../VanillaGraphicsExpanded/Rendering/ShaderPreparedSubmission.cs) checks raw sampler IDs with `GL.IsTexture` and invalidates the texture unit before publishing each active integer input. An equal integer cannot distinguish a deleted allocation from a replacement reusing its name. Managed texture inputs already publish through the ordinary cache without that unconditional invalidation.
- [BorrowedTexture](../VanillaGraphicsExpanded/Rendering/BorrowedTexture.cs) captures target, dimensions, format and mip metadata using driver queries. It does not own native storage and rejects handle transfer. Its caller must retire the wrapper when external storage changes; creating a wrapper alone does not establish reliable lifecycle observation.
- [CloudStoredVolumeConsumer](../VanillaGraphicsExpanded/PBR/Clouds/Volume/CloudStoredVolumeConsumer.cs) creates synchronous borrowed wrappers for the native map, material depth, visibility depth and liquid depth. Material depth comes from a VGE-owned `DynamicTexture2D`, but is converted to an integer and wrapped again. The volume bindings already accept `GpuTexture`; they are not raw integer sampler inputs.
- [ICloudSimpleBindings](../VanillaGraphicsExpanded/PBR/Clouds/ICloudSimpleBindings.cs) still exposes liquid depth as an integer sampler input.
- [CloudMapPublication](../VanillaGraphicsExpanded/PBR/Clouds/CloudMapPublication.cs) already distinguishes allocation, upload, shader and completed generation certificates. Its adapter observes allocation entry before native deletion, including same-size replacement and name reuse.
- [DepthHierarchyRenderer](../VanillaGraphicsExpanded/Rendering/DepthHierarchy/DepthHierarchyRenderer.cs) retains a borrowed primary-depth wrapper and exposes a frame-qualified owned result. Its existing acquisition and retirement behavior must be reconciled with the same native-target lifetime contract.
- [ScreenResourceManager](../VanillaGraphicsExpanded/Rendering/ScreenResourceManager.cs) invalidates StateCache after native framebuffer rebuild and before dependent callbacks. [StateCache.TextureLifetime](../VanillaGraphicsExpanded/Rendering/StateCache.TextureLifetime.cs) records owned texture deletion; borrowed wrapper disposal does not perform that deletion path.

Consequently, raw-ID bind suppression and persistent borrowed metadata are related but distinct improvements. Reusing an already typed volume input may save metadata queries without reducing its texture binds. Receipts must distinguish these outcomes.

## Resource identity and authority

A tracked borrowed texture represents one allocation in one live graphics context. Its identity includes the external owner, owner allocation generation and context generation. The native ID and immutable metadata describe that allocation but cannot certify its identity. Dimensions, framebuffer object reference and `GL.IsTexture` are insufficient evidence of continuity.

Retain one borrowed metadata wrapper per admitted allocation, using the existing `GpuTexture` contract. Replace the wrapper when the allocation changes instead of mutating an old wrapper into a new allocation. Old retained shader inputs must become invalid, including when the replacement has identical dimensions, format and integer ID. This preserves the existing validation history's resource-object distinction without introducing a parallel validation cache.

The integration observing the external producer owns the wrapper and its retirement. Consumers receive a reference valid only within the established synchronous rendering contract; they neither own native storage nor extend its lifetime beyond the external owner's guarantee. A persistent wrapper is metadata, not a promise that external storage cannot disappear.

Admission requires a verified notification boundary covering every relevant allocation replacement and teardown path. Acquire metadata only after successful allocation validation. Unknown late acquisition or an unobserved replacement withdraws eligibility until the adapter can establish a valid lifetime contract. Inputs lacking that contract retain conservative publication; do not turn an arbitrary integer into a supposedly tracked resource by wrapping it once.

## Proposed consumer behavior

Pass the existing VGE-owned material-depth texture directly to volumetric cloud view inputs. Preserve `DepthHierarchyRenderer.TryGetCurrent` frame, view and extent qualification; sharing the object does not transfer disposal authority or permit a consumer to retain stale frame content.

Retain native map metadata under the existing cloud allocation certificate. Allocation entry withdraws the old reference before native storage changes; successful completion admits a fresh wrapper. Keep completed map generation, upload validity and same-frame checks independent. Updating texels within the same allocation does not itself require rebinding the same texture, but a missing or stale content certificate still rejects the draw.

Provide native primary and liquid depth through the owning rendering integration's lifecycle references. Reuse those references for Simple clouds, volumetric clouds and the existing borrowed primary-depth consumer where their contracts agree. Change the eligible Simple liquid-depth binding to the existing typed texture contract. Do not add a general registry keyed by integer handles or duplicate native depth images.

Framebuffer rebuild observation must cover same-size replacement and reused names. The current post-rebuild resize callback is a reconciliation boundary, not proof of advance retirement. Verify the producer's actual rebuild entry/completion ordering and prevent borrowing during the replacement interval. If existing hooks cannot provide this guarantee, retain conservative handling for those inputs and report the uncovered lifecycle path before expanding integration scope.

## Cache coherence and restoration

StateCache remains the sole authority for context binding state. A tracked allocation permits reuse of known slot state; it does not prove that native rendering has left the texture bound. Existing engine-boundary capture, invalidation and restoration remain authoritative after intervening native calls. Unknown slots bind normally even when allocation identity is unchanged.

When a tracked external allocation is withdrawn, forget every cached texture association referring to its native name, and any image association if that allocation can be image-bound. Integrate with existing boundary retirement checks so a saved restoration cannot bind a retired allocation or its same-name replacement. Invalidation must happen before the next cache observation or restoration, not merely when the replacement is first sampled. Preserve unrelated known bindings when retirement is specific; retain existing broad invalidation where native rebuild or context changes make broader knowledge uncertain.

External retirement differs from owned deletion: it revokes metadata and cache/restoration knowledge without calling `GL.DeleteTexture`, enqueueing deletion, transferring the handle, or claiming that native deletion has already unbound it. Extend the existing lifetime/cache machinery only as needed to express that distinction. A synchronous borrow overlapping attempted replacement must be rejected or reconciled before native mutation; managed guards alone cannot stop an external producer.

Check current allocation eligibility during input validation and again at publication where intervening activity is possible. Preserve complete-input validation before publication and existing failure cleanup. Shader reload invalidates executable compatibility and content certificates as required; it need not invent a new storage allocation when storage survives. World/context teardown withdraws all associated borrowed references before subsequent use, and no binding knowledge crosses contexts.

## Scope boundaries

The proposal covers sampler publication and lifecycle references for the cloud inputs described above, including their shared depth acquisition. Inventory other raw sampler inputs to distinguish eligible references from intentionally untracked handles; migration of unrelated rendering domains requires separate scope review.

Preserve render order, cloud math, visibility rejection, sampler policy, image content publication, resource ownership and failure behavior. Do not add texture copies, new shader algorithms, a second state cache, per-frame metadata queries, or general engine resource interception. Existing raw integer publication remains supported.

## Acceptance evidence

Use subagents for builds and test execution. Focused GPU coverage must establish:

- Repeated publication of the same eligible allocation skips actual texture binds when the required slot state is already known. Unknown or externally changed state still binds correctly.
- Raw untracked IDs continue to invalidate and bind conservatively.
- Resize, same-size native target replacement, deliberately reused names, failed allocation, reload, world retirement and context replacement reject stale references. Metadata and compatibility validation refresh on the appropriate changes, with failed validation unable to publish partial inputs.
- Borrowed retirement invalidates affected cached/restored references without deleting native textures. Owned texture disposal retains its existing deletion behavior. Active borrows, exception cleanup, texture/sampler state and engine-boundary restoration preserve their contracts.
- Frame/content freshness remains independent of allocation identity. Stale map publications and stale depth views still reject rendering, and cloud visibility rejection and boundary outputs remain unchanged.

Compare a fixed candidate against its baseline using matched cloud scenes, inputs and settings. Count actual driver texture binds separately from cache checks, metadata queries and compatibility checks; include entry/exit restoration costs. Report Simple and volumetric results separately. Compare cloud output and relevant restored GL state, including after native interference and replacement. Do not claim a bind saving from wrapper reuse alone, or a frame-time saving from bind counts alone.

Focused/headless evidence establishes binding and output contracts under the tested fixtures. Live appearance and frame-time acceptance remain separate user-run evidence. Keep the originating task open until the agreed correctness and measurement criteria are satisfied.

## Decision requested

Approve producer-owned persistent borrowed references using existing typed texture inputs, with cache eligibility tied to verified allocation and context lifetimes. Preserve conservative raw-ID handling wherever lifecycle coverage cannot be established. No implementation plan or runtime changes accompany this proposal.
