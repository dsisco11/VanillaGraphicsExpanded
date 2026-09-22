using System.Linq;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderOptions;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Stable lighting specialization IDs and structural availability conditions.</summary>
internal static class LumOnShaderConstants
{
    #region Declarations
    /// <summary>Declares world-probe inputs, including diffuse stride only for gather consumers.</summary>
    public static ShaderSpecialization[] World(bool gather)
    {
        var condition = ShaderCondition.Equal(WorldProbes, true);
        ShaderSpecialization[] constants =
        [
            new(11, WorldProbeBaseSpacing, condition), new(12, WorldProbeLevels, condition),
            new(13, WorldProbeOctahedralSize, condition), new(14, WorldProbeResolution, condition)
        ];
        return gather ? [.. constants, new(15, WorldProbeDiffuseStride, condition)] : constants;
    }

    /// <summary>Declares trace numeric inputs and the sky fallback available without near-field continuation.</summary>
    public static ShaderSpecialization[] Trace() =>
    [
        new(0, EmissiveBoost), new(1, AtlasTexelsPerFrame), new(2, HzbCoarseMip),
        new(3, RayMaxDistance), new(4, RaySteps), new(5, RayThickness),
        new(6, SkyMissWeight, ShaderCondition.Equal(NearField, false)), .. World(false)
    ];

    /// <summary>Declares exploration inputs only for importance sampling without either override.</summary>
    public static ShaderSpecialization[] Mask()
    {
        var exploration = ShaderCondition.All(ShaderCondition.Equal(ImportanceSampling, true),
            ShaderCondition.Equal(BatchSlicing, false), ShaderCondition.Equal(UniformMask, false));
        return [new(1, AtlasTexelsPerFrame), new(7, MinConfidenceWeight),
            new(8, ExploreCount, exploration), new(9, ExploreFraction, exploration), new(10, WeightEpsilon, exploration)];
    }
    #endregion
}
