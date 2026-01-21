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

    // World-probe clipmap (Phase 18)
    public const string LumOnWorldProbeEnabled = "VGE_LUMON_WORLDPROBE_ENABLED";
    public const string LumOnWorldProbeClipmapLevels = "VGE_LUMON_WORLDPROBE_LEVELS";
    public const string LumOnWorldProbeClipmapResolution = "VGE_LUMON_WORLDPROBE_RESOLUTION";
    public const string LumOnWorldProbeClipmapBaseSpacing = "VGE_LUMON_WORLDPROBE_BASE_SPACING";

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
