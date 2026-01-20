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
            LumOnConfig.MaterialAtlasConfig atlas = config.MaterialAtlas;
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        // Bump whenever the stable prefix contract changes.
        const int SchemaVersion = 4;

            string prefix = FormattableString.Invariant(
                $"schema={SchemaVersion}|atlas=(reload={snapshot.ReloadIteration},nonNull={snapshot.NonNullPositionCount})|matDefs={registry.MaterialById.Count}|mapRules={registry.MappingRules.Count}|defScale=({registry.DefaultScale.Roughness:R},{registry.DefaultScale.Metallic:R},{registry.DefaultScale.Emissive:R},{registry.DefaultScale.Normal:R},{registry.DefaultScale.Depth:R})|ndEnabled={atlas.EnableNormalMaps}|ndBake=({atlas.NormalDepthBake.SigmaBig:R},{atlas.NormalDepthBake.Sigma1:R},{atlas.NormalDepthBake.Sigma2:R},{atlas.NormalDepthBake.Sigma3:R},{atlas.NormalDepthBake.Sigma4:R},{atlas.NormalDepthBake.W1:R},{atlas.NormalDepthBake.W2:R},{atlas.NormalDepthBake.W3:R},{atlas.NormalDepthBake.Gain:R},{atlas.NormalDepthBake.MaxSlope:R},{atlas.NormalDepthBake.EdgeT0:R},{atlas.NormalDepthBake.EdgeT1:R},{atlas.NormalDepthBake.MultigridVCycles},{atlas.NormalDepthBake.MultigridPreSmooth},{atlas.NormalDepthBake.MultigridPostSmooth},{atlas.NormalDepthBake.MultigridCoarsestIters},{atlas.NormalDepthBake.HeightStrength:R},{atlas.NormalDepthBake.Gamma:R},{atlas.NormalDepthBake.NormalStrength:R})");

        return new MaterialAtlasCacheKeyInputs(SchemaVersion, prefix);
    }
}
