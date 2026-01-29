namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Debug visualization modes for the LumOn debug overlay.
/// 
/// Most values map directly to the shader's <c>debugMode</c> switch in <c>lumon_debug.fsh</c>.
/// Some modes are VGE-only and are special-cased by the C# debug renderer.
/// </summary>
public enum LumOnDebugMode
{
    Off = 0,

    ProbeGrid = 1,
    ProbeDepth = 2,
    ProbeNormal = 3,

    SceneDepth = 4,
    SceneNormal = 5,

    TemporalWeight = 6,
    TemporalRejection = 7,

    ShCoefficients = 8,
    InterpolationWeights = 9,

    RadianceOverlay = 10,

    // Visualizes gather weighting diagnostics encoded into indirectHalf alpha.
    // Grayscale = edge-aware total weight (scaled), Red = fallback path used.
    GatherWeight = 11,

    // Screen-probe atlas (probe-atlas mode) debug views.
    ProbeAtlasMetaConfidence = 12,
    ProbeAtlasTemporalAlpha = 13,
    ProbeAtlasMetaFlags = 14,
    ProbeAtlasFilteredRadiance = 15,
    ProbeAtlasFilterDelta = 16,
    ProbeAtlasGatherInputSource = 17,

    // Composite debug views.
    CompositeAO = 18,
    CompositeIndirectDiffuse = 19,
    CompositeIndirectSpecular = 20,
    CompositeMaterial = 21,

    // Direct lighting debug views.
    DirectDiffuse = 22,
    DirectSpecular = 23,
    DirectEmissive = 24,
    DirectTotal = 25,

    // Velocity debug views.
    VelocityMagnitude = 26,
    VelocityValidity = 27,
    VelocityPrevUv = 28,
   
    // Pseudo material-id visualization (hash of gBufferMaterial)
    MaterialBands = 29,

    // VGE-only debug views (special-cased by C# debug renderer; not driven by lumon_debug.fsh)
    VgeNormalDepthAtlas = 30,

    // World-probe clipmap debug views.
    WorldProbeIrradianceCombined = 31,
    WorldProbeIrradianceLevel = 32,
    WorldProbeConfidence = 33,
    WorldProbeShortRangeAoDirection = 34,
    WorldProbeShortRangeAoConfidence = 35,
    WorldProbeHitDistance = 36,
    WorldProbeMetaFlagsHeatmap = 37,
    WorldProbeBlendWeights = 38,
    WorldProbeCrossLevelBlend = 39,

    // VGE-only: render probe "orbs" as GL_POINTS point sprites using CPU-generated probe centers + atlas coords.
    // Also renders the clipmap bounds overlay.
    WorldProbeOrbsPoints = 40,

    // POM debug visualization (reads gBufferNormal.w written by patched chunk shaders)
    PomMetrics = 41,

    // World-probe diagnostics.
    // R = reconstructed screen confidence (screenW), G = raw world-probe confidence, B = final confidence (sumW)
    WorldProbeRawConfidences = 42,

    // Contribution split (screen vs world-probe)
    WorldProbeContributionOnly = 43,
    ScreenSpaceContributionOnly = 44,

    // Additional probe-atlas debug views (append-only)
    ProbeAtlasCurrentRadiance = 45,
    ProbeAtlasGatherInputRadiance = 46,
    ProbeAtlasHitDistance = 47,
    ProbeAtlasTraceRadiance = 48,

    // Probe-atlas temporal rejection reasons (append-only)
    ProbeAtlasTemporalRejection = 49,

    // Phase 10: PIS debug views (append-only)
    ProbeAtlasPisTraceMask = 50,
    ProbePisEnergy = 51,

    // Phase 22: LumonScene surface cache debug views (append-only)
    LumonScenePageReady = 52,
    LumonScenePatchUv = 53,
    LumonSceneIrradiance = 54,

    // Phase 23: TraceScene (occupancy clipmap) debug views (append-only)
    TraceSceneBoundsL0 = 55,
    TraceSceneOccupancyL0 = 56,
    TraceScenePayloadL0 = 57,
}
