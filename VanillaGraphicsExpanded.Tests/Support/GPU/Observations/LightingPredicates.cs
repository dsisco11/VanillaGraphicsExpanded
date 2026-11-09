namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored lighting predicate contracts.</summary>
internal static class LightingPredicates
{
    private static readonly GpuObservationDomain Declaration = new("lighting", [(40, 1u), (41, 3u), (42, 3u), (43, 3u), (44, 1u), (53, 1u), (136, 3u), (137, 3u), (138, 3u), (139, 1u), (151, 3u), (152, 1u), (153, 1u), (154, 27u), (155, 3u), (156, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent ConfigurableRasterizerGpuTests observation.</summary>
    internal static readonly GpuAssertionPredicate ConfigurableRasterizerGpuTests_Count = new(Declaration, 40);
    /// <summary>Preserves the independent DebugGraphicsSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate DebugGraphicsSubmissionTests_OrbSubmissionUsesSixRoutesProgramPointSizeAndDepthWrites = new(Declaration, 41);
    /// <summary>Preserves the independent DirectLightingSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate DirectLightingSubmissionTests_ProductionMatchesReferenceAcrossFrozenReceivers = new(Declaration, 42);
    /// <summary>Preserves the independent DirectLightingSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate DirectLightingSubmissionTests_Compare = new(Declaration, 43);
    /// <summary>Preserves the independent DynamicTextureCubeTests observation.</summary>
    internal static readonly GpuAssertionPredicate DynamicTextureCubeTests_ColorFacesAndMipsAreIndependent = new(Declaration, 44);
    /// <summary>Preserves the independent LightShaftShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate LightShaftShaderTests_RadialFilterProducesBoundedSpatialHalo = new(Declaration, 53);
    /// <summary>Preserves the independent OwnedPostprocessShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate OwnedPostprocessShaderTests_BloomThresholdAndConstantEnergyAreDefined = new(Declaration, 136);
    /// <summary>Preserves the independent OwnedPostprocessShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs_2 = new(Declaration, 137);
    /// <summary>Preserves the independent OwnedPostprocessShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate OwnedPostprocessShaderTests_BloomOwnerPreservesConstantGainAndLifetime = new(Declaration, 138);
    /// <summary>Preserves the independent OwnedPostprocessShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate OwnedPostprocessShaderTests_BloomOwnerReconstructsSymmetricMultiscaleImpulse = new(Declaration, 139);
    /// <summary>Preserves the independent PmjJitterTextureUploadTests observation.</summary>
    internal static readonly GpuAssertionPredicate PmjJitterTextureUploadTests_UploadMatchesPackedSequenceAndRetainsIndependentStorage = new(Declaration, 151);
    /// <summary>Preserves the independent SharedGeometryDebugTests observation.</summary>
    internal static readonly GpuAssertionPredicate SharedGeometryDebugTests_NearViewerClipsToLogicalWindow = new(Declaration, 152);
    /// <summary>Preserves the independent SharedGeometryDebugTests observation.</summary>
    internal static readonly GpuAssertionPredicate SharedGeometryDebugTests_AssertColor = new(Declaration, 153);
    /// <summary>Preserves the independent SharedTraceSceneScreenTests observation.</summary>
    internal static readonly GpuAssertionPredicate SharedTraceSceneScreenTests_SealedRoomUsesSharedHitLighting = new(Declaration, 154);
    /// <summary>Preserves the independent SharedTraceSceneSurfaceTests observation.</summary>
    internal static readonly GpuAssertionPredicate SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates = new(Declaration, 155);
    /// <summary>Preserves the independent SharedTraceSceneSurfaceTests observation.</summary>
    internal static readonly GpuAssertionPredicate SharedTraceSceneSurfaceTests_ValidSharedSurfaceCapturesAndAccumulates_2 = new(Declaration, 156);
    #endregion
}
