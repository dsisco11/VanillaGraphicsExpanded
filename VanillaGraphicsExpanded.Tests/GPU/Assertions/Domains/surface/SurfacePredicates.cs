namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored surface predicate contracts.</summary>
internal static class SurfacePredicates
{
    private static readonly GpuObservationDomain Declaration = new("surface", [(46, 3u), (58, 1u), (59, 3u), (60, 3u), (61, 1u), (62, 3u), (63, 3u), (64, 3u), (65, 3u), (66, 11u), (67, 11u), (68, 3u), (69, 11u), (70, 1u), (71, 1u), (72, 3u), (73, 3u), (74, 3u), (75, 3u), (76, 3u), (77, 3u), (78, 1u), (79, 27u), (80, 11u), (81, 11u), (82, 3u), (83, 3u), (84, 1u), (85, 3u), (86, 3u), (87, 3u), (88, 3u), (89, 3u), (90, 1u), (91, 1u), (92, 1u), (93, 1u), (94, 3u), (95, 11u), (96, 1u), (97, 1u), (98, 1u), (99, 3u), (100, 3u), (101, 3u), (102, 3u), (103, 1u), (104, 1u), (105, 3u), (106, 3u), (107, 3u), (108, 1u), (109, 1u), (110, 1u), (111, 3u), (112, 1u), (113, 3u), (114, 1u), (115, 11u), (116, 3u), (117, 3u), (118, 1u), (119, 3u), (120, 3u), (121, 3u), (122, 1u), (123, 3u), (124, 3u), (125, 1u), (126, 1u), (127, 1u), (128, 1u), (129, 1u), (159, 1u), (160, 1u), (161, 1u), (162, 3u), (163, 1u), (164, 3u), (165, 27u), (166, 19u), (167, 3u), (168, 11u), (169, 3u), (170, 3u), (171, 3u), (172, 3u), (179, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent SurfaceLightingEnclosureFixture observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingEnclosureFixture_Predicate = new(Declaration, 46);
    /// <summary>Preserves the independent LumOnCombineFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnCombineFunctionalTests_AssertRgb = new(Declaration, 58);
    /// <summary>Preserves the independent LumOnDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnDebugFunctionalTests_Mode1_ProbeGrid_ShowsValidityColors = new(Declaration, 59);
    /// <summary>Preserves the independent LumOnDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnDebugFunctionalTests_Mode4_SceneDepth_ShowsHeatmap = new(Declaration, 60);
    /// <summary>Preserves the independent LumOnDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnDebugFunctionalTests_Mode10_RadianceOverlay_ShowsIndirectDiffuse = new(Declaration, 61);
    /// <summary>Preserves the independent LumOnDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnDebugFunctionalTests_Mode4_SceneDepth_SkyIsBlack = new(Declaration, 62);
    /// <summary>Preserves the independent LumOnDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance = new(Declaration, 63);
    /// <summary>Preserves the independent LumOnHzbFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnHzbFunctionalTests_HZB_BuildProducesMonotonicDepthMips = new(Declaration, 64);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsCache observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsCache_ParallaxNeighborhood_PreservesConstantRadiance = new(Declaration, 65);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsCache observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsCache_Parallax_ChangesDirectionalLookup = new(Declaration, 66);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTests_SealedRoom_RequiresPublishedSurfaceLighting = new(Declaration, 67);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTests_UnresolvedScene_RemainsDark = new(Declaration, 68);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTests_ClearScene_UsesDistantCache = new(Declaration, 69);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsGeometry observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections = new(Declaration, 70);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsGeometry observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_2 = new(Declaration, 71);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsGeometry observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_3 = new(Declaration, 72);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsGeometry observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsGeometry_InitialSolid_IsNotSkipped = new(Declaration, 73);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsLighting observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache = new(Declaration, 74);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsLighting observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsLighting_MissingMaterial_PreservesOpaqueHit = new(Declaration, 75);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsLighting observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsLighting_UpwardSurface_RequiresPublishedSurfaceLighting = new(Declaration, 76);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsOutcomes observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsOutcomes_TraceOutcome_RecordsActualSourceAndAvailability = new(Declaration, 77);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsPublication observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsPublication_StaleArtifact_CannotReplaceCurrentRegion = new(Declaration, 78);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsScreenHits observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_AndOffscreenHitsRequireCachePublication = new(Declaration, 79);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsScreenHits observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_UnavailableLighting_RemainsUnresolved = new(Declaration, 80);
    /// <summary>Preserves the independent LumOnNearFieldFunctionalTestsScreenHits observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldFunctionalTestsScreenHits_ScreenHits_EmissionIsNotDoubled_AndSurvivesUnavailableLocalData = new(Declaration, 81);
    /// <summary>Preserves the independent LumOnNearFieldGeometryDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates = new(Declaration, 82);
    /// <summary>Preserves the independent LumOnNearFieldGeometryDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_2 = new(Declaration, 83);
    /// <summary>Preserves the independent LumOnNearFieldGeometryDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_3 = new(Declaration, 84);
    /// <summary>Preserves the independent LumOnNearFieldGeometryDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_CameraMovesAcrossVoxelBoundary_AtLargeOrigins = new(Declaration, 85);
    /// <summary>Preserves the independent LumOnNearFieldGeometryDebugFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_PublicationIntervalCameraSweep = new(Declaration, 86);
    /// <summary>Preserves the independent LumOnNearFieldMaterialReadinessTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnNearFieldMaterialReadinessTests_AssertLighting = new(Declaration, 87);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_AssertDecodedNormals = new(Declaration, 88);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_UniformDepth_ProducesCorrectWorldPositions = new(Declaration, 89);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_ValidProbes_HaveValidityFlagSet = new(Declaration, 90);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_SkyPixels_ProduceInvalidProbes = new(Declaration, 91);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_DepthDiscontinuity_ProducesPartialValidity = new(Declaration, 92);
    /// <summary>Preserves the independent LumOnProbeAnchorFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAnchorFunctionalTests_InvalidNormals_ProduceInvalidProbes = new(Declaration, 93);
    /// <summary>Preserves the independent LumOnProbeAtlasGatherFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasGatherFunctionalTests_WorldProbeSuppression_PreservesFallbackSelection = new(Declaration, 94);
    /// <summary>Preserves the independent LumOnProbeAtlasGatherFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasGatherFunctionalTests_IndirectTint_AppliedToOutput = new(Declaration, 95);
    /// <summary>Preserves the independent LumOnProbeAtlasGatherFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasGatherFunctionalTests_SkyPixels_ProduceZeroIrradiance = new(Declaration, 96);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_Temporal_MetaReducesHistoryWeight = new(Declaration, 97);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_InvalidProbe_OutputsZero = new(Declaration, 98);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_NonTracedTexels_PreserveCurrentOutput = new(Declaration, 99);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_Disocclusion_ResetsToCurrentFrame = new(Declaration, 100);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_VelocityInvalid_RejectsHistory = new(Declaration, 101);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_FrameIndex_AffectsTracedTexels = new(Declaration, 102);
    /// <summary>Preserves the independent LumOnProbeAtlasTemporalFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTemporalFunctionalTests_InvalidProbe_OutputsCurrentWithoutBlending = new(Declaration, 103);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_ValidProbe_TracesRaysToAtlas = new(Declaration, 104);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_HitDistance_EncodedCorrectly = new(Declaration, 105);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_TemporalDistribution_OnlyTracesSubset = new(Declaration, 106);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_RayHit_ReturnsSceneColor = new(Declaration, 107);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_RaySteps_AffectsTraceQuality = new(Declaration, 108);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky = new(Declaration, 109);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky_2 = new(Declaration, 110);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_FrameIndex_JittersTexelSelection = new(Declaration, 111);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceFunctionalTests_TexelsPerFrame_AffectsTemporalDistribution = new(Declaration, 112);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests_RunWorldProbeTraceScenario = new(Declaration, 113);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests_RunWorldProbeTraceScenario_2 = new(Declaration, 114);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsLightingEffect observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsLightingEffect_AssertGatherLightingEffect = new(Declaration, 115);
    /// <summary>Preserves the independent LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsPipeline observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsPipeline_AssertPairedHistoryReachesGather = new(Declaration, 116);
    /// <summary>Preserves the independent LumonSceneMaterialAtlasMultiFramePopulationTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonSceneMaterialAtlasMultiFramePopulationTests_MaterialAtlas_MultiFrame_SingleChunk_EventuallyPopulatesAllAllocatedTiles = new(Declaration, 117);
    /// <summary>Preserves the independent LumonSceneMeshCardCaptureComputeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonSceneMeshCardCaptureComputeTests_AssertDepthBounds = new(Declaration, 118);
    /// <summary>Preserves the independent LumonScenePipelineSmokeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonScenePipelineSmokeTests_PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages = new(Declaration, 119);
    /// <summary>Preserves the independent LumonScenePipelineSmokeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonScenePipelineSmokeTests_PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages_2 = new(Declaration, 120);
    /// <summary>Preserves the independent LumonSceneRuntimeCaptureWiringTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonSceneRuntimeCaptureWiringTests_RuntimeWiring_FeedbackToCpuToCapture_WritesExpectedMaterialNormals = new(Declaration, 121);
    /// <summary>Preserves the independent LumonSceneVoxelCaptureComputeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumonSceneVoxelCaptureComputeTests_CaptureVoxel_SingleWorkItem_WritesDepthZero_AndExpectedMaterial = new(Declaration, 122);
    /// <summary>Preserves the independent LumOnUpsampleFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnUpsampleFunctionalTests_GradientInput_SmoothUpsample = new(Declaration, 123);
    /// <summary>Preserves the independent LumOnUpsampleFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnUpsampleFunctionalTests_DepthEdge_PreservesSharpness = new(Declaration, 124);
    /// <summary>Preserves the independent LumOnVelocityFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnVelocityFunctionalTests_DisplacedSurfaceRejectsVelocityHistory = new(Declaration, 125);
    /// <summary>Preserves the independent LumOnVelocityFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnVelocityFunctionalTests_Velocity_RenderOriginMovementPreservesWorldReprojection = new(Declaration, 126);
    /// <summary>Preserves the independent LumOnVelocityFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnVelocityFunctionalTests_Velocity_StaticCamera_IsZero = new(Declaration, 127);
    /// <summary>Preserves the independent LumOnVelocityFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnVelocityFunctionalTests_Velocity_PureTranslation_ProducesExpectedDirection = new(Declaration, 128);
    /// <summary>Preserves the independent LumOnWorldProbeLightingEffectFunctionalTests observation.</summary>
    internal static readonly GpuAssertionPredicate LumOnWorldProbeLightingEffectFunctionalTests_LightingEffect_MapsSignedLuminance_AndIndicatesMissingPair = new(Declaration, 129);
    /// <summary>Preserves the independent SurfaceCacheDynamicLightingTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceCacheDynamicLightingTests_DoorwayClosureAndReopeningRetainStaleIllumination = new(Declaration, 159);
    /// <summary>Preserves the independent SurfaceCacheDynamicLightingTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceCacheDynamicLightingTests_AssertLighting = new(Declaration, 160);
    /// <summary>Preserves the independent SurfaceCacheLightingTermsTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceCacheLightingTermsTests_UniformEnclosurePreservesIrradianceAcrossTravelDistances = new(Declaration, 161);
    /// <summary>Preserves the independent SurfaceCacheLightingTermsTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceCacheLightingTermsTests_IncompleteHemisphereBatchPreservesHistory = new(Declaration, 162);
    /// <summary>Preserves the independent SurfaceCacheRuntimeTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceCacheRuntimeTests_PartialPagePublicationCarriesForwardUnchangedTiles = new(Declaration, 163);
    /// <summary>Preserves the independent SurfaceLightingConsumerTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache = new(Declaration, 164);
    /// <summary>Preserves the independent SurfaceLightingConsumerTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache_2 = new(Declaration, 165);
    /// <summary>Preserves the independent SurfaceLightingConsumerTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingConsumerTests_ScreenHitsPreserveCachedEmissionExactlyOnce = new(Declaration, 166);
    /// <summary>Preserves the independent SurfaceLightingHitComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingHitComponentTests_OffscreenLightRemovalAndRestorationReachHitRadiance = new(Declaration, 167);
    /// <summary>Preserves the independent SurfaceLightingTemporalComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingTemporalComponentTests_BounceGenerationPreservesAndAccumulatesHistory = new(Declaration, 168);
    /// <summary>Preserves the independent SurfaceLightingTemporalComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingTemporalComponentTests_DoorwayClosureAndReopeningReachRetainedProbeHistory = new(Declaration, 169);
    /// <summary>Preserves the independent SurfaceLightingTemporalComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingTemporalComponentTests_AssertRetainedDirections = new(Declaration, 170);
    /// <summary>Preserves the independent SurfaceLightingTemporalComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingTemporalComponentTests_AssertColdUntracedDirections = new(Declaration, 171);
    /// <summary>Preserves the independent SurfaceLightingTemporalComponentTests observation.</summary>
    internal static readonly GpuAssertionPredicate SurfaceLightingTemporalComponentTests_AssertClose = new(Declaration, 172);
    /// <summary>Preserves the independent WorldProbeDirectionalHistoryTests observation.</summary>
    internal static readonly GpuAssertionPredicate WorldProbeDirectionalHistoryTests_QueuedOverlapAndEmptyFlushPreserveNewHistoryUntilFullReset = new(Declaration, 179);
    #endregion
}
