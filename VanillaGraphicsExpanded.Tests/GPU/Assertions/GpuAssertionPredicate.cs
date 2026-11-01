namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Identifies independently authored, build-declared image predicates.</summary>
internal enum GpuAssertionPredicate
{
    /// <summary>Selects the ordinary generic operation.</summary>
    None = 0,
    /// <summary>Preserves the independent observation in AmbientOcclusionHorizonBehaviorTests.</summary>
    AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence = 1,
    /// <summary>Preserves the independent observation in AmbientOcclusionHorizonBehaviorTests.</summary>
    AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence_2 = 2,
    /// <summary>Preserves the independent observation in AmbientOcclusionHorizonBehaviorTests.</summary>
    AmbientOcclusionHorizonBehaviorTests_ScreenEdgesUseOnlyVisibleGeometry = 3,
    /// <summary>Preserves the independent observation in AmbientOcclusionLightingContributionTests.</summary>
    AmbientOcclusionLightingContributionTests_GeneratedVisibilityChangesOnlyAvailableIndirectLighting = 4,
    /// <summary>Preserves the independent observation in AmbientOcclusionPassTests.</summary>
    AmbientOcclusionPassTests_ContactTransmissionAndMotionRemainCurrentFrame = 5,
    /// <summary>Preserves the independent observation in AmbientOcclusionPassTests.</summary>
    AmbientOcclusionPassTests_PerpendicularCornerAndCutoutGapsRespectGeometry = 6,
    /// <summary>Preserves the independent observation in AoMinimumChildProductionTests.</summary>
    AoMinimumChildProductionTests_ValidateMasks_2 = 7,
    /// <summary>Preserves the independent observation in GpuImageAssertions.Selection.</summary>
    GpuImageAssertionsSelection_Extremum = 8,
    /// <summary>Preserves the independent observation in GpuImageAssertions.Selection.</summary>
    GpuImageAssertionsSelection_Brightest_2 = 9,
    /// <summary>Preserves the independent observation in AtmosphereCelestialPublicationTests.</summary>
    AtmosphereCelestialPublicationTests_ThreeBandsReconstructIndependentDirectionsAndFaintBackground = 10,
    /// <summary>Preserves the independent observation in BloomLocalExposureShaderTests.</summary>
    BloomLocalExposureShaderTests_OwnedPyramidAppliesSourceGainOnceAndDisablesCleanly = 11,
    /// <summary>Preserves the independent observation in CelestialGalaxyGenerationTests.</summary>
    CelestialGalaxyGenerationTests_PackedCubePreservesSourceAndMipEnergy = 12,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation = 13,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts = 14,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_2 = 15,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3 = 16,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_3 = 17,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_4 = 18,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5 = 19,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_6 = 20,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_7 = 21,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_8 = 22,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_9 = 23,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10 = 24,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_11 = 25,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_13 = 26,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_14 = 27,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_15 = 28,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_16 = 29,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_17 = 30,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance = 31,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_2 = 32,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_3 = 33,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_4 = 34,
    /// <summary>Preserves the independent observation in CloudTemporalFreshnessTests.</summary>
    CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations = 35,
    /// <summary>Preserves the independent observation in CloudTemporalFreshnessTests.</summary>
    CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_2 = 36,
    /// <summary>Preserves the independent observation in CloudTemporalFreshnessTests.</summary>
    CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_3 = 37,
    /// <summary>Preserves the independent observation in ColorGradingLutTests.</summary>
    ColorGradingLutTests_NeutralGradientHasBoundedInterpolationError = 38,
    /// <summary>Preserves the independent observation in ColorGradingLutTests.</summary>
    ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_3 = 39,
    /// <summary>Preserves the independent observation in ConfigurableRasterizerGpuTests.</summary>
    ConfigurableRasterizerGpuTests_Count = 40,
    /// <summary>Preserves the independent observation in DebugGraphicsSubmissionTests.</summary>
    DebugGraphicsSubmissionTests_OrbSubmissionUsesSixRoutesProgramPointSizeAndDepthWrites = 41,
    /// <summary>Preserves the independent observation in DirectLightingSubmissionTests.</summary>
    DirectLightingSubmissionTests_ProductionMatchesReferenceAcrossFrozenReceivers = 42,
    /// <summary>Preserves the independent observation in DirectLightingSubmissionTests.</summary>
    DirectLightingSubmissionTests_Compare = 43,
    /// <summary>Preserves the independent observation in DynamicTextureCubeTests.</summary>
    DynamicTextureCubeTests_ColorFacesAndMipsAreIndependent = 44,
    /// <summary>Preserves the independent observation in FinalDisplayShaderTests.</summary>
    FinalDisplayShaderTests_DisplayControlsAffectOutputWithoutChangingScene = 45,
    /// <summary>Preserves the independent observation in SurfaceLightingEnclosureFixture.</summary>
    SurfaceLightingEnclosureFixture_Predicate = 46,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint = 47,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2 = 48,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses = 49,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_2 = 50,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_3 = 51,
    /// <summary>Preserves the independent observation in GpuImageAssertionTests.</summary>
    GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4 = 52,
    /// <summary>Preserves the independent observation in LightShaftShaderTests.</summary>
    LightShaftShaderTests_RadialFilterProducesBoundedSpatialHalo = 53,
    /// <summary>Preserves the independent observation in LiquidSpecularTriangulationTests.</summary>
    LiquidSpecularTriangulationTests_CompareFolds = 54,
    /// <summary>Preserves the independent observation in LiquidSpecularTriangulationTests.</summary>
    LiquidSpecularTriangulationTests_CompareFolds_2 = 55,
    /// <summary>Preserves the independent observation in LocalExposurePrototypeTests.</summary>
    LocalExposurePrototypeTests_InvalidCoordinatesAreRejectedBeforeIntegerConversion = 56,
    /// <summary>Preserves the independent observation in LocalExposurePrototypeTests.</summary>
    LocalExposurePrototypeTests_BoundsAndSmoothJoinPreserveBaseDetail = 57,
    /// <summary>Preserves the independent observation in LumOnCombineFunctionalTests.</summary>
    LumOnCombineFunctionalTests_AssertRgb = 58,
    /// <summary>Preserves the independent observation in LumOnDebugFunctionalTests.</summary>
    LumOnDebugFunctionalTests_Mode1_ProbeGrid_ShowsValidityColors = 59,
    /// <summary>Preserves the independent observation in LumOnDebugFunctionalTests.</summary>
    LumOnDebugFunctionalTests_Mode4_SceneDepth_ShowsHeatmap = 60,
    /// <summary>Preserves the independent observation in LumOnDebugFunctionalTests.</summary>
    LumOnDebugFunctionalTests_Mode10_RadianceOverlay_ShowsIndirectDiffuse = 61,
    /// <summary>Preserves the independent observation in LumOnDebugFunctionalTests.</summary>
    LumOnDebugFunctionalTests_Mode4_SceneDepth_SkyIsBlack = 62,
    /// <summary>Preserves the independent observation in LumOnDebugFunctionalTests.</summary>
    LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance = 63,
    /// <summary>Preserves the independent observation in LumOnHzbFunctionalTests.</summary>
    LumOnHzbFunctionalTests_HZB_BuildProducesMonotonicDepthMips = 64,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Cache.</summary>
    LumOnNearFieldFunctionalTestsCache_ParallaxNeighborhood_PreservesConstantRadiance = 65,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Cache.</summary>
    LumOnNearFieldFunctionalTestsCache_Parallax_ChangesDirectionalLookup = 66,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.</summary>
    LumOnNearFieldFunctionalTests_SealedRoom_RequiresPublishedSurfaceLighting = 67,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.</summary>
    LumOnNearFieldFunctionalTests_UnresolvedScene_RemainsDark = 68,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.</summary>
    LumOnNearFieldFunctionalTests_ClearScene_UsesDistantCache = 69,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Geometry.</summary>
    LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections = 70,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Geometry.</summary>
    LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_2 = 71,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Geometry.</summary>
    LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_3 = 72,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Geometry.</summary>
    LumOnNearFieldFunctionalTestsGeometry_InitialSolid_IsNotSkipped = 73,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Lighting.</summary>
    LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache = 74,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Lighting.</summary>
    LumOnNearFieldFunctionalTestsLighting_MissingMaterial_PreservesOpaqueHit = 75,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Lighting.</summary>
    LumOnNearFieldFunctionalTestsLighting_UpwardSurface_RequiresPublishedSurfaceLighting = 76,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Outcomes.</summary>
    LumOnNearFieldFunctionalTestsOutcomes_TraceOutcome_RecordsActualSourceAndAvailability = 77,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.Publication.</summary>
    LumOnNearFieldFunctionalTestsPublication_StaleArtifact_CannotReplaceCurrentRegion = 78,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.ScreenHits.</summary>
    LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_AndOffscreenHitsRequireCachePublication = 79,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.ScreenHits.</summary>
    LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_UnavailableLighting_RemainsUnresolved = 80,
    /// <summary>Preserves the independent observation in LumOnNearFieldFunctionalTests.ScreenHits.</summary>
    LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_EmissionIsNotDoubled_AndSurvivesUnavailableLocalData = 81,
    /// <summary>Preserves the independent observation in LumOnNearFieldGeometryDebugFunctionalTests.</summary>
    LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates = 82,
    /// <summary>Preserves the independent observation in LumOnNearFieldGeometryDebugFunctionalTests.</summary>
    LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_2 = 83,
    /// <summary>Preserves the independent observation in LumOnNearFieldGeometryDebugFunctionalTests.</summary>
    LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_3 = 84,
    /// <summary>Preserves the independent observation in LumOnNearFieldGeometryDebugFunctionalTests.</summary>
    LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_CameraMovesAcrossVoxelBoundary_AtLargeOrigins = 85,
    /// <summary>Preserves the independent observation in LumOnNearFieldGeometryDebugFunctionalTests.</summary>
    LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_PublicationIntervalCameraSweep = 86,
    /// <summary>Preserves the independent observation in LumOnNearFieldMaterialReadinessTests.</summary>
    LumOnNearFieldMaterialReadinessTests_AssertLighting = 87,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_AssertDecodedNormals = 88,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_UniformDepth_ProducesCorrectWorldPositions = 89,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_ValidProbes_HaveValidityFlagSet = 90,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_SkyPixels_ProduceInvalidProbes = 91,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_DepthDiscontinuity_ProducesPartialValidity = 92,
    /// <summary>Preserves the independent observation in LumOnProbeAnchorFunctionalTests.</summary>
    LumOnProbeAnchorFunctionalTests_InvalidNormals_ProduceInvalidProbes = 93,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasGatherFunctionalTests.</summary>
    LumOnProbeAtlasGatherFunctionalTests_WorldProbeSuppression_PreservesFallbackSelection = 94,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasGatherFunctionalTests.</summary>
    LumOnProbeAtlasGatherFunctionalTests_IndirectTint_AppliedToOutput = 95,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasGatherFunctionalTests.</summary>
    LumOnProbeAtlasGatherFunctionalTests_SkyPixels_ProduceZeroIrradiance = 96,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_Temporal_MetaReducesHistoryWeight = 97,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_InvalidProbe_OutputsZero = 98,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_NonTracedTexels_PreserveCurrentOutput = 99,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_Disocclusion_ResetsToCurrentFrame = 100,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_VelocityInvalid_RejectsHistory = 101,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_FrameIndex_AffectsTracedTexels = 102,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTemporalFunctionalTests.</summary>
    LumOnProbeAtlasTemporalFunctionalTests_InvalidProbe_OutputsCurrentWithoutBlending = 103,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_ValidProbe_TracesRaysToAtlas = 104,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_HitDistance_EncodedCorrectly = 105,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_TemporalDistribution_OnlyTracesSubset = 106,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_RayHit_ReturnsSceneColor = 107,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_RaySteps_AffectsTraceQuality = 108,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky = 109,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky_2 = 110,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_FrameIndex_JittersTexelSelection = 111,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceFunctionalTests.</summary>
    LumOnProbeAtlasTraceFunctionalTests_TexelsPerFrame_AffectsTemporalDistribution = 112,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.</summary>
    LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests_RunWorldProbeTraceScenario = 113,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.</summary>
    LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests_RunWorldProbeTraceScenario_2 = 114,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.LightingEffect.</summary>
    LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsLightingEffect_AssertGatherLightingEffect = 115,
    /// <summary>Preserves the independent observation in LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.Pipeline.</summary>
    LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsPipeline_AssertPairedHistoryReachesGather = 116,
    /// <summary>Preserves the independent observation in LumonSceneMaterialAtlasMultiFramePopulationTests.</summary>
    LumonSceneMaterialAtlasMultiFramePopulationTests_MaterialAtlas_MultiFrame_SingleChunk_EventuallyPopulatesAllAllocatedTiles = 117,
    /// <summary>Preserves the independent observation in LumonSceneMeshCardCaptureComputeTests.</summary>
    LumonSceneMeshCardCaptureComputeTests_AssertDepthBounds = 118,
    /// <summary>Preserves the independent observation in LumonScenePipelineSmokeTests.</summary>
    LumonScenePipelineSmokeTests_PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages = 119,
    /// <summary>Preserves the independent observation in LumonScenePipelineSmokeTests.</summary>
    LumonScenePipelineSmokeTests_PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages_2 = 120,
    /// <summary>Preserves the independent observation in LumonSceneRuntimeCaptureWiringTests.</summary>
    LumonSceneRuntimeCaptureWiringTests_RuntimeWiring_FeedbackToCpuToCapture_WritesExpectedMaterialNormals = 121,
    /// <summary>Preserves the independent observation in LumonSceneVoxelCaptureComputeTests.</summary>
    LumonSceneVoxelCaptureComputeTests_CaptureVoxel_SingleWorkItem_WritesDepthZero_AndExpectedMaterial = 122,
    /// <summary>Preserves the independent observation in LumOnUpsampleFunctionalTests.</summary>
    LumOnUpsampleFunctionalTests_GradientInput_SmoothUpsample = 123,
    /// <summary>Preserves the independent observation in LumOnUpsampleFunctionalTests.</summary>
    LumOnUpsampleFunctionalTests_DepthEdge_PreservesSharpness = 124,
    /// <summary>Preserves the independent observation in LumOnVelocityFunctionalTests.</summary>
    LumOnVelocityFunctionalTests_DisplacedSurfaceRejectsVelocityHistory = 125,
    /// <summary>Preserves the independent observation in LumOnVelocityFunctionalTests.</summary>
    LumOnVelocityFunctionalTests_Velocity_RenderOriginMovementPreservesWorldReprojection = 126,
    /// <summary>Preserves the independent observation in LumOnVelocityFunctionalTests.</summary>
    LumOnVelocityFunctionalTests_Velocity_StaticCamera_IsZero = 127,
    /// <summary>Preserves the independent observation in LumOnVelocityFunctionalTests.</summary>
    LumOnVelocityFunctionalTests_Velocity_PureTranslation_ProducesExpectedDirection = 128,
    /// <summary>Preserves the independent observation in LumOnWorldProbeLightingEffectFunctionalTests.</summary>
    LumOnWorldProbeLightingEffectFunctionalTests_LightingEffect_MapsSignedLuminance_AndIndicatesMissingPair = 129,
    /// <summary>Preserves the independent observation in MaterialAtlasBakeSubmissionTests.</summary>
    MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles = 130,
    /// <summary>Preserves the independent observation in MaterialAtlasBakeSubmissionTests.</summary>
    MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_2 = 131,
    /// <summary>Preserves the independent observation in MaterialAtlasBakeSubmissionTests.</summary>
    MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_3 = 132,
    /// <summary>Preserves the independent observation in NearFieldResourceReuseTests.</summary>
    NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges = 133,
    /// <summary>Preserves the independent observation in NearFieldResourceReuseTests.</summary>
    NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges_2 = 134,
    /// <summary>Preserves the independent observation in OitStorageArithmeticTests.</summary>
    OitStorageArithmeticTests_PatchedDepthGuardUsesActualRoundedLogInput = 135,
    /// <summary>Preserves the independent observation in OwnedPostprocessShaderTests.</summary>
    OwnedPostprocessShaderTests_BloomThresholdAndConstantEnergyAreDefined = 136,
    /// <summary>Preserves the independent observation in OwnedPostprocessShaderTests.</summary>
    OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs_2 = 137,
    /// <summary>Preserves the independent observation in OwnedPostprocessShaderTests.</summary>
    OwnedPostprocessShaderTests_BloomOwnerPreservesConstantGainAndLifetime = 138,
    /// <summary>Preserves the independent observation in OwnedPostprocessShaderTests.</summary>
    OwnedPostprocessShaderTests_BloomOwnerReconstructsSymmetricMultiscaleImpulse = 139,
    /// <summary>Preserves the independent observation in PbrDisplayResolveTests.</summary>
    PbrDisplayResolveTests_QuantizedRampIsBalancedNeutralAndDeterministic_2 = 140,
    /// <summary>Preserves the independent observation in PbrHeightBakeFullChainTests.</summary>
    PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput = 141,
    /// <summary>Preserves the independent observation in PbrHeightBakeFullChainTests.</summary>
    PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2 = 142,
    /// <summary>Preserves the independent observation in PbrHeightBakeFullChainTests.</summary>
    PbrHeightBakeFullChainTests_FullChain_NormalScaleZero_FlattensPackedNormalsRgb = 143,
    /// <summary>Preserves the independent observation in PbrHeightBakeFullChainTests.</summary>
    PbrHeightBakeFullChainTests_AlphaStats = 144,
    /// <summary>Preserves the independent observation in PbrLumOnFullPipelineIntegrationTests.</summary>
    PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite = 145,
    /// <summary>Preserves the independent observation in PbrLumOnFullPipelineIntegrationTests.</summary>
    PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite_2 = 146,
    /// <summary>Preserves the independent observation in PbrLumOnFullPipelineIntegrationTests.</summary>
    PbrLumOnFullPipelineIntegrationTests_AssertStageHasRgbEnergy = 147,
    /// <summary>Preserves the independent observation in PbrLumOnFullPipelineIntegrationTests.</summary>
    PbrLumOnFullPipelineIntegrationTests_AssertCompositeMatchesDirectOnly = 148,
    /// <summary>Preserves the independent observation in PbrNormalDepthBakeShaderPipelineTests.</summary>
    PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput = 149,
    /// <summary>Preserves the independent observation in PbrNormalDepthBakeShaderPipelineTests.</summary>
    PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput_2 = 150,
    /// <summary>Preserves the independent observation in PmjJitterTextureUploadTests.</summary>
    PmjJitterTextureUploadTests_UploadMatchesPackedSequenceAndRetainsIndependentStorage = 151,
    /// <summary>Preserves the independent observation in SharedGeometryDebugTests.</summary>
    SharedGeometryDebugTests_NearViewerClipsToLogicalWindow = 152,
    /// <summary>Preserves the independent observation in SharedGeometryDebugTests.</summary>
    SharedGeometryDebugTests_AssertColor = 153,
    /// <summary>Preserves the independent observation in SharedTraceSceneScreenTests.</summary>
    SharedTraceSceneScreenTests_SealedRoomUsesSharedHitLighting = 154,
    /// <summary>Preserves the independent observation in SharedTraceSceneSurfaceTests.</summary>
    SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates = 155,
    /// <summary>Preserves the independent observation in SharedTraceSceneSurfaceTests.</summary>
    SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates_2 = 156,
    /// <summary>Preserves the independent observation in SunDiskDrawTests.</summary>
    SunDiskDrawTests_VisibleDiskStorageLimitIsFiniteAndChromatic = 157,
    /// <summary>Preserves the independent observation in SunDiskDrawTests.</summary>
    SunDiskDrawTests_SolarDiskIgnoresCameraTranslation = 158,
    /// <summary>Preserves the independent observation in SurfaceCacheDynamicLightingTests.</summary>
    SurfaceCacheDynamicLightingTests_DoorwayClosureAndReopeningRetainStaleIllumination = 159,
    /// <summary>Preserves the independent observation in SurfaceCacheDynamicLightingTests.</summary>
    SurfaceCacheDynamicLightingTests_AssertLighting = 160,
    /// <summary>Preserves the independent observation in SurfaceCacheLightingTermsTests.</summary>
    SurfaceCacheLightingTermsTests_UniformEnclosurePreservesIrradianceAcrossTravelDistances = 161,
    /// <summary>Preserves the independent observation in SurfaceCacheLightingTermsTests.</summary>
    SurfaceCacheLightingTermsTests_IncompleteHemisphereBatchPreservesHistory = 162,
    /// <summary>Preserves the independent observation in SurfaceCacheRuntimeTests.</summary>
    SurfaceCacheRuntimeTests_PartialPagePublicationCarriesForwardUnchangedTiles = 163,
    /// <summary>Preserves the independent observation in SurfaceLightingConsumerTests.</summary>
    SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache = 164,
    /// <summary>Preserves the independent observation in SurfaceLightingConsumerTests.</summary>
    SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache_2 = 165,
    /// <summary>Preserves the independent observation in SurfaceLightingConsumerTests.</summary>
    SurfaceLightingConsumerTests_ScreenHitsPreserveCachedEmissionExactlyOnce = 166,
    /// <summary>Preserves the independent observation in SurfaceLightingHitComponentTests.</summary>
    SurfaceLightingHitComponentTests_OffscreenLightRemovalAndRestorationReachHitRadiance = 167,
    /// <summary>Preserves the independent observation in SurfaceLightingTemporalComponentTests.</summary>
    SurfaceLightingTemporalComponentTests_BounceGenerationPreservesAndAccumulatesHistory = 168,
    /// <summary>Preserves the independent observation in SurfaceLightingTemporalComponentTests.</summary>
    SurfaceLightingTemporalComponentTests_DoorwayClosureAndReopeningReachRetainedProbeHistory = 169,
    /// <summary>Preserves the independent observation in SurfaceLightingTemporalComponentTests.</summary>
    SurfaceLightingTemporalComponentTests_AssertRetainedDirections = 170,
    /// <summary>Preserves the independent observation in SurfaceLightingTemporalComponentTests.</summary>
    SurfaceLightingTemporalComponentTests_AssertColdUntracedDirections = 171,
    /// <summary>Preserves the independent observation in SurfaceLightingTemporalComponentTests.</summary>
    SurfaceLightingTemporalComponentTests_AssertClose = 172,
    /// <summary>Preserves the independent observation in TerrainDisplacementRasterTests.</summary>
    TerrainDisplacementRasterTests_AdaptiveStagesPublishBoundedPositionAndNormal = 173,
    /// <summary>Preserves the independent observation in TerrainReliefNumericalTests.</summary>
    TerrainReliefNumericalTests_ReliefUsesAuthoredMetric = 174,
    /// <summary>Preserves the independent observation in TerrainTessellationEngineDrawTests.</summary>
    TerrainTessellationEngineDrawTests_PatchedEngineGroupedDrawMatchesOrdinaryIndexedDraw = 175,
    /// <summary>Preserves the independent observation in WaterMediumTests.</summary>
    WaterMediumTests_HomogeneousTransportMatchesAnalyticReference = 176,
    /// <summary>Preserves the independent observation in WaterMediumTests.</summary>
    WaterMediumTests_HomogeneousTransportMatchesAnalyticReference_2 = 177,
    /// <summary>Preserves the independent observation in WaterRefractionReductionTests.</summary>
    WaterRefractionReductionTests_AssertAssociatedReceivers = 178,
    /// <summary>Preserves the independent observation in WorldProbeDirectionalHistoryTests.</summary>
    WorldProbeDirectionalHistoryTests_QueuedOverlapAndEmptyFlushPreserveNewHistoryUntilFullReset = 179,
    /// <summary>Preserves the independent observation in GpuImageAssertions.Comparisons.</summary>
    GpuImageAssertionsComparisons_CompareBits = 180,
    /// <summary>Preserves the independent observation in GpuImageAssertions.Predicates.</summary>
    GpuImageAssertionsPredicates_CompareFloating = 181,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_18 = 182,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_19 = 183,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_20 = 184,
    /// <summary>Preserves the independent observation in CloudStoredVolumeViewTests.</summary>
    CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_21 = 185,
}
