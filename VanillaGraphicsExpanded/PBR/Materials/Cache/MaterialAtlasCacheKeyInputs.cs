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
        const int SchemaVersion = 2;

        string prefix = string.Format(
            CultureInfo.InvariantCulture,
            "schema={0}|reload={1}|nonNull={2}|pages={3}|matDefs={4}|mapRules={5}|defScale=({6:R},{7:R},{8:R},{9:R},{10:R})|ndEnabled={11}|ndBake=({12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21:R},{22:R},{23:R},{24},{25},{26},{27:R},{28:R},{29:R})",
            SchemaVersion,
            snapshot.ReloadIteration,
            snapshot.NonNullPositionCount,
            snapshot.Pages.Count,
            registry.MaterialById.Count,
            registry.MappingRules.Count,
            registry.DefaultScale.Roughness,
            registry.DefaultScale.Metallic,
            registry.DefaultScale.Emissive,
            registry.DefaultScale.Normal,
            registry.DefaultScale.Depth,
            config.EnableNormalDepthAtlas,
            config.NormalDepthBake.SigmaBig,
            config.NormalDepthBake.Sigma1,
            config.NormalDepthBake.Sigma2,
            config.NormalDepthBake.Sigma3,
            config.NormalDepthBake.Sigma4,
            config.NormalDepthBake.W1,
            config.NormalDepthBake.W2,
            config.NormalDepthBake.W3,
            config.NormalDepthBake.Gain,
            config.NormalDepthBake.MaxSlope,
            config.NormalDepthBake.EdgeT0,
            config.NormalDepthBake.EdgeT1,
            config.NormalDepthBake.MultigridVCycles,
            config.NormalDepthBake.MultigridPreSmooth,
            config.NormalDepthBake.MultigridPostSmooth,
            config.NormalDepthBake.MultigridCoarsestIters,
            config.NormalDepthBake.HeightStrength,
            config.NormalDepthBake.Gamma,
            config.NormalDepthBake.NormalStrength);

        return new MaterialAtlasCacheKeyInputs(SchemaVersion, prefix);
    }
}
