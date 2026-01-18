using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Pure planning layer that turns an <see cref="AtlasSnapshot"/> plus the current material registry into a deterministic
/// <see cref="AtlasBuildPlan"/>.
/// </summary>
internal sealed class MaterialAtlasBuildPlanner
{
    /// <summary>
    /// Creates a build plan from a snapshot and registry state.
    /// The provided <paramref name="tryGetAtlasPosition"/> must be a pure lookup (no mutation) for determinism.
    /// </summary>
    public AtlasBuildPlan CreatePlan(
        AtlasSnapshot snapshot,
        System.Func<AssetLocation, TextureAtlasPosition?> tryGetAtlasPosition,
        PbrMaterialRegistry registry,
        IEnumerable<AssetLocation> blockTextureAssets,
        bool enableNormalDepth)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tryGetAtlasPosition);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(blockTextureAssets);

        var pages = snapshot.Pages
            .Select(p => new AtlasBuildPlan.AtlasPagePlan(p.AtlasTextureId, p.Width, p.Height))
            .OrderBy(p => p.AtlasTextureId)
            .ToArray();

        var sizesByAtlasTexId = snapshot.Pages.ToDictionary(p => p.AtlasTextureId, p => (p.Width, p.Height));

        var materialParamsTiles = new List<AtlasBuildPlan.MaterialParamsTileJob>(capacity: registry.MaterialIdByTexture.Count);
        var materialParamsOverrides = new List<AtlasBuildPlan.MaterialParamsOverrideJob>(capacity: registry.OverridesByTexture.Count);

        var normalDepthTiles = new List<AtlasBuildPlan.NormalDepthTileJob>();
        var normalDepthOverrides = new List<AtlasBuildPlan.NormalDepthOverrideJob>();

        int missingAtlasPositions = 0;
        int emptyRects = 0;

        // Material params: procedural tiles are only for textures that have a material definition.
        foreach (AssetLocation texture in registry.MaterialIdByTexture.Keys.OrderBy(l => l.Domain, StringComparer.Ordinal).ThenBy(l => l.Path, StringComparer.Ordinal))
        {
            if (!registry.TryGetMaterial(texture, out PbrMaterialDefinition definition))
            {
                continue;
            }

            PbrOverrideScale scale = definition.Scale;
            if (registry.ScaleByTexture.TryGetValue(texture, out PbrOverrideScale combinedScale))
            {
                scale = combinedScale;
                definition = definition with { Scale = combinedScale };
            }

            if (!PbrMaterialAtlasPositionResolver.TryResolve(tryGetAtlasPosition, texture, out TextureAtlasPosition? texPos)
                || texPos is null)
            {
                missingAtlasPositions++;
                continue;
            }

            if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int w, int h) size))
            {
                missingAtlasPositions++;
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.w, size.h, out AtlasRect rect))
            {
                emptyRects++;
                continue;
            }

            materialParamsTiles.Add(new AtlasBuildPlan.MaterialParamsTileJob(
                AtlasTextureId: texPos.atlasTextureId,
                Rect: rect,
                Texture: texture,
                Definition: definition,
                Scale: scale,
                Priority: 0));
        }

        // Material params overrides: apply regardless of whether the texture has a procedural material definition.
        // This matches legacy behavior: if an override exists and the texture resolves to an atlas rect, it is applied.
        foreach ((AssetLocation targetTexture, PbrMaterialTextureOverrides overrides) in registry.OverridesByTexture
                     .OrderBy(kvp => kvp.Key.Domain, StringComparer.Ordinal)
                     .ThenBy(kvp => kvp.Key.Path, StringComparer.Ordinal))
        {
            if (overrides.MaterialParams is null)
            {
                continue;
            }

            if (!PbrMaterialAtlasPositionResolver.TryResolve(tryGetAtlasPosition, targetTexture, out TextureAtlasPosition? texPos)
                || texPos is null)
            {
                missingAtlasPositions++;
                continue;
            }

            if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int w, int h) size))
            {
                missingAtlasPositions++;
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.w, size.h, out AtlasRect rect))
            {
                emptyRects++;
                continue;
            }

            materialParamsOverrides.Add(new AtlasBuildPlan.MaterialParamsOverrideJob(
                AtlasTextureId: texPos.atlasTextureId,
                Rect: rect,
                TargetTexture: targetTexture,
                OverrideTexture: overrides.MaterialParams,
                RuleId: overrides.RuleId,
                RuleSource: overrides.RuleSource,
                Scale: overrides.Scale,
                Priority: 0));
        }

        // Normal+depth: asset-scanned textures/block are the source of truth for resolving scales.
        if (enableNormalDepth)
        {
            IReadOnlyList<AssetLocation> normalizedAssets = MaterialAtlasAssetScan.NormalizeSortAndDedupeBlockTextures(blockTextureAssets);

            // Deduplicate by (atlas page id + rect) to avoid redundant work.
            var seen = new HashSet<(int atlasTexId, AtlasRect rect)>();

            foreach (AssetLocation texture in normalizedAssets)
            {
                if (!PbrMaterialAtlasPositionResolver.TryResolve(tryGetAtlasPosition, texture, out TextureAtlasPosition? texPos)
                    || texPos is null)
                {
                    missingAtlasPositions++;
                    continue;
                }

                if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int w, int h) size))
                {
                    missingAtlasPositions++;
                    continue;
                }

                if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.w, size.h, out AtlasRect rect))
                {
                    emptyRects++;
                    continue;
                }

                if (!seen.Add((texPos.atlasTextureId, rect)))
                {
                    continue;
                }

                float normalScale = registry.DefaultScale.Normal;
                float depthScale = registry.DefaultScale.Depth;

                if (registry.TryGetScale(texture, out PbrOverrideScale scale))
                {
                    normalScale = scale.Normal;
                    depthScale = scale.Depth;
                }

                normalDepthTiles.Add(new AtlasBuildPlan.NormalDepthTileJob(
                    AtlasTextureId: texPos.atlasTextureId,
                    Rect: rect,
                    Texture: texture,
                    NormalScale: normalScale,
                    DepthScale: depthScale,
                    Priority: 0));

                if (registry.OverridesByTexture.TryGetValue(texture, out PbrMaterialTextureOverrides overrides)
                    && overrides.NormalHeight is not null)
                {
                    normalDepthOverrides.Add(new AtlasBuildPlan.NormalDepthOverrideJob(
                        AtlasTextureId: texPos.atlasTextureId,
                        Rect: rect,
                        TargetTexture: texture,
                        OverrideTexture: overrides.NormalHeight,
                        RuleId: overrides.RuleId,
                        RuleSource: overrides.RuleSource,
                        NormalScale: overrides.Scale.Normal,
                        DepthScale: overrides.Scale.Depth,
                        Priority: 0));
                }
            }
        }

        // Deterministic ordering (important for reproducibility and stable testing).
        materialParamsTiles.Sort(JobOrder.MaterialParamsTile);
        materialParamsOverrides.Sort(JobOrder.MaterialParamsOverride);
        normalDepthTiles.Sort(JobOrder.NormalDepthTile);
        normalDepthOverrides.Sort(JobOrder.NormalDepthOverride);

        var stats = new AtlasBuildPlan.AtlasBuildStats(
            MaterialParamsTiles: materialParamsTiles.Count,
            MaterialParamsOverrides: materialParamsOverrides.Count,
            NormalDepthTiles: normalDepthTiles.Count,
            NormalDepthOverrides: normalDepthOverrides.Count,
            MissingAtlasPositions: missingAtlasPositions,
            EmptyRects: emptyRects);

        return new AtlasBuildPlan(
            Snapshot: snapshot,
            Pages: pages,
            MaterialParamsTiles: materialParamsTiles,
            MaterialParamsOverrides: materialParamsOverrides,
            NormalDepthTiles: normalDepthTiles,
            NormalDepthOverrides: normalDepthOverrides,
            Stats: stats);
    }

    private static class JobOrder
    {
        public static int MaterialParamsTile(AtlasBuildPlan.MaterialParamsTileJob a, AtlasBuildPlan.MaterialParamsTileJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.Texture, b.AtlasTextureId, b.Rect, b.Priority, b.Texture);

        public static int MaterialParamsOverride(AtlasBuildPlan.MaterialParamsOverrideJob a, AtlasBuildPlan.MaterialParamsOverrideJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.TargetTexture, b.AtlasTextureId, b.Rect, b.Priority, b.TargetTexture);

        public static int NormalDepthTile(AtlasBuildPlan.NormalDepthTileJob a, AtlasBuildPlan.NormalDepthTileJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.Texture, b.AtlasTextureId, b.Rect, b.Priority, b.Texture);

        public static int NormalDepthOverride(AtlasBuildPlan.NormalDepthOverrideJob a, AtlasBuildPlan.NormalDepthOverrideJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.TargetTexture, b.AtlasTextureId, b.Rect, b.Priority, b.TargetTexture);

        private static int CompareCommon(
            int atlasA,
            AtlasRect rectA,
            int priorityA,
            AssetLocation texA,
            int atlasB,
            AtlasRect rectB,
            int priorityB,
            AssetLocation texB)
        {
            int d = atlasA.CompareTo(atlasB);
            if (d != 0) return d;

            d = priorityB.CompareTo(priorityA); // higher priority first
            if (d != 0) return d;

            d = rectA.Y.CompareTo(rectB.Y);
            if (d != 0) return d;

            d = rectA.X.CompareTo(rectB.X);
            if (d != 0) return d;

            d = rectA.Width.CompareTo(rectB.Width);
            if (d != 0) return d;

            d = rectA.Height.CompareTo(rectB.Height);
            if (d != 0) return d;

            d = string.Compare(texA.Domain, texB.Domain, StringComparison.Ordinal);
            if (d != 0) return d;

            return string.Compare(texA.Path, texB.Path, StringComparison.Ordinal);
        }
    }
}
