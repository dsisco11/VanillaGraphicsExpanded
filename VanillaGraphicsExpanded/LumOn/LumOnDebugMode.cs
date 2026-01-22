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

    // Phase 15 composite debug views (implemented in lumon_debug.fsh)
    CompositeAO = 18,
    CompositeIndirectDiffuse = 19,
    CompositeIndirectSpecular = 20,
    CompositeMaterial = 21,

    // Phase 16 direct lighting debug views (implemented in lumon_debug.fsh)
    DirectDiffuse = 22,
    DirectSpecular = 23,
    DirectEmissive = 24,
    DirectTotal = 25,

    // Phase 14 velocity debug views
    VelocityMagnitude = 26,
    VelocityValidity = 27,
    VelocityPrevUv = 28,
   
    // Phase 7: pseudo material-id visualization (hash of gBufferMaterial)
    MaterialBands = 29,

    // VGE-only debug views (special-cased by C# debug renderer; not driven by lumon_debug.fsh)
    VgeNormalDepthAtlas = 30,

    // Phase 18: world-probe clipmap debug views (implemented in lumon_debug.fsh)
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

    // Phase 18: diagnostics
    // R = reconstructed screen confidence (screenW), G = raw world-probe confidence, B = final confidence (sumW)
    WorldProbeRawConfidences = 42,
}
