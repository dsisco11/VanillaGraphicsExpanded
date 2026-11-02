namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored material predicate contracts.</summary>
internal static class MaterialPredicates
{
    private static readonly GpuObservationDomain Declaration = new("material", [(11, 1u), (38, 3u), (39, 1u), (45, 3u), (56, 1u), (57, 3u), (130, 1u), (131, 1u), (132, 3u), (135, 3u), (140, 3u), (141, 3u), (142, 3u), (143, 1u), (144, 1u), (145, 1u), (146, 3u), (147, 1u), (148, 27u), (149, 1u), (150, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent BloomLocalExposureShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate BloomLocalExposureShaderTests_OwnedPyramidAppliesSourceGainOnceAndDisablesCleanly = new(Declaration, 11);
    /// <summary>Preserves the independent ColorGradingLutTests observation.</summary>
    internal static readonly GpuAssertionPredicate ColorGradingLutTests_NeutralGradientHasBoundedInterpolationError = new(Declaration, 38);
    /// <summary>Preserves the independent ColorGradingLutTests observation.</summary>
    internal static readonly GpuAssertionPredicate ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_3 = new(Declaration, 39);
    /// <summary>Preserves the independent FinalDisplayShaderTests observation.</summary>
    internal static readonly GpuAssertionPredicate FinalDisplayShaderTests_DisplayControlsAffectOutputWithoutChangingScene = new(Declaration, 45);
    /// <summary>Preserves the independent LocalExposurePrototypeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LocalExposurePrototypeTests_InvalidCoordinatesAreRejectedBeforeIntegerConversion = new(Declaration, 56);
    /// <summary>Preserves the independent LocalExposurePrototypeTests observation.</summary>
    internal static readonly GpuAssertionPredicate LocalExposurePrototypeTests_BoundsAndSmoothJoinPreserveBaseDetail = new(Declaration, 57);
    /// <summary>Preserves the independent MaterialAtlasBakeSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles = new(Declaration, 130);
    /// <summary>Preserves the independent MaterialAtlasBakeSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_2 = new(Declaration, 131);
    /// <summary>Preserves the independent MaterialAtlasBakeSubmissionTests observation.</summary>
    internal static readonly GpuAssertionPredicate MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_3 = new(Declaration, 132);
    /// <summary>Preserves the independent OitStorageArithmeticTests observation.</summary>
    internal static readonly GpuAssertionPredicate OitStorageArithmeticTests_PatchedDepthGuardUsesActualRoundedLogInput = new(Declaration, 135);
    /// <summary>Preserves the independent PbrDisplayResolveTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrDisplayResolveTests_QuantizedRampIsBalancedNeutralAndDeterministic_2 = new(Declaration, 140);
    /// <summary>Preserves the independent PbrHeightBakeFullChainTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput = new(Declaration, 141);
    /// <summary>Preserves the independent PbrHeightBakeFullChainTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2 = new(Declaration, 142);
    /// <summary>Preserves the independent PbrHeightBakeFullChainTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrHeightBakeFullChainTests_FullChain_NormalScaleZero_FlattensPackedNormalsRgb = new(Declaration, 143);
    /// <summary>Preserves the independent PbrHeightBakeFullChainTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrHeightBakeFullChainTests_AlphaStats = new(Declaration, 144);
    /// <summary>Preserves the independent PbrLumOnFullPipelineIntegrationTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite = new(Declaration, 145);
    /// <summary>Preserves the independent PbrLumOnFullPipelineIntegrationTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite_2 = new(Declaration, 146);
    /// <summary>Preserves the independent PbrLumOnFullPipelineIntegrationTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrLumOnFullPipelineIntegrationTests_AssertStageHasRgbEnergy = new(Declaration, 147);
    /// <summary>Preserves the independent PbrLumOnFullPipelineIntegrationTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrLumOnFullPipelineIntegrationTests_AssertCompositeMatchesDirectOnly = new(Declaration, 148);
    /// <summary>Preserves the independent PbrNormalDepthBakeShaderPipelineTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput = new(Declaration, 149);
    /// <summary>Preserves the independent PbrNormalDepthBakeShaderPipelineTests observation.</summary>
    internal static readonly GpuAssertionPredicate PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput_2 = new(Declaration, 150);
    #endregion
}
