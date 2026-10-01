# Shader preparation migration inventory

Snapshot: 2026-10-01. This is a source inventory, not a claim that these consumers are migrated.
Implementation update: shared graphics activation and contract-generated submission are implemented for upsample and probe anchor. Other entries remain migration work, including remaining authored setters and compute/engine adapters. Explicit compatibility Submit implementations preserve their existing publication paths until that work is complete.
Contract: [Rendering.ShaderPreparation.md](Rendering.ShaderPreparation.md).
Bulleted file paths are relative to the repository root. Infrastructure table paths beginning Rendering/ or LumOn/ are inside VanillaGraphicsExpanded/. Partial declarations are grouped by their owning type during implementation; static declarations are not runtime shader instances.

## Infrastructure and generators

| Owner | Required migration |
| --- | --- |
| Rendering/Shaders/GpuProgram.cs, GpuProgram.Preparation.cs | Persistent inputs; generated Submit; Use/TryUse/UseScope and engine interface routing; activation failure and restoration semantics |
| Rendering/Shaders/GpuProgram.Options.cs, GpuProgram.Spirv.cs | Preserve option transaction and executable installation; retain runtime inputs across reload |
| LumOn/Shaders/LumOnShaderProgram.cs; Rendering/Shaders/VgeShaderProgram.cs | Shared runtime resource getters consumed by generated Submit; persistent common inputs |
| Rendering/CpuUniformBuffer.cs, GpuUniformRingSystem.cs, GpuUniformRingBuffer.cs | Submission-time mutation checks; explicit publication result; successful dirty consumption and unchanged ring lifetime rules |
| Rendering/GpuProgramLayout.cs, ShaderBindingAccess.cs, GlStateCache.Bindings.cs | Keep active-resource resolution and authored slot/target/sampler policy; submission uses existing binding owners |
| Rendering/GpuComputePipeline.cs and .Preparation.cs | Retain executable ownership; wrap production consumers with the common runtime submission contract |
| ShaderContractGenerator/ShaderDeclarationGenerator.cs, DeclarationEmitter.cs | Emit Submit for runtime owners from binding resource sources; diagnose unsubmitable resources; exclude offline/static shells |
| ShaderContractGenerator/InterfaceBindingReader.cs, BindingReader.cs | Replace generated immediate resource setters with retained inputs and explicit submission helpers; preserve interfaces and texture-ID policy |
| ShaderContractGenerator/OptionReader.cs; Rendering/Contracts/ShaderSettingsEditor.cs | Keep generated option assignments on the existing transaction path |

## Runtime graphics owners

All files in this list own runtime shader state or its inherited contract. Move parameter, raw uniform and resource setters to staging, and migrate associated layouts/partial files with their owner. Preserve conditional resource policy rather than binding every declared variant resource unconditionally.
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnCombineShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnDebugShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnHzbCopyShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnHzbDownsampleShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnProbeAnchorShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnProbeAtlasPisMaskShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnProbeSh9GatherShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasFilterShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasGatherShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasProjectSh9ShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasProjectSHShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTemporalShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTraceShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnUpsampleShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnVelocityShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnWorldProbeClipmapResolveShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnWorldProbeRadianceTileResolveShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/Liquids/LiquidDepthShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/Liquids/LiquidShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/Materials/PbrHeightBakeShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/PBRCompositeShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/PBRDirectLightingShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/PBRDisplayResolveShaderProgram.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/Fixtures/GeneratedAccessorShader.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/Fixtures/GeneratedResourceBindingShader.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/Fixtures/GeneratedTextureImageShader.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/VgeDebugLinesShaderProgram.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/VgeShaderProgram.cs`
- `VanillaGraphicsExpanded/Rendering/Shaders/VgeWorldProbeOrbsPointsShaderProgram.cs`

## Runtime compute consumers

These files reference GpuComputePipeline directly. The six Scene/Shaders compute wrappers already provide natural runtime owners. SurfaceLightingDispatch, SurfaceLightingQueryBatch, LumonSceneIrradianceHistory, world-probe batch/commit/invalidation owners and AtmosphereGpuComputation combine execution with bindings and need explicit per-program submission ownership. Declaration-only compute classes remain descriptors. GpuComputePipeline itself is infrastructure, not a shader consumer.
- `VanillaGraphicsExpanded/LumOn/Scene/LumonSceneIrradianceHistory.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneCaptureMeshCardComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneCaptureVoxelComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackCompactPagesComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackGatherComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackMarkPagesComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneRelightVoxelDdaComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/SurfaceLightingDispatch.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/SurfaceLightingQueryBatch.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuResources.Invalidation.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/WorldProbeHybridCommit.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/WorldProbeTraceBatch.cs`
- `VanillaGraphicsExpanded/PBR/Atmosphere/AtmosphereGpuComputation.cs`
- `VanillaGraphicsExpanded/Rendering/GpuComputePipeline.cs`
- `VanillaGraphicsExpanded/Rendering/GpuComputePipeline.Preparation.cs`

## Submission callers and shared input producers

The following production files contain activation/dispatch entry points or parameter-publication calls. They are candidate migration sites, not all immediate-upload defects. Inspect each owning render sequence: prepare options and retained inputs first, activate/submit next, then draw or dispatch. Helpers using UseScope for uniform updates must not accidentally recurse into Submit. Infrastructure activation/restoration and low-level GL primitives remain infrastructure.
- `VanillaGraphicsExpanded/DebugView/Views/VgeGBufferOverlayDebugView.cs`
- `VanillaGraphicsExpanded/DebugView/Views/VgeWorldCellBoundsDebugView.cs`
- `VanillaGraphicsExpanded/HarmonyPatches/TerrainLumonSceneChunkSlotUniformBindingHook.cs`
- `VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs`
- `VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/LumonSceneFeedbackUpdateRenderer.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/LumonSceneIrradianceHistory.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/LumonSceneMeshCardCaptureDispatcher.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneCaptureMeshCardComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneCaptureVoxelComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackCompactPagesComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackGatherComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneFeedbackMarkPagesComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/Shaders/LumonSceneRelightVoxelDdaComputeShader.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/SurfaceLightingDispatch.cs`
- `VanillaGraphicsExpanded/LumOn/Scene/SurfaceLightingQueryBatch.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnCombineProgramLayout.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnCombineShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnDebugProgramLayout.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnDebugShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnHzbDownsampleShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnNearFieldVisibilityBindings.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnProbeAnchorShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnProbeSh9GatherShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasFilterShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasGatherShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTemporalShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTraceShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTraceShaderProgram.NearField.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnUpsampleShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnWorldProbeClipmapResolveShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/Shaders/LumOnWorldProbeRadianceTileResolveShaderProgram.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuResources.Invalidation.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/WorldProbeHybridCommit.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/WorldProbeTraceBatch.cs`
- `VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs`
- `VanillaGraphicsExpanded/PBR/Liquids/LiquidDepthRenderer.cs`
- `VanillaGraphicsExpanded/PBR/Liquids/LiquidRenderer.cs`
- `VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs`
- `VanillaGraphicsExpanded/PBR/PbrCompositeProgramLayout.cs`
- `VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs`
- `VanillaGraphicsExpanded/PBR/PBRCompositeShaderProgram.cs`
- `VanillaGraphicsExpanded/PBR/PbrDirectLightingProgramLayout.cs`
- `VanillaGraphicsExpanded/PBR/PBRDirectLightingShaderProgram.cs`

## Engine adapters and resource families

- PBR/Liquids/LiquidRenderer.cs, LiquidDepthRenderer.cs and LiquidShaderProgram.EngineInterface.cs: frame preparation followed by engine mesh-pool submission; origin/model-view/transparency callbacks need a pre-draw bridge. LiquidDepthShaderProgram contains its own interface adapter.
- HarmonyPatches/TerrainDisplacementPoolHooks.cs: existing MeshDataPool.RenderMesh and manager hooks must retain ordering with the liquid bridge. vsapi/Client/MeshPool/MeshDataPool.cs:465 forwards the actual pool draw; manager and mini-dimension ordering still require migration-time verification.
- HarmonyPatches/AtmosphereShaderBindingHook.cs, TerrainLumonSceneChunkSlotUniformBindingHook.cs and other engine binding hooks: vanilla shader adapters remain outside the owned-shader inheritance contract. Preserve their explicit engine publication boundaries.
- LumOn/LumOnUniformBuffers.cs, near-field visibility bindings, scene SurfaceLightingBindings/geometry binding helpers, PBR material and atmosphere bindings: distinguish externally uploaded shared buffers from owned CPU parameter UBOs. Store references in prepared inputs; do not transfer ownership or duplicate shared uploads.
- Texture/sampler setters (including raw texture IDs), image bindings, SSBO ranges, UBO references, atomic counters and raw Uniform/UniformMatrix/UniformMatrixArray methods all need staging or an explicit low-level execution classification. Counter clears, barriers and readbacks stay execution operations.
- PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs and PbrHeightBakeShaderProgram.cs: preserve family-selected contracts and pass-by-pass inputs; descriptors in PbrNormalDepthBakeShaderProgram.cs are static.
- DebugView/Views and Rendering/Shaders/VgeDebugLinesShaderProgram.cs/VgeWorldProbeOrbsPointsShaderProgram.cs: preserve lazy program preparation and retained debug settings while moving publication to Use.

## Fixtures, tests and documentation

Migrate concrete fixture owners in Rendering/Shaders/Fixtures, including GeneratedResourceBindingShader and GeneratedTextureImageShader which have no ShaderProgram attribute. Static fixture declarations remain descriptors. Nested test-local subclasses retain explicit Submit implementations: GPU/DriverProgramCacheTests.cs, EngineShaderDebugLabelsTests.cs, ShaderDigestIndexCacheTests.cs, ShaderLinkBatchTests.cs, SpirvGraphicsLifecycleTests.cs, and Unit/Rendering/Contracts/ShaderOptionBatchTests.cs under VanillaGraphicsExpanded.Tests. Include engine-interface activation paths in their migration. Nested fixtures currently supply an explicit Submit because top-level generation does not own nested declarations. Top-level fixtures can use generated Submit after declaring their runtime resource sources.

Relevant tests include CpuUniformBufferTests, UniformBufferCallerTests, UboPackingTests, shader declaration/generator suites, GPU/GpuUniformRingBufferIntegrationTests, GPU/TestUniformRingRetirementTests and existing program preparation/reload/layout/state-cache suites. Update the generated-resource fixtures to prove setters make no GL calls and Submit does the binding. Execute builds/tests through subagents only during implementation.

Update docs/ShaderAuthoring.md after API implementation; preserve the executable readiness rules in docs/GPU.ShaderDemandLoading.md. The first task adds this contract/inventory without changing those current-runtime descriptions.

## Reconciliation searches

Repeat these searches during migration and classify remaining matches; a textual hit is not by itself a defect:

```powershell
rg -n 'class .*:.*(GpuProgram|LumOnShaderProgram|VgeShaderProgram)' VanillaGraphicsExpanded VanillaGraphicsExpanded.Tests
rg -l 'GpuComputePipeline' VanillaGraphicsExpanded --glob '*.cs'
rg -n 'BindTo\(|BindParamsUbo\(|PublishDraw\(|ApplyInputs\(|UploadOrResize\(' VanillaGraphicsExpanded --glob '*.cs'
rg -n '\.(TryUse|UseScope|Use|DispatchBound|DispatchIndirect)\(' VanillaGraphicsExpanded --glob '*.cs'
rg -n 'UniformMatrix|BindTexture|BindExternalTexture|BindImage|BindRange|BindBase' VanillaGraphicsExpanded --glob '*Shader*.cs'
```

The generated path lists were produced from source files excluding obj/bin; they include all identified direct GpuProgram/LumOnShaderProgram/VgeShaderProgram subclasses (including the qualified VgeWorldProbeOrbsPointsShaderProgram base) and direct GpuComputePipeline references found in the production tree. The caller list includes non-Rendering owners with the activation/publication patterns above; Rendering callers, generated code and engine callbacks are covered separately and must not be inferred absent from that list.
