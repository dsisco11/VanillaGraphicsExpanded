using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderOptions;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Explicitly reused configuration memberships for lighting program owners.</summary>
internal static class LumOnShaderGroups
{
    public static readonly ShaderOptionGroup Lighting = new("lighting", [Enabled]);
    public static readonly ShaderOptionGroup Composite = new("composite", [PbrComposite, ShortRangeAo]);
    public static readonly ShaderOptionGroup Ao = new("ambient-occlusion", [AmbientOcclusion]);
    public static readonly ShaderOptionGroup Visibility = new("visibility", [DirectVisibility]);
    public static readonly ShaderOptionGroup Tracing = new("tracing",
        [NearField, EmissiveBoost, AtlasTexelsPerFrame, HzbCoarseMip, RayMaxDistance, RaySteps, RayThickness, SkyMissWeight]);
    public static readonly ShaderOptionGroup Pis = new("importance-sampling", [ImportanceSampling, BatchSlicing]);
    public static readonly ShaderOptionGroup PisMask = new("importance-mask",
        [UniformMask, AtlasTexelsPerFrame, MinConfidenceWeight, ExploreCount, ExploreFraction, WeightEpsilon]);
    public static readonly ShaderOptionGroup AtlasUpdate = new("atlas-update", [AtlasTexelsPerFrame]);
    public static readonly ShaderOptionGroup World = new("world-probes",
        [WorldProbes, WorldProbeBaseSpacing, WorldProbeLevels, WorldProbeOctahedralSize, WorldProbeResolution]);
    public static readonly ShaderOptionGroup WorldGather = new("world-probe-gather", [WorldProbeDiffuseStride]);
    public static readonly ShaderOptionGroup Orbs = new("world-probe-orbs", [WorldProbeOctahedralSize, WorldProbeResolution]);
    public static readonly ShaderOptionGroup Upsample = new("upsample", [UpsampleDenoise, UpsampleHoleFill]);
}
