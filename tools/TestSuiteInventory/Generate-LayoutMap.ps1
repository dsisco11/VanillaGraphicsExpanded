param(
    [string] $InventoryRoot = 'docs/testing-inventory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
Returns the principal level for a route with configuration-specific or mixed native behavior.
#>
function Get-PrincipalLevel {
    param([string] $RecordedLevel)
    if ($RecordedLevel -match '^Unit') { return 'Unit' }
    if ($RecordedLevel -match '^Component') { return 'Component' }
    if ($RecordedLevel -match '^Integration') { return 'Integration' }
    throw "Unrecognized level: $RecordedLevel"
}

<#
.SYNOPSIS
Assigns a stable subject directory using the reviewed owner and source location.
#>
function Get-Domain {
    param([string] $Project, [string] $SourcePath, [string] $Owner)
    if ($Project -eq 'OpenGLInterceptionGenerator.Tests' -or $Project -eq 'ShaderContractGenerator.Tests') { return 'Generation' }
    if ($Project -eq 'ShaderBuildTool.Tests') {
        if ($Owner -match 'receipt' ) { return 'Spirv/Build' }
        if ($Owner -match 'cache|record|identity|hash' ) { return 'Spirv/Cache' }
        if ($Owner -match 'compiler|compilation|process' ) { return 'Spirv/Compiler' }
        if ($Owner -match 'source|preprocess|dependency|GLSL|shader text' ) { return 'Spirv/Source' }
        return 'Spirv/Build'
    }
    $relative = $SourcePath.Substring($Project.Length + 1)
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/AtmosphereLookupTests.cs' -and $Owner -match 'diagnostic') { return 'Atmosphere/Diagnostics' }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/PackagedShaderAssetTests.cs' -and $Owner -match 'published shader artifact') { return 'Rendering/Shaders' }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/ShaderRegistryTests.cs' -and $Owner -match 'published shader catalog source') { return 'Rendering/Shaders' }
    if ($relative -match '^Unit/([^/]+)(?:/([^/]+))?/' -and $Matches[1] -notin @('Rendering', 'PBR')) {
        $first = $Matches[1]
        $second = $Matches[2]
        if ($first -eq 'LumOn' -and $second -eq 'LumonScene') { return 'LumOn/Scene' }
        if ($first -eq 'LumOn' -and $second -eq 'WorldProbes') { return 'LumOn/WorldProbes' }
        if ($first -eq 'LumOn' -and $second -eq 'Scene') { return 'LumOn/Scene' }
        if ($second -and $first -notin @('WorldPartition', 'Noise', 'Cache')) { return "$first/$second" }
        return $first
    }
    if ($relative -match '^Unit/PBR/Materials/WorldProbes/') { return 'PBR/Materials/WorldProbes' }
    if ($relative -match '^Unit/PBR/Materials/') { return 'PBR/Materials' }
    if ($relative -match '^Unit/Rendering/Contracts/') { return 'Rendering/Contracts' }
    $subject = [System.IO.Path]::GetFileNameWithoutExtension($relative) + ' ' + $Owner
    switch -Regex ($subject) {
        'Atmosphere|Scattering|AerialLookup' { return 'Atmosphere' }
        'AmbientOcclusion|\bAO\b|^Ao|Ssao' { return 'AmbientOcclusion' }
        'Water|Liquid' { return 'Water' }
        'Cloud|DynamicVolume' { return 'Clouds' }
        'LumOn|Lumon|WorldProbe|NearField|SurfaceLighting|SurfaceCache|TraceGeometry|SurfaceCapture|SurfaceFallback|SurfaceHit|SurfaceTraversal|SurfaceUpdate|SurfaceWork|SurfacePage|SharedTrace|SharedGeometry|RuntimeWait' { return 'LumOn' }
        'Terrain|Tessellation' { return 'Terrain' }
        'Celestial|Solar|Moon|SunDisk|Sky' { return 'Celestial' }
        'Material|Pbr|Microfacet|Brdf|BaseColor' { return 'PBR/Materials' }
        'Exposure|Bloom|ColorGrading|FinalDisplay|Filmic|VisualAdaptation|DepthHierarchy|SceneColor|Oit|Fog|Postprocess|Composite' { return 'Postprocessing' }
        'Config|Setting|ShadingQuality' { return 'Configuration' }
        'Vector|Matrix|SIMD|PooledArray' { return 'Numerics' }
        'DebugView|DebugViewer|DebugPanel|Debug-view' { return 'Rendering/Debug' }
        'Photometric|HeldLight|FrameLighting|DirectLighting|Lighting|FrameCamera' { return 'Lighting' }
        'Resource|Owner|Uniform|Ubo|Submission|Format|Intercept|OpenTK|GlError|Viewport|Raster|Prepared|Compute|ArrayTexture|Binding|Storage|Collection|Lifecycle|Knowledge|EngineEarlyDeletion|EngineStartupContext|ReadOnlyDepth|SceneCompatibility|StandaloneNumeric|StaticTarget' { return 'Rendering' }
        'Shader|Spirv|Program|Binding|Pipeline' { return 'Rendering/Shaders' }
        'Gpu|RHI|Framebuffer|Texture|Buffer|State|Render|Graphics|Image|Vao|Vbo|Ebo' { return 'Rendering' }
        'Partition|Chunk|Voxel' { return 'WorldPartition' }
        'Profil|EventSource|Counter' { return 'Profiling' }
        'Cache' { return 'Cache' }
        'Noise|Sample' { return 'Noise' }
        default {
            if ($relative -match '^Unit/Rendering/') { return 'Rendering' }
            if ($relative -match '^Unit/PBR/') { return 'PBR' }
            return 'Core'
        }
    }
}

<#
.SYNOPSIS
Forms a valid behavior suffix without treating punctuation in owner labels as a namespace.
#>
function Get-OwnerSuffix {
    param([string] $Owner)
    $words = [regex]::Matches($Owner, '[A-Za-z0-9]+') | ForEach-Object { $_.Value }
    return ($words | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join ''
}

<#
.SYNOPSIS
Applies the reviewed behavior names for the first mixed migration batches.
#>
function Get-ReviewedClassName {
    param([string] $SourcePath, [string] $Owner, [string] $Level, [string] $DefaultName)
    switch -Regex ($SourcePath) {
        'VanillaGraphicsExpanded.Tests/Unit/Voxels/ChunkProcessing/ChunkProcessingServicePhase2Tests.cs$' { return 'ChunkProcessingQueueSupersessionTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Voxels/ChunkProcessing/ChunkProcessingServicePhase3Tests.cs$' { return 'ChunkProcessingSnapshotLeaseTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Voxels/ChunkProcessing/ChunkProcessingServicePhase4Tests.cs$' { return 'ChunkProcessingCacheBehaviorTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Cache/ArtifactSystem/ArtifactBackpressurePhase6Tests.cs$' { return 'ArtifactReservationCapacityTests' }
        'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/BaseColorCachePhase6Tests.cs$' { return 'BaseColorCacheBehaviorTests' }
        'VanillaGraphicsExpanded.Tests/Unit/LumOnDebugViewsPhase5UnitTests.cs$' { return 'LumOnDebugViewContractTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Rendering/ProgramBinaryStoreTests.cs$' {
            if ($Owner -eq 'Atomic in-memory cache filesystem') { return 'CountingDiskCacheFileSystemTests' }
            if ($Owner -eq 'Program-binary input preparation') { return 'ProgramBinaryStoreTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/PackagedShaderAssetTests.cs$' {
            if ($Level -eq 'Integration') { return 'PackagedShaderArtifactTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/ShaderRegistryTests.cs$' {
            if ($Level -eq 'Integration' -and $Owner -match 'published shader catalog source') { return 'ShaderCatalogSourceArtifactTests' }
        }
        'VanillaGraphicsExpanded.Tests/AtmosphereLookupTests.cs$' {
            if ($Owner -match 'diagnostic') { return 'AtmosphereLookupCostDiagnosticTests' }
            return 'AtmosphereLookupPublicationTests'
        }
        'VanillaGraphicsExpanded.Tests/AtmosphereScatteringQualityTests.cs$' {
            if ($Owner -match 'quality policy') { return 'AtmosphereQualityPolicyTests' }
            if ($Level -eq 'Unit') { return 'AtmosphereTransportRulesTests' }
            return 'AtmosphereLookupQualityLifecycleTests'
        }
        'VanillaGraphicsExpanded.Tests/PbrTerrainColorPatchesTests.cs$' {
            if ($Level -eq 'Integration') { return 'PbrTerrainInstalledShaderTests' }
            return 'PbrTerrainColorPatchRulesTests'
        }
        'VanillaGraphicsExpanded.Tests/PbrMaterialRegistryPhase8Tests.cs$' { return 'PbrMaterialRegistryResolutionTests' }
        'VanillaGraphicsExpanded.Tests/HeldLightTests.cs$' {
            if ($Level -eq 'Unit') { return 'HeldLightTransformTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuImageAssertionTests.cs$' {
            # Native transfer, selector, and actual context-lifetime cases share one integration owner.
            if ($Level -eq 'Integration') { return 'GpuImageNativeTransferTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuFramebufferExecutableCreationTests.cs$' {
            if ($Level -eq 'Component') { return 'GpuFramebufferBackingAcquisitionTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuFramebufferRoutingInterceptionTests.cs$' {
            if ($Level -eq 'Component') { return 'GpuFramebufferAttachmentPublicationTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/LumOnNearFieldFunctionalTests(?:\.Ring)?.cs$' {
            if ($Level -eq 'Integration') { return 'NearFieldNativeBoundaryTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuEboIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuEboManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuVboIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuVboManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuVaoIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuVaoManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuMappedBufferScopeIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuMappedBufferScopeGuardTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/LumonScenePipelineSmokeTests.cs$' { return 'LumonScenePipelinePublicationTests' }
        'VanillaGraphicsExpanded.Tests/GPU/EngineStateRecompilationGpuTests.cs$' { return 'EngineStateCallerBindingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialDisplacementTests.cs$' {
            if ($Level -eq 'Integration') { return 'MaterialDisplacementSchemaTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/PbrSolarBrdfTests.cs$' {
            if ($Level -eq 'Integration') { return 'SolarShaderArtifactTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeCoordinateSpaceTests.cs$' { return 'WorldProbeLookupPositionCalculationTests' }
        'VanillaGraphicsExpanded.Tests/GPU/ShaderDefineInjectionTests.cs$' { return 'DeclaredShaderVariantLoadingTests' }
        'VanillaGraphicsExpanded.Tests/GPU/WaterRefractionOverlayCompositionTests.cs$' {
            if ($Level -eq 'Unit') { return 'WaterRefractionCapturePolicyTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialAtlasParamsSyncAsyncParityTests.cs$' { return 'MaterialAtlasParamsPageTileParityTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Rendering/ShaderDigestIndexCacheUnitTests.cs$' {
            if ($Level -eq 'Component') { return 'ShaderDigestIndexCacheTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/LumOn/LumonScene/LumonSceneRegionSchedulerIntegrationSmokeTests.cs$' { return 'LumonSceneRegionSchedulingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeTraceEndToEndVoxelArrangementTests.cs$' { return 'WorldProbeVoxelTracingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/WorldPartition/PartitionCoverageTests.cs$' {
            if ($Level -eq 'Unit') { return 'PartitionCoordinateRulesTests' }
            return 'PartitionCoverageTests'
        }
        'VanillaGraphicsExpanded.Tests/CachedObservableTests.cs$' {
            if ($Owner -match 'Rendering-thread dispatch') { return 'RenderThreadDispatchTests' }
        }
        'ShaderBuildTool.Tests/ShaderCacheKeyTests.cs$' {
            if ($Level -eq 'Unit') { return 'ShaderCacheKeyTests' }
            return 'CompilerCachePersistenceTests'
        }
        'ShaderBuildTool.Tests/ShaderCompilerProcessTests.cs$' {
            if ($Level -eq 'Unit') { return 'ShaderCompilerCancellationGuardTests' }
            if ($Owner -match 'receipt') { return 'BuildReceiptReuseTests' }
            return 'ShaderCompilerProcessTests'
        }
    }
    return $DefaultName
}

<#
.SYNOPSIS
Names retained helper-only coordinate cases for the behavior they actually exercise.
#>
function Get-ReviewedMethodName {
    param([string] $SourcePath, [string] $Method)
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/FramebufferAttachmentNotificationTests.cs' -and
        $Method -eq 'ReleasedTransferIntentIsNotRetainedByLiveTargets') {
        return 'CompletedBlitDoesNotPreventTargetResize'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/CommandBindingKnowledgeTests.cs' -and
        $Method -eq 'DirectHelperPreservesAuthoredFactsAndRecoversStaleCopies') {
        return 'DirectVertexBindingHelpersPublishAuthoredStateWithoutPrequeries'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/LumOnDebugViewsPhase5UnitTests.cs' -and
        $Method -eq 'LumOnDebugMode_ProbeAtlasPhase3Modes_AreAppendOnlyAndStable') {
        return 'DebugModeIdentifiersRetainAssignedValues'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialAtlasParamsSyncAsyncParityTests.cs' -and
        $Method -eq 'MaterialParams_SyncVsAsyncTileBuild_ProducesIdenticalAtlas_ForFixedSnapshot') {
        return 'MaterialParams_PageAndReverseOrderTiles_ProduceIdenticalAtlas_ForFixedSnapshot'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/Rendering/GlPipelineDescValidationTests.cs') {
        switch ($Method) {
            'ValidateDesc_BlendEnableIndexed_NormalizesSortOrder' { return 'Constructor_NormalizesBlendEnableIndexedAttachmentOrder' }
            'ValidateDesc_BlendFuncIndexed_SortsByAttachmentIndex' { return 'Constructor_NormalizesBlendFuncIndexedAttachmentOrder' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/BlockAccessorWorldProbeTraceSceneTests.cs') {
        switch ($Method) {
            'Trace_WhenDirectionContainsNaN_ReturnsMiss' { return 'Trace_WhenDirectionContainsNaN_ReturnsInvalid' }
            'Trace_WhenDirectionContainsInfinity_ReturnsMiss' { return 'Trace_WhenDirectionContainsInfinity_ReturnsInvalid' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeSchedulerStressTests.cs' -and
        $Method -eq 'BuildUpdateList_Stress_DoesNotExceedBudgetsOverManyFrames') {
        return 'BuildUpdateList_Stress_RespectsTraceCapOverManyFrames'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/PackagedShaderAssetTests.cs' -and
        $Method -eq 'PackagedBuildDigestsMatchEveryCompiledVariant') {
        return 'PublishedShaderBinariesMatchCatalogDigests'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/Rendering/Contracts/ShaderRegistryTests.cs' -and
        $Method -eq 'SourcesAndRegistryCoverEachOther') {
        return 'PublishedCatalogCoversPackagedShaderSources'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/LumOnCombineFunctionalTests.cs' -and
        $Method -eq 'Composite_AO_ReducesIndirectInCreases') {
        return 'Composite_ReflectivityDoesNotActAsAmbientOcclusion'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/LumonScenePipelineSmokeTests.cs' -and
        $Method -eq 'PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages') {
        return 'FeedbackCaptureAndRelightPublishesMultiplePages'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuVertexSelectionAndMultiBindingTests.cs') {
        switch ($Method) {
            'DoubleHelperConfiguresMultipleAttributes' { return 'NativeDoubleAttributeConfigurationPreservesFormatAndDivisor' }
            'ManagedGeometrySupportsSharedLegacyBinding' { return 'ManagedGeometryCreationLeavesNoGlErrorsWithDirectStateAccessDisabled' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuVertexWrapperMetadataTests.cs' -and
        $Method -eq 'WrapperConfigurationUsesSelectedAddressing') {
        return 'NativeAttributeConfigurationUpdatesCacheWithoutChangingResourceMetadata'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/EngineFramebufferRoutingTests.cs' -and
        $Method -eq 'PatchedNativeRoutingSharesTheOwnedMapping') {
        return 'NativeRoutingCallsUpdateTheOwnedMapping'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/EngineGraphicsGeometryTests.cs' -and
        $Method -eq 'InstalledEngineDrawPreservesSubmissionContract') {
        return 'ImportedEngineGeometrySubmissionPreservesContract'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/EngineStateRecompilationGpuTests.cs' -and
        $Method -eq 'WarmCullCallersTrackBothTransitionsWithoutRewriting') {
        return 'OptimizedCullCallersKeepCacheAndNativeStateSynchronized'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/EngineStateSwitchingGpuTests.cs') {
        switch ($Method) {
            'PatchedDepthCallRestoresPreviouslyCachedValue' { return 'NativeDepthCallRestoresPreviouslyCachedValue' }
            'PatchedTextureBindingPreservesSamplerAndTracksActiveUnit' { return 'NativeTextureBindingPreservesSamplerAndTracksActiveUnit' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialDisplacementTests.cs' -and
        $Method -eq 'SchemaKeepsDisplacementOutsideBrdfAndMappingOverrides') {
        return 'BuiltSchemaKeepsDisplacementOnMaterialDefinitions'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/TerrainSurfaceDetailModeTests.cs' -and
        $Method -eq 'SnapshotTracksOnlyActiveSettings') {
        return 'SnapshotTracksReliefInputsAndSanitizationOrdersFadeBounds'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuProgramLayoutBindingTests.cs' -and
        $Method -eq 'ProgramLayout_TryBindSamplerTexture_UsesLayoutBindingUnit') {
        return 'ComputeDispatchUsesLayoutResolvedSamplerAndImageSlots'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuControlledTexturePublicationTests.cs' -and
        $Method -eq 'MultisampleFallbackPublishesActualSampleCount') {
        return 'MultisampleAllocationPublishesActualSampleCount'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuResourceWrapperLifecycleTests.cs') {
        switch ($Method) {
            'BorrowerConstructionRefreshesExternallyReallocatedTextureAliases' { return 'ExternalTextureReallocationRefreshesSharedAliasesWithoutDescriptorReads' }
            'RenderbufferWrapRefreshesExternallyReallocatedAliases' { return 'ExternalRenderbufferReallocationRefreshesSharedAliasesWithoutDescriptorReads' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuBufferImageCreationTests.cs' -and
        $Method -eq 'RenderbufferFallbackPublishesActualSamples') {
        return 'RenderbufferAllocationPublishesNativeSampleCount'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/MaterialAtlasParamsBuilderNoiseTests.cs' -and
        $Method -eq 'BuildRgb16fPixelBuffers_AppliesNoiseWithinRect_AndIsDeterministic') {
        return 'BuildRgb16fPixelBuffers_IsDeterministicAndVariesInsideCoveredRect'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/ConfigurableRasterizerDescriptionTests.cs' -and
        $Method -eq 'ProfileAndClipCapabilitiesAreEnforced') {
        return 'SupportedProfilesAcceptRasterDescriptions'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GraphicsPipelineDescriptionTests.cs') {
        switch ($Method) {
            'ShaderSpecializationsAndBindingLayoutsChangeIdentity' { return 'DistinctSameTypeExecutableGenerationsChangeIdentity' }
            'AssemblyAndTessellationAreValidatedAndIdentified' { return 'PrimitiveRestartIdentityAndPatchStageValidation' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/ShaderDefineInjectionTests.cs') {
        switch ($Method) {
            'Shaders_CompileAndLink_WithInjectedDefines' { return 'LumOnCombineLoads_WithPerProgramSettings' }
            'GlobalDefinesInclude_Defaults_CompileWithoutInjection' { return 'DefaultDeclaredVariantLoads_WithoutPerProgramOverrides' }
            'LumOnDebugWorldProbe_Compiles_WithWorldProbeEnabledVariant' { return 'WorldProbeDebugVariantLoads_ForEachFeatureState' }
            'LumOnCombine_Compiles_WithVGE_LUMON_ENABLED_Variant' { return 'LumOnCombineLoads_ForEachFeatureState' }
            'LumOnCombine_Compiles_WithPbrCompositeVariants' { return 'LumOnCombineLoads_WithPbrCompositeSettings' }
            'PbrComposite_Compiles_WithToggleVariants' { return 'PbrCompositeLoads_WithToggleSettings' }
            'LumOnUpsample_Compiles_WithToggleVariants' { return 'LumOnUpsampleLoads_WithToggleSettings' }
            'LumOnProbeAtlasTrace_Compiles_WithLoopBoundVariants' { return 'ProbeAnchorTraceLoads_WithLoopBoundSettings' }
            'LumOnProbeAtlasTemporal_Compiles_WithTexelDistributionVariants' { return 'ProbeAtlasTemporalLoads_WithTexelDistribution' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/ShaderPatchingTests.cs' -and
        $Method -eq 'Insert_BeforeFunction_InsertsAtCorrectPosition') {
        return 'Insert_BeforeFunction_PreservesInsertedTextAndMain'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeCoordinateSpaceTests.cs') {
        if ($Method -eq 'PlayerRelativeCoordinates_ProduceWorldStableLookup') { return 'LookupPosition_ReturnsExpectedOffsetFromRelativeOrigins' }
        if ($Method -eq 'CameraBob_DoesNotAffectPlayerRelativeLookup') { return 'LookupPosition_RepeatedInputsProduceSameCoordinates' }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/WorldCellBoundsCameraTests.cs' -and
        $Method -eq 'BoundsPreserveFullSceneViewTranslation') {
        return 'FrameCameraBindingPreservesSceneViewTranslation'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/TraceSceneDebugShaderCoordSpaceTests.cs') {
        switch ($Method) {
            'TraceSceneDebug_UsesSharedFrameWorldspaceConversion' { return 'TraceSceneCellSourceContainsConversionToken' }
            'ScenesOverviewDebug_UsesSharedFrameWorldspaceConversion' { return 'ScenesOverviewSourceContainsTraceCellHelperToken' }
            'SurfaceCache_IrradianceSampling_DoesNotRejectNeedsRelight' { return 'SurfaceCacheSourceContainsCaptureFlagAndNotCombinedLiteral' }
            'RelightShader_UsesCpuBatchIndex_ForDeterministicCoverage' { return 'RelightShaderSourceContainsBatchIndexAndHashLiterals' }
        }
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/RHICommandContextIntegrationTests.cs' -and
        $Method -eq 'InvalidDrawContractEmitsNoDraw') {
        return 'InvalidDrawContractRejectsSelectedFailure'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/RHIAuxiliaryTests.cs' -and
        $Method -eq 'OptionalRequestsRejectBeforeDispatch') {
        return 'OptionalRequestsValidateImportsAndSparseRanges'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/RHICommandListTests.cs' -and
        $Method -eq 'CancelCreatesNoNativeResultsOrUniformPublication') {
        return 'CancelResolvesPendingRequestsWithoutUniformPublication'
    }
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/RHIImageTests.cs') {
        switch ($Method) {
            'MultisampleImageCopiesPreserveAllocation' { return 'MultisampleCopyPreservesStorageRevisionAndRejectsPixelUpload' }
            'InvalidInputsRejectBeforeNativeMutation' { return 'InvalidTextureInputsRejectWithoutGlErrors' }
        }
    }
    return $Method
}

$routes = @(Import-Csv (Join-Path $InventoryRoot 'Phase1.MigrationRouting.csv'))
$fileGroups = @{}
# Group methods before assigning class names so a multi-owner source is split consistently.
foreach ($route in $routes) {
    if (-not $fileGroups.ContainsKey($route.SourcePath)) { $fileGroups[$route.SourcePath] = @() }
    $fileGroups[$route.SourcePath] += $route
}
$moves = foreach ($route in $routes) {
    $project = $route.SourcePath.Split('/')[0]
    $level = Get-PrincipalLevel $route.Level
    if ($route.SourcePath -in @(
        'VanillaGraphicsExpanded.Tests/GraphicsPipelineDescriptionTests.cs',
        'VanillaGraphicsExpanded.Tests/ConfigurableRasterizerDescriptionTests.cs',
        'VanillaGraphicsExpanded.Tests/GraphicsStateRelevanceTests.cs')) {
        # Every case in these groups acquires a real shader generation.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/PbrTerrainColorPatchesTests.cs' -and
        $route.Method -in @(
            'ProductionPatchChainRetainsMaterialScope',
            'FragmentCapturePreservesBodyAndCapturesBeforeEffects',
            'VertexCapturePreservesOriginalBody',
            'UnsupportedMaterialPathsRemainUnchanged')) {
        # These cases read installed engine shader assets as their input boundary.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/SceneColorLegacyPatchSourceTests.cs' -and
        $route.Method -in @(
            'InstalledColorGradeRemainsNative',
            'InstalledWoitDiagnosticDecodesBeforeAlphaWeighting',
            'RestoredCloudFragmentsRetainConditionalSceneConversion',
            'RetainedCloudMapProducerIsUnmodified')) {
        # These cases use build-frozen game shader assets with the production source patcher.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/EnginePostprocessInputsTests.cs' -and
        $route.Method -eq 'CaptureUsesNativeSettingsAndEngineGate') {
        # This case composes native platform flags and client settings at the postprocess input boundary.
        $level = 'Component'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/EngineSkyFrameInputsTests.cs') {
        # This case verifies the production bridge to engine-owned private fields and calendar state.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuProgramNarrowPublicationTests.cs' -and
        $route.Method -eq 'InvalidControlledAssignmentsPreserveMetadata') {
        # The case captures real GL assignment symbols while proving the native boundary is not crossed.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuProgramNarrowPublicationTests.cs' -and
        $route.Method -eq 'LocalComputeRealizationReusesCanonicalInterface') {
        # The case exports and reloads a real driver program binary before verifying canonical interface reuse.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuResourceRetirementRegressionTests.cs' -and
        $route.Method -eq 'NonOwningWrapRejectsExternalOwnerHandleTransfer') {
        # Each row creates real framebuffer or renderbuffer owners and borrowed wrappers, then checks native liveness.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/GpuResourceMetadataPublicationTests.cs' -and
        $route.Method -eq 'RejectedBufferAllocationPreservesMeasuredCapacity') {
        # The test invokes the real GPU uploader against an allocated native buffer and checks driver error state.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/RHIComputePipelineTests.cs' -and
        $route.Method -eq 'DescriptionsDistinguishIndependentExecutableGenerations') {
        # The identity assertion depends on two live executable generations created through the real OpenGL context.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/PbrMaterialRegistryTextureNormalizationTests.cs' -and
        $route.Method -eq 'TryGetSurface_NormalizesTexturesPrefixAndExtension') {
        # The public derived-surface path is exercised through a controlled engine API.
        $level = 'Component'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialDisplacementTests.cs' -and
        $route.Method -eq 'SchemaKeepsDisplacementOutsideBrdfAndMappingOverrides') {
        # The case reads the built material schema artifact.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/GPU/PbrSolarBrdfTests.cs' -and
        $route.Method -eq 'SolarAngularRadiusMatchesTheAtmosphereEmitter') {
        # The comparison reads a build-frozen production shader artifact.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/EnginePostprocessInputsTests.cs' -and
        $route.Method -eq 'CaptureUsesNativeSettingsAndEngineGate') {
        # This case composes native engine flags and client settings at the postprocess input boundary.
        $level = 'Component'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/EngineSkyFrameInputsTests.cs') {
        # This case verifies the production bridge to engine-owned private fields and calendar state.
        $level = 'Integration'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/HeldLightTests.cs' -and
        $route.Method -in @('ViewPositionPreservesSubBlockOffsetsAndNeighborEntries', 'FirstPersonProjectionMatchesParticlePath')) {
        # These methods exercise deterministic matrix calculations without the engine fixture boundary.
        $level = 'Unit'
    }
    if ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/CachedObservableTests.cs' -and
        $route.Method -eq 'RenderingDispatchQueuesBackgroundWorkAndCancelsRetiredWork') {
        # The test uses a controlled queue and thread to qualify dispatch state, without engine integration.
        $level = 'Unit'
    }
    $domain = Get-Domain $project $route.SourcePath $route.ProposedOwner
    if ($route.SourcePath -in @(
        'VanillaGraphicsExpanded.Tests/GraphicsPipelineDescriptionTests.cs',
        'VanillaGraphicsExpanded.Tests/ConfigurableRasterizerDescriptionTests.cs',
        'VanillaGraphicsExpanded.Tests/GraphicsStateRelevanceTests.cs')) {
        $domain = 'Rendering/Pipelines'
    }
    $fileStem = [System.IO.Path]::GetFileNameWithoutExtension($route.SourcePath)
    $stem = ($fileStem -split '\.')[0]
    $owners = @($fileGroups[$route.SourcePath] | ForEach-Object { "$(Get-PrincipalLevel $_.Level)|$($_.ProposedOwner)" } | Sort-Object -Unique)
    $split = $owners.Count -gt 1
    $baseStem = $stem -replace 'Tests$', ''
    $ownerSuffix = Get-OwnerSuffix $route.ProposedOwner
    $sameLevelOwners = @($fileGroups[$route.SourcePath] | Where-Object { (Get-PrincipalLevel $_.Level) -eq $level } | Select-Object -ExpandProperty ProposedOwner -Unique)
    $defaultClass = if ($route.SourcePath -match '/Gpu(?:Ebo|Vbo)UnitTests.cs$' -and $route.ProposedOwner -match 'witness gap') { "${baseStem}${ownerSuffix}Tests" }
        elseif ($sameLevelOwners.Count -eq 1 -or $ownerSuffix -eq $baseStem) { $stem }
        elseif ($ownerSuffix.StartsWith($baseStem, [StringComparison]::Ordinal)) { "${ownerSuffix}Tests" }
        else { "${ownerSuffix}Tests" }
    $pendingCorrection = ($route.SourcePath -match '/Unit/Gpu(?:Ebo|Vbo)UnitTests.cs$') -or
        ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/PbrGlobstarTests.cs' -and $route.ProposedOwner -match 'current case does not reach selector') -or
        ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Squirrel3NoiseTests.cs' -and $route.ProposedOwner -match 'current name overclaims shader parity') -or
        ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialAtlasCacheWarmupPlannerTests.cs' -and
            $route.Method -eq 'CountExisting_CountsHits_ByPayloadKind')
    $class = if ($pendingCorrection) { $stem } else { Get-ReviewedClassName $route.SourcePath $route.ProposedOwner $level $defaultClass }
    $fileName = if ($fileStem.Contains('.')) { "$class.$($fileStem.Substring($stem.Length + 1)).cs" } else { "$class.cs" }
    $target = if ($pendingCorrection) { $route.SourcePath } else { "$project/$level/$domain/$fileName" }
    $targetNamespace = if ($pendingCorrection -and $route.SourcePath -match '/Unit/PBR/Materials/') { "$project.Unit.PBR.Materials" }
        elseif ($pendingCorrection -and $route.SourcePath -match '/Unit/') { "$project.Unit" }
        elseif ($pendingCorrection) { $project }
        else { "$project.$level.$($domain.Replace('/', '.'))" }
    [pscustomobject]@{
        SourceKey = $route.SourceKey
        SourcePath = $route.SourcePath
        Method = $route.Method
        TargetMethod = Get-ReviewedMethodName $route.SourcePath $route.Method
        RecordedLevel = $route.Level
        ProposedOwner = $route.ProposedOwner
        TargetPath = $target
        TargetNamespace = $targetNamespace
        TargetClass = $class
        Disposition = if ($pendingCorrection) { 'RetainPendingOracleDecision' } else { 'Move' }
        SplitSource = $split
        CurrentRuntimeCases = $route.RuntimeCases
    }
}
$moves | Export-Csv (Join-Path $InventoryRoot 'Phase2.MethodMoves.csv') -NoTypeInformation

# Keep each support source's lifetime cue alongside the proposed owning area.
$support = foreach ($source in Import-Csv (Join-Path $InventoryRoot 'Phase1.SupportRouting.csv')) {
    $project = $source.SourcePath.Split('/')[0]
    $group = $source.ProposedOwner
    $area = if ($project -ne 'VanillaGraphicsExpanded.Tests') { 'Support/Generation' }
        elseif ($group -match 'Water support') { 'Component/Water/Fixtures' }
        elseif ($group -match 'Ambient occlusion support') { 'Component/AmbientOcclusion/Fixtures' }
        elseif ($group -match 'Cloud support') { 'Component/Clouds/Fixtures' }
        elseif ($group -match 'Near-field support') { 'Component/LumOn/Fixtures' }
        elseif ($group -match 'Precompiled terrain|Terrain stage|Engine terrain') { 'Component/Terrain/Fixtures' }
        elseif ($group -match 'CPU multiple-scattering numerics') { 'Component/Atmosphere/Fixtures' }
        elseif ($group -match 'Owning test partials') { 'Component/LumOn' }
        elseif ($group -match 'World partition') { 'Component/WorldPartition/Fixtures' }
        elseif ($group -match 'Controlled voxel world|World-probe room') { 'Support/LumOn/WorldProbes/Fixtures' }
        elseif ($group -match 'Authored shader declarations') { 'Unit/Rendering/Contracts/Fixtures' }
        elseif ($group -match 'Unreferenced legacy uniform helper') { 'Support/Rendering/Uniforms' }
        elseif ($group -match 'GPU observation') { 'Support/GPU/Observations' }
        elseif ($group -match 'Shared GPU harness') { 'Support/GPU/Harness' }
        elseif ($group -match 'Built SPIR-V|Installed-engine shader|Native link|Driver-binary') { 'Support/GPU/Shaders' }
        elseif ($group -match 'Numerical shader|Prepared shader interface') { 'Support/GPU/Shaders' }
        elseif ($group -match 'Shared CPU references') { 'Support/Numerics' }
        elseif ($group -match 'RHI architecture') { 'Support/Architecture' }
        elseif ($group -match 'Shared asset|source and diagnostic') { 'Support/Assets' }
        elseif ($group -match 'Collection policy|Runner environment') { 'Support/Policy' }
        elseif ($group -match 'Native|GPU|Framebuffer|Frame and uniform') { 'Support/GPU/Native' }
        elseif ($group -match 'Lighting|sky|Surface|World-probe|Trace geometry') { 'Support/Lighting' }
        elseif ($group -match 'cache|Cache|Material') { 'Support/Cache' }
        else { 'Support/Core' }
    # Source-backed consumer checks refine broad domain groups without changing their reviewed lifetimes.
    $fixtureName = [System.IO.Path]::GetFileName($source.SourcePath)
    if ($source.SourcePath -like 'VanillaGraphicsExpanded.Tests/GPU/Fixtures/*') {
        if ($fixtureName -in @('CloudStoredVolumeViewScene.cs', 'CloudOitSurface.cs', 'CloudTemporalObservation.cs')) {
            $area = 'Support/Clouds'
        }
        elseif ($fixtureName -in @('TerrainBoundaryCaptureFixture.cs', 'TerrainLinkedTestProgram.cs')) {
            $area = 'Integration/Terrain/Fixtures'
        }
        elseif ($fixtureName -eq 'TerrainShaderTestFixture.cs') {
            $area = 'Support/Rendering/Shaders'
        }
        elseif ($fixtureName -eq 'TerrainTessellationTestAssets.cs') {
            $area = 'Support/Terrain'
        }
        elseif ($fixtureName -eq 'PbrHeightBakeTestPrograms.cs') {
            $area = 'Component/PBR/Materials/Fixtures'
        }
    }
    if ($source.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Fixtures/WorldProbes/ScopedPbrMaterialFixture.cs') {
        $area = 'Support/PBR/Materials'
    }
    $file = [System.IO.Path]::GetFileName($source.SourcePath)
    [pscustomobject]@{
        SourcePath = $source.SourcePath
        ProposedOwner = $group
        TargetPath = "$project/$area/$file"
        TargetNamespace = "$project.$($area.Replace('/', '.'))"
        LifetimeCue = $source.LifetimeCue
        DependencyCheck = 'Review direct and transitive consumers, partial types, collection fixtures, disposal, and linked assets before move'
    }
}
$support | Export-Csv (Join-Path $InventoryRoot 'Phase2.SupportMoves.csv') -NoTypeInformation

$duplicateTargets = @($moves | Group-Object TargetPath | Where-Object { @($_.Group.SourcePath | Sort-Object -Unique).Count -gt 1 })
$duplicateSupport = @($support | Group-Object TargetPath | Where-Object { @($_.Group.SourcePath | Sort-Object -Unique).Count -gt 1 })
if ($duplicateTargets.Count -gt 0 -or $duplicateSupport.Count -gt 0) {
    throw "Duplicate destination paths: test=$($duplicateTargets.Count), support=$($duplicateSupport.Count)"
}
Write-Output "Mapped $($routes.Count) test methods from $($fileGroups.Count) files and $($support.Count) support files."
