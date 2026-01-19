using System;
using System.Globalization;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Stable, deterministic inputs used when building per-tile cache keys.
/// </summary>
internal readonly record struct MaterialAtlasCacheKeyInputs(int SchemaVersion, string StablePrefix)
{
    public static MaterialAtlasCacheKeyInputs Create(LumOnConfig config, AtlasSnapshot snapshot, PbrMaterialRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        // Bump whenever the stable prefix contract changes.
        const int SchemaVersion = 4;

        string prefix = FormattableString.Invariant(
            $"schema={SchemaVersion}|atlas=(reload={snapshot.ReloadIteration},nonNull={snapshot.NonNullPositionCount})|matDefs={registry.MaterialById.Count}|mapRules={registry.MappingRules.Count}|defScale=({registry.DefaultScale.Roughness:R},{registry.DefaultScale.Metallic:R},{registry.DefaultScale.Emissive:R},{registry.DefaultScale.Normal:R},{registry.DefaultScale.Depth:R})|ndEnabled={config.EnableNormalDepthAtlas}|ndBake=({config.NormalDepthBake.SigmaBig:R},{config.NormalDepthBake.Sigma1:R},{config.NormalDepthBake.Sigma2:R},{config.NormalDepthBake.Sigma3:R},{config.NormalDepthBake.Sigma4:R},{config.NormalDepthBake.W1:R},{config.NormalDepthBake.W2:R},{config.NormalDepthBake.W3:R},{config.NormalDepthBake.Gain:R},{config.NormalDepthBake.MaxSlope:R},{config.NormalDepthBake.EdgeT0:R},{config.NormalDepthBake.EdgeT1:R},{config.NormalDepthBake.MultigridVCycles},{config.NormalDepthBake.MultigridPreSmooth},{config.NormalDepthBake.MultigridPostSmooth},{config.NormalDepthBake.MultigridCoarsestIters},{config.NormalDepthBake.HeightStrength:R},{config.NormalDepthBake.Gamma:R},{config.NormalDepthBake.NormalStrength:R})");

        return new MaterialAtlasCacheKeyInputs(SchemaVersion, prefix);
    }
}
