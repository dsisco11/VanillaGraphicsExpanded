namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Canonical lighting option metadata shared by attributed shader owners.</summary>
internal static partial class LumOnShaderOptions
{
    #region Shared option keys
    /// <summary>Declares the AmbientOcclusion option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_ENABLE_AO", true)]
    public static partial ShaderOption<bool> AmbientOcclusion { get; }

    /// <summary>Declares the AtlasTexelsPerFrame option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_ATLAS_TEXELS_PER_FRAME", 16)]
    public static partial ShaderOption<int> AtlasTexelsPerFrame { get; }

    /// <summary>Declares the BatchSlicing option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", false)]
    public static partial ShaderOption<bool> BatchSlicing { get; }

    /// <summary>Declares the DirectVisibility option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", false)]
    public static partial ShaderOption<bool> DirectVisibility { get; }

    /// <summary>Declares the EmissiveBoost option and its canonical default.</summary>
    [ShaderOption("LUMON_EMISSIVE_BOOST", 1f)]
    public static partial ShaderOption<float> EmissiveBoost { get; }

    /// <summary>Declares the Enabled option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_ENABLED", true)]
    public static partial ShaderOption<bool> Enabled { get; }

    /// <summary>Declares the ExploreCount option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_EXPLORE_COUNT", -1)]
    public static partial ShaderOption<int> ExploreCount { get; }

    /// <summary>Declares the ExploreFraction option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION", 0.25f)]
    public static partial ShaderOption<float> ExploreFraction { get; }

    /// <summary>Declares the HzbCoarseMip option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_HZB_COARSE_MIP", 4)]
    public static partial ShaderOption<int> HzbCoarseMip { get; }

    /// <summary>Declares the ImportanceSampling option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_ENABLED", false)]
    public static partial ShaderOption<bool> ImportanceSampling { get; }

    /// <summary>Declares the MinConfidenceWeight option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT", 0.1f)]
    public static partial ShaderOption<float> MinConfidenceWeight { get; }

    /// <summary>Declares the NearField option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_NEAR_FIELD_ENABLED", false)]
    public static partial ShaderOption<bool> NearField { get; }

    /// <summary>Declares the PbrComposite option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PBR_COMPOSITE", true)]
    public static partial ShaderOption<bool> PbrComposite { get; }

    /// <summary>Declares the RayMaxDistance option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_RAY_MAX_DISTANCE", 4f)]
    public static partial ShaderOption<float> RayMaxDistance { get; }

    /// <summary>Declares the RaySteps option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_RAY_STEPS", 10)]
    public static partial ShaderOption<int> RaySteps { get; }

    /// <summary>Declares the RayThickness option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_RAY_THICKNESS", 0.5f)]
    public static partial ShaderOption<float> RayThickness { get; }

    /// <summary>Declares the ShortRangeAo option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_ENABLE_SHORT_RANGE_AO", true, Aliases = new[] { "VGE_LUMON_ENABLE_BENT_NORMAL" })]
    public static partial ShaderOption<bool> ShortRangeAo { get; }

    /// <summary>Declares the SkyMissWeight option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_SKY_MISS_WEIGHT", 0.5f)]
    public static partial ShaderOption<float> SkyMissWeight { get; }

    /// <summary>Declares the UniformMask option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK", false)]
    public static partial ShaderOption<bool> UniformMask { get; }

    /// <summary>Declares the UpsampleDenoise option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_UPSAMPLE_DENOISE", true)]
    public static partial ShaderOption<bool> UpsampleDenoise { get; }

    /// <summary>Declares the UpsampleHoleFill option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_UPSAMPLE_HOLEFILL", true)]
    public static partial ShaderOption<bool> UpsampleHoleFill { get; }

    /// <summary>Declares the WeightEpsilon option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON", 1e-6f)]
    public static partial ShaderOption<float> WeightEpsilon { get; }

    /// <summary>Declares the WorldProbeBaseSpacing option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_BASE_SPACING", 0f)]
    public static partial ShaderOption<float> WorldProbeBaseSpacing { get; }

    /// <summary>Declares the WorldProbeDiffuseStride option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE", 2)]
    public static partial ShaderOption<int> WorldProbeDiffuseStride { get; }

    /// <summary>Declares the WorldProbeLevels option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_LEVELS", 0)]
    public static partial ShaderOption<int> WorldProbeLevels { get; }

    /// <summary>Declares the WorldProbeOctahedralSize option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE", 16)]
    public static partial ShaderOption<int> WorldProbeOctahedralSize { get; }

    /// <summary>Declares the WorldProbeResolution option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_RESOLUTION", 0)]
    public static partial ShaderOption<int> WorldProbeResolution { get; }

    /// <summary>Declares the WorldProbes option and its canonical default.</summary>
    [ShaderOption("VGE_LUMON_WORLDPROBE_ENABLED", false)]
    public static partial ShaderOption<bool> WorldProbes { get; }

    #endregion
}
