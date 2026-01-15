namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Debug visualization modes for the LumOn debug overlay.
/// 
/// Values map directly to the shader's <c>debugMode</c> switch in <c>lumon_debug.fsh</c>.
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
}
