namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Reusable accepted option memberships for lighting programs.</summary>
[ShaderGroup("Ao", "ambient-occlusion", nameof(AmbientOcclusion))]
[ShaderGroup("AtlasUpdate", "atlas-update", nameof(AtlasTexelsPerFrame))]
[ShaderGroup("Composite", "composite", nameof(PbrComposite), nameof(ShortRangeAo))]
[ShaderGroup("Lighting", "lighting", nameof(Enabled))]
[ShaderGroup("Orbs", "world-probe-orbs", nameof(WorldProbeOctahedralSize), nameof(WorldProbeResolution))]
[ShaderGroup("Pis", "importance-sampling", nameof(ImportanceSampling), nameof(BatchSlicing))]
[ShaderGroup("PisMask", "importance-mask", nameof(UniformMask), nameof(AtlasTexelsPerFrame), nameof(MinConfidenceWeight), nameof(ExploreCount), nameof(ExploreFraction), nameof(WeightEpsilon))]
[ShaderGroup("Tracing", "tracing", nameof(NearField), nameof(EmissiveBoost), nameof(AtlasTexelsPerFrame), nameof(HzbCoarseMip), nameof(RayMaxDistance), nameof(RaySteps), nameof(RayThickness), nameof(SkyMissWeight))]
[ShaderGroup("Upsample", "upsample", nameof(UpsampleDenoise), nameof(UpsampleHoleFill))]
[ShaderGroup("Visibility", "visibility", nameof(DirectVisibility))]
[ShaderGroup("World", "world-probes", nameof(WorldProbes), nameof(WorldProbeBaseSpacing), nameof(WorldProbeLevels), nameof(WorldProbeOctahedralSize), nameof(WorldProbeResolution))]
[ShaderGroup("WorldGather", "world-probe-gather", nameof(WorldProbeDiffuseStride))]
internal static partial class LumOnShaderGroups
{
    #region Shared option references
    /// <summary>References the canonical AmbientOcclusion metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AmbientOcclusion))]
    internal static partial ShaderOption<bool> AmbientOcclusion { get; }

    /// <summary>References the canonical AtlasTexelsPerFrame metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AtlasTexelsPerFrame))]
    internal static partial ShaderOption<int> AtlasTexelsPerFrame { get; }

    /// <summary>References the canonical BatchSlicing metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.BatchSlicing))]
    internal static partial ShaderOption<bool> BatchSlicing { get; }

    /// <summary>References the canonical DirectVisibility metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    internal static partial ShaderOption<bool> DirectVisibility { get; }

    /// <summary>References the canonical EmissiveBoost metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.EmissiveBoost))]
    internal static partial ShaderOption<float> EmissiveBoost { get; }

    /// <summary>References the canonical Enabled metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.Enabled))]
    internal static partial ShaderOption<bool> Enabled { get; }

    /// <summary>References the canonical ExploreCount metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ExploreCount))]
    internal static partial ShaderOption<int> ExploreCount { get; }

    /// <summary>References the canonical ExploreFraction metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ExploreFraction))]
    internal static partial ShaderOption<float> ExploreFraction { get; }

    /// <summary>References the canonical HzbCoarseMip metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.HzbCoarseMip))]
    internal static partial ShaderOption<int> HzbCoarseMip { get; }

    /// <summary>References the canonical ImportanceSampling metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ImportanceSampling))]
    internal static partial ShaderOption<bool> ImportanceSampling { get; }

    /// <summary>References the canonical MinConfidenceWeight metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.MinConfidenceWeight))]
    internal static partial ShaderOption<float> MinConfidenceWeight { get; }

    /// <summary>References the canonical NearField metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.NearField))]
    internal static partial ShaderOption<bool> NearField { get; }

    /// <summary>References the canonical PbrComposite metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.PbrComposite))]
    internal static partial ShaderOption<bool> PbrComposite { get; }

    /// <summary>References the canonical RayMaxDistance metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayMaxDistance))]
    internal static partial ShaderOption<float> RayMaxDistance { get; }

    /// <summary>References the canonical RaySteps metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RaySteps))]
    internal static partial ShaderOption<int> RaySteps { get; }

    /// <summary>References the canonical RayThickness metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayThickness))]
    internal static partial ShaderOption<float> RayThickness { get; }

    /// <summary>References the canonical ShortRangeAo metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ShortRangeAo))]
    internal static partial ShaderOption<bool> ShortRangeAo { get; }

    /// <summary>References the canonical SkyMissWeight metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.SkyMissWeight))]
    internal static partial ShaderOption<float> SkyMissWeight { get; }

    /// <summary>References the canonical UniformMask metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UniformMask))]
    internal static partial ShaderOption<bool> UniformMask { get; }

    /// <summary>References the canonical UpsampleDenoise metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UpsampleDenoise))]
    internal static partial ShaderOption<bool> UpsampleDenoise { get; }

    /// <summary>References the canonical UpsampleHoleFill metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UpsampleHoleFill))]
    internal static partial ShaderOption<bool> UpsampleHoleFill { get; }

    /// <summary>References the canonical WeightEpsilon metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WeightEpsilon))]
    internal static partial ShaderOption<float> WeightEpsilon { get; }

    /// <summary>References the canonical WorldProbeBaseSpacing metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeBaseSpacing))]
    internal static partial ShaderOption<float> WorldProbeBaseSpacing { get; }

    /// <summary>References the canonical WorldProbeDiffuseStride metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeDiffuseStride))]
    internal static partial ShaderOption<int> WorldProbeDiffuseStride { get; }

    /// <summary>References the canonical WorldProbeLevels metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeLevels))]
    internal static partial ShaderOption<int> WorldProbeLevels { get; }

    /// <summary>References the canonical WorldProbeOctahedralSize metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeOctahedralSize))]
    internal static partial ShaderOption<int> WorldProbeOctahedralSize { get; }

    /// <summary>References the canonical WorldProbeResolution metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeResolution))]
    internal static partial ShaderOption<int> WorldProbeResolution { get; }

    /// <summary>References the canonical WorldProbes metadata.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbes))]
    internal static partial ShaderOption<bool> WorldProbes { get; }

    #endregion
}
