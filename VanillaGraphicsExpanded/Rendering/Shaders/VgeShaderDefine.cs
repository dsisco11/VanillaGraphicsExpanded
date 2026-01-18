namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Authoritative list of VGE-owned shader define names.
/// </summary>
public static class VgeShaderDefines
{
    public const string LumOnEnabled = "VGE_LUMON_ENABLED";
    public const string LumOnPbrComposite = "VGE_LUMON_PBR_COMPOSITE";
    public const string LumOnEnableAo = "VGE_LUMON_ENABLE_AO";
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

    public const string LumOnEmissiveBoost = "LUMON_EMISSIVE_BOOST";

    public const string PbrEnableParallax = "VGE_PBR_ENABLE_PARALLAX";
    public const string PbrParallaxScale = "VGE_PBR_PARALLAX_SCALE";
}
