namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Authoritative list of VGE-owned shader define names.
/// </summary>
public static class VgeShaderDefines
{
    public const string LumOnEnabled = "VGE_LUMON_ENABLED";
    public const string LumOnPbrComposite = "VGE_LUMON_PBR_COMPOSITE";
    public const string LumOnEnableAo = "VGE_LUMON_ENABLE_AO";
    public const string LumOnEnableShortRangeAo = "VGE_LUMON_ENABLE_SHORT_RANGE_AO";

    [System.Obsolete("Renamed to LumOnEnableShortRangeAo.")]
    public const string LumOnEnableBentNormal = "VGE_LUMON_ENABLE_BENT_NORMAL";

    public const string LumOnUpsampleDenoise = "VGE_LUMON_UPSAMPLE_DENOISE";
    public const string LumOnUpsampleHoleFill = "VGE_LUMON_UPSAMPLE_HOLEFILL";

    public const string LumOnTemporalUseVelocityReprojection = "VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION";

    public const string LumOnRaysPerProbe = "VGE_LUMON_RAYS_PER_PROBE";
    public const string LumOnRaySteps = "VGE_LUMON_RAY_STEPS";
    public const string LumOnAtlasTexelsPerFrame = "VGE_LUMON_ATLAS_TEXELS_PER_FRAME";
    public const string LumOnRayMaxDistance = "VGE_LUMON_RAY_MAX_DISTANCE";
    public const string LumOnRayThickness = "VGE_LUMON_RAY_THICKNESS";
    public const string LumOnHzbCoarseMip = "VGE_LUMON_HZB_COARSE_MIP";
    public const string LumOnSkyMissWeight = "VGE_LUMON_SKY_MISS_WEIGHT";

    // Product Importance Sampling (Phase 10)
    public const string LumOnProbePisEnabled = "VGE_LUMON_PROBE_PIS_ENABLED";
    public const string LumOnProbePisExploreFraction = "VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION";
    public const string LumOnProbePisExploreCount = "VGE_LUMON_PROBE_PIS_EXPLORE_COUNT";
    public const string LumOnProbePisMinConfidenceWeight = "VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT";
    public const string LumOnProbePisWeightEpsilon = "VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON";
    public const string LumOnProbePisForceUniformMask = "VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK";
    public const string LumOnProbePisForceBatchSlicing = "VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING";

    // World-probe clipmap (Phase 18)
    public const string LumOnWorldProbeEnabled = "VGE_LUMON_WORLDPROBE_ENABLED";
    public const string LumOnWorldProbeClipmapLevels = "VGE_LUMON_WORLDPROBE_LEVELS";
    public const string LumOnWorldProbeClipmapResolution = "VGE_LUMON_WORLDPROBE_RESOLUTION";
    public const string LumOnWorldProbeClipmapBaseSpacing = "VGE_LUMON_WORLDPROBE_BASE_SPACING";

    // World-probe octahedral atlas (SH replacement; future refactor)
    public const string LumOnWorldProbeOctahedralSize = "VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE";
    public const string LumOnWorldProbeAtlasTexelsPerUpdate = "VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE";

    // World-probe radiance atlas binding + shader sampling knobs
    public const string LumOnWorldProbeBindRadianceAtlas = "VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS";
    public const string LumOnWorldProbeDiffuseStride = "VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE";

    public const string LumOnEmissiveBoost = "LUMON_EMISSIVE_BOOST";

    public const string PbrEnablePom = "VGE_PBR_ENABLE_POM";
    public const string PbrPomScale = "VGE_PBR_POM_SCALE";
    public const string PbrPomMinSteps = "VGE_PBR_POM_MIN_STEPS";
    public const string PbrPomMaxSteps = "VGE_PBR_POM_MAX_STEPS";
    public const string PbrPomRefinementSteps = "VGE_PBR_POM_REFINEMENT_STEPS";
    public const string PbrPomFadeStart = "VGE_PBR_POM_FADE_START";
    public const string PbrPomFadeEnd = "VGE_PBR_POM_FADE_END";
    public const string PbrPomMaxTexels = "VGE_PBR_POM_MAX_TEXELS";

    public const string PbrPomDebugMode = "VGE_PBR_POM_DEBUG_MODE";

    // PBR composite debug (full-screen output override)
    public const string PbrDebugViewMode = "VGE_PBR_DEBUG_VIEW_MODE";
}
