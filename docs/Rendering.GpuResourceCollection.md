# GPU resource collections

`GpuResourceCollection` owns explicitly registered GPU resources. It disposes all framebuffers before other resources, deduplicates registration by object identity, and delegates actual GL deletion to `GpuResource`. Disposal is idempotent; if a resource release throws, remaining releases are attempted and errors are aggregated.

Borrowed resources are not registered. Framebuffers in a collection must use non-owning attachments so each texture has one disposal owner. The collection does not infer ownership from a GL name or framebuffer attachment, inspect GL validity, subscribe to screen events, or resize resources.

## Typed allocations

- `DirectLightingTargets` owns the three linear radiance textures and their MRT framebuffer. It preserves framebuffer binding around allocation and explicit in-place resize, and validates completeness before readiness is published.
- `LumOnTargets` owns one allocation of probe-grid, atlas, half/full-resolution, and mipmapped targets. Its manager resolves dimensions and controls recreation, history invalidation, and lighting publication. Temporal swaps change role references without changing the owned collection.
- `GBufferTextures` registers its four supplementary terrain textures before configuring sampling. Borrowed engine position and the optional owned fallback position remain under `GBufferManager`'s existing attachment policy.

Each allocation constructor cleans up already registered resources if a later allocation or setup operation throws. Managers retain their public properties and existing `EnsureBuffers` return semantics. Direct lighting resizes in place; LumOn recreates its allocation. Engine attachment rebinding and screen callback dependency order remain owned by the existing framebuffer lifecycle hooks and `ScreenResourceManager`.

## Validation scope

Focused GPU coverage checks resource ownership, FBO-before-texture retirement, repeated disposal, cleanup after allocation/disposal failures, borrowed texture survival, direct-light handle stability during resize, and LumOn temporal role swaps/recreation/publication. The engine framebuffer rebuild regression remains part of the validation set. These checks use a headless GL context; no game process is launched.

Allocation review confirmed all 36 LumOn factory expressions retain their formats, filters, labels, and arguments after extraction. Probe-grid and half-resolution formulas remain in the manager; atlas and mip formulas remain with allocation.

Validation result: **37 passed, 0 failed, 0 skipped**, including the existing 30 framebuffer/PBR regressions and seven ownership/failure tests. Log: `artifacts/resource-collection-focused.log`. SPIR-V compilation succeeded; no new warnings were reported for changed files.

```powershell
dotnet test .\VanillaGraphicsExpanded.Tests\VanillaGraphicsExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~GBufferExternalFramebufferTests|FullyQualifiedName~PbrModeLifecycleTests|FullyQualifiedName~PbrDirectLightingShadowTests|FullyQualifiedName~PbrTerrainCaptureGpuTests|FullyQualifiedName~EngineFramebufferRebuildTests|FullyQualifiedName~GpuResourceCollectionTests|FullyQualifiedName~DirectLightingBufferOwnershipTests|FullyQualifiedName~LumOnBufferOwnershipTests" -v minimal
```

The sandbox package-path retry used `NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages`, where the installed shader compiler package was available.
