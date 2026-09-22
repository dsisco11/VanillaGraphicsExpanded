namespace VanillaGraphicsExpanded.Rendering.Contracts;
/// <summary>Canonical lighting settings shared by their explicit program memberships.</summary>
internal static class LumOnShaderOptions
{
    public static readonly ShaderOption<bool> Enabled = new("VGE_LUMON_ENABLED", true);
    public static readonly ShaderOption<bool> PbrComposite = new("VGE_LUMON_PBR_COMPOSITE", true);
    public static readonly ShaderOption<bool> AmbientOcclusion = new("VGE_LUMON_ENABLE_AO", true);
    public static readonly ShaderOption<bool> ShortRangeAo = new("VGE_LUMON_ENABLE_SHORT_RANGE_AO", true, aliases: ["VGE_LUMON_ENABLE_BENT_NORMAL"]);
    public static readonly ShaderOption<bool> DirectVisibility = new("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", false);
    public static readonly ShaderOption<bool> NearField = new("VGE_LUMON_NEAR_FIELD_ENABLED", false);
    public static readonly ShaderOption<bool> WorldProbes = new("VGE_LUMON_WORLDPROBE_ENABLED", false);
    public static readonly ShaderOption<bool> ImportanceSampling = new("VGE_LUMON_PROBE_PIS_ENABLED", false);
    public static readonly ShaderOption<bool> BatchSlicing = new("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", false);
    public static readonly ShaderOption<bool> UniformMask = new("VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK", false);
    public static readonly ShaderOption<bool> UpsampleDenoise = new("VGE_LUMON_UPSAMPLE_DENOISE", true);
    public static readonly ShaderOption<bool> UpsampleHoleFill = new("VGE_LUMON_UPSAMPLE_HOLEFILL", true);
    public static readonly ShaderOption<float> EmissiveBoost = new("LUMON_EMISSIVE_BOOST", 1f);
    public static readonly ShaderOption<int> AtlasTexelsPerFrame = new("VGE_LUMON_ATLAS_TEXELS_PER_FRAME", 16);
    public static readonly ShaderOption<int> HzbCoarseMip = new("VGE_LUMON_HZB_COARSE_MIP", 4);
    public static readonly ShaderOption<float> RayMaxDistance = new("VGE_LUMON_RAY_MAX_DISTANCE", 4f);
    public static readonly ShaderOption<int> RaySteps = new("VGE_LUMON_RAY_STEPS", 10);
    public static readonly ShaderOption<float> RayThickness = new("VGE_LUMON_RAY_THICKNESS", 0.5f);
    public static readonly ShaderOption<float> SkyMissWeight = new("VGE_LUMON_SKY_MISS_WEIGHT", 0.5f);
    public static readonly ShaderOption<float> MinConfidenceWeight = new("VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT", 0.1f);
    public static readonly ShaderOption<int> ExploreCount = new("VGE_LUMON_PROBE_PIS_EXPLORE_COUNT", -1);
    public static readonly ShaderOption<float> ExploreFraction = new("VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION", 0.25f);
    public static readonly ShaderOption<float> WeightEpsilon = new("VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON", 1e-6f);
    public static readonly ShaderOption<float> WorldProbeBaseSpacing = new("VGE_LUMON_WORLDPROBE_BASE_SPACING", 0f);
    public static readonly ShaderOption<int> WorldProbeLevels = new("VGE_LUMON_WORLDPROBE_LEVELS", 0);
    public static readonly ShaderOption<int> WorldProbeOctahedralSize = new("VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE", 16);
    public static readonly ShaderOption<int> WorldProbeResolution = new("VGE_LUMON_WORLDPROBE_RESOLUTION", 0);
    public static readonly ShaderOption<int> WorldProbeDiffuseStride = new("VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE", 2);
}
