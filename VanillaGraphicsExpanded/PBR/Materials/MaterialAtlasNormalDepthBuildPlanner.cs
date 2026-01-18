using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Pure planning layer for the normal+depth atlas GPU bake.
/// Produces bake jobs for atlas rects and override jobs to apply after baking.
/// </summary>
internal sealed class MaterialAtlasNormalDepthBuildPlanner
{
    public MaterialAtlasNormalDepthBuildPlan CreatePlan(
        AtlasSnapshot snapshot,
        System.Func<AssetLocation, TextureAtlasPosition?> tryGetAtlasPosition,
        PbrMaterialRegistry registry,
        IEnumerable<AssetLocation> blockTextureAssets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tryGetAtlasPosition);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(blockTextureAssets);

        var pages = snapshot.Pages
            .Select(p => new MaterialAtlasNormalDepthBuildPlan.PagePlan(p.AtlasTextureId, p.Width, p.Height))
            .OrderBy(p => p.AtlasTextureId)
            .ToArray();

        var pageSizes = snapshot.Pages.ToDictionary(p => p.AtlasTextureId, p => (p.Width, p.Height));

        var overrideJobs = new List<MaterialAtlasNormalDepthBuildPlan.OverrideJob>(capacity: registry.OverridesByTexture.Count);
        var overrideRectsByPage = new Dictionary<int, HashSet<AtlasRect>>();

        int missingAtlasPositions = 0;
        int emptyRects = 0;

        // 1) Collect explicit overrides (these are applied after baking).
        foreach ((AssetLocation targetTexture, PbrMaterialTextureOverrides overrides) in registry.OverridesByTexture
                     .OrderBy(kvp => kvp.Key.Domain, StringComparer.Ordinal)
                     .ThenBy(kvp => kvp.Key.Path, StringComparer.Ordinal))
        {
            if (overrides.NormalHeight is null)
            {
                continue;
            }

            if (!MaterialAtlasKeyResolver.TryResolve(tryGetAtlasPosition, targetTexture, out TextureAtlasPosition? texPos)
                || texPos is null)
            {
                missingAtlasPositions++;
                continue;
            }

            if (!pageSizes.TryGetValue(texPos.atlasTextureId, out (int w, int h) size))
            {
                missingAtlasPositions++;
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.w, size.h, out AtlasRect rect))
            {
                emptyRects++;
                continue;
            }

            overrideJobs.Add(new MaterialAtlasNormalDepthBuildPlan.OverrideJob(
                AtlasTextureId: texPos.atlasTextureId,
                Rect: rect,
                TargetTexture: targetTexture,
                OverrideTexture: overrides.NormalHeight,
                NormalScale: overrides.Scale.Normal,
                DepthScale: overrides.Scale.Depth,
                RuleId: overrides.RuleId,
                RuleSource: overrides.RuleSource,
                Priority: 0));

            if (!overrideRectsByPage.TryGetValue(texPos.atlasTextureId, out HashSet<AtlasRect>? set))
            {
                set = new HashSet<AtlasRect>();
                overrideRectsByPage[texPos.atlasTextureId] = set;
            }

            set.Add(rect);
        }

        // 2) Seed bake rects from the atlas positions array (captures inserted textures without stable asset keys).
        var bakeByKey = new Dictionary<(int atlasTexId, AtlasRect rect), MaterialAtlasNormalDepthBuildPlan.BakeJob>(capacity: snapshot.Positions.Length);

        int skippedByOverrides = 0;
        PbrOverrideScale defaultScale = registry.DefaultScale;

        foreach (TextureAtlasPosition? pos in snapshot.Positions)
        {
            if (pos is null)
            {
                continue;
            }

            if (!pageSizes.TryGetValue(pos.atlasTextureId, out (int w, int h) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(pos, size.w, size.h, out AtlasRect rect))
            {
                continue;
            }

            if (overrideRectsByPage.TryGetValue(pos.atlasTextureId, out HashSet<AtlasRect>? overridesForPage)
                && overridesForPage.Contains(rect))
            {
                skippedByOverrides++;
                continue;
            }

            var key = (pos.atlasTextureId, rect);
            bakeByKey.TryAdd(
                key,
                new MaterialAtlasNormalDepthBuildPlan.BakeJob(
                    AtlasTextureId: pos.atlasTextureId,
                    Rect: rect,
                    NormalScale: defaultScale.Normal,
                    DepthScale: defaultScale.Depth,
                    SourceTexture: null,
                    Priority: 0));
        }

        // 3) Refine scales using the registry scale map (block textures only).
        foreach ((AssetLocation tex, PbrOverrideScale scale) in registry.ScaleByTexture
                     .OrderBy(kvp => kvp.Key.Domain, StringComparer.Ordinal)
                     .ThenBy(kvp => kvp.Key.Path, StringComparer.Ordinal))
        {
            if (!tex.Path.StartsWith("textures/block/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!MaterialAtlasKeyResolver.TryResolve(tryGetAtlasPosition, tex, out TextureAtlasPosition? pos)
                || pos is null)
            {
                missingAtlasPositions++;
                continue;
            }

            if (!pageSizes.TryGetValue(pos.atlasTextureId, out (int w, int h) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(pos, size.w, size.h, out AtlasRect rect))
            {
                emptyRects++;
                continue;
            }

            if (overrideRectsByPage.TryGetValue(pos.atlasTextureId, out HashSet<AtlasRect>? overridesForPage)
                && overridesForPage.Contains(rect))
            {
                skippedByOverrides++;
                continue;
            }

            var key = (pos.atlasTextureId, rect);
            bakeByKey[key] = new MaterialAtlasNormalDepthBuildPlan.BakeJob(
                AtlasTextureId: pos.atlasTextureId,
                Rect: rect,
                NormalScale: scale.Normal,
                DepthScale: scale.Depth,
                SourceTexture: tex,
                Priority: 0);
        }

        // 4) Refine scales using asset scan (more exhaustive; also captures textures missing from ScaleByTexture).
        IReadOnlyList<AssetLocation> normalizedAssets = MaterialAtlasAssetScan.NormalizeSortAndDedupeBlockTextures(blockTextureAssets);
        foreach (AssetLocation tex in normalizedAssets)
        {
            if (!MaterialAtlasKeyResolver.TryResolve(tryGetAtlasPosition, tex, out TextureAtlasPosition? pos)
                || pos is null)
            {
                continue;
            }

            if (!pageSizes.TryGetValue(pos.atlasTextureId, out (int w, int h) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(pos, size.w, size.h, out AtlasRect rect))
            {
                continue;
            }

            if (overrideRectsByPage.TryGetValue(pos.atlasTextureId, out HashSet<AtlasRect>? overridesForPage)
                && overridesForPage.Contains(rect))
            {
                skippedByOverrides++;
                continue;
            }

            float ns = defaultScale.Normal;
            float ds = defaultScale.Depth;
            if (registry.TryGetScale(tex, out PbrOverrideScale scale))
            {
                ns = scale.Normal;
                ds = scale.Depth;
            }

            var key = (pos.atlasTextureId, rect);
            bakeByKey[key] = new MaterialAtlasNormalDepthBuildPlan.BakeJob(
                AtlasTextureId: pos.atlasTextureId,
                Rect: rect,
                NormalScale: ns,
                DepthScale: ds,
                SourceTexture: tex,
                Priority: 0);
        }

        var bakeJobs = bakeByKey.Values.ToList();
        bakeJobs.Sort(JobOrder.Bake);
        overrideJobs.Sort(JobOrder.Override);

        var stats = new MaterialAtlasNormalDepthBuildPlan.Stats(
            BakeJobs: bakeJobs.Count,
            OverrideJobs: overrideJobs.Count,
            SkippedByOverrides: skippedByOverrides,
            MissingAtlasPositions: missingAtlasPositions,
            EmptyRects: emptyRects);

        return new MaterialAtlasNormalDepthBuildPlan(
            Snapshot: snapshot,
            Pages: pages,
            BakeJobs: bakeJobs,
            OverrideJobs: overrideJobs,
            PlanStats: stats);
    }

    private static class JobOrder
    {
        public static int Bake(MaterialAtlasNormalDepthBuildPlan.BakeJob a, MaterialAtlasNormalDepthBuildPlan.BakeJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.SourceTexture, b.AtlasTextureId, b.Rect, b.Priority, b.SourceTexture);

        public static int Override(MaterialAtlasNormalDepthBuildPlan.OverrideJob a, MaterialAtlasNormalDepthBuildPlan.OverrideJob b)
            => CompareCommon(a.AtlasTextureId, a.Rect, a.Priority, a.TargetTexture, b.AtlasTextureId, b.Rect, b.Priority, b.TargetTexture);

        private static int CompareCommon(
            int atlasA,
            AtlasRect rectA,
            int priorityA,
            AssetLocation? texA,
            int atlasB,
            AtlasRect rectB,
            int priorityB,
            AssetLocation? texB)
        {
            int d = atlasA.CompareTo(atlasB);
            if (d != 0) return d;

            d = priorityB.CompareTo(priorityA);
            if (d != 0) return d;

            d = rectA.Y.CompareTo(rectB.Y);
            if (d != 0) return d;

            d = rectA.X.CompareTo(rectB.X);
            if (d != 0) return d;

            d = rectA.Width.CompareTo(rectB.Width);
            if (d != 0) return d;

            d = rectA.Height.CompareTo(rectB.Height);
            if (d != 0) return d;

            string aDomain = texA?.Domain ?? string.Empty;
            string bDomain = texB?.Domain ?? string.Empty;
            d = string.Compare(aDomain, bDomain, StringComparison.Ordinal);
            if (d != 0) return d;

            string aPath = texA?.Path ?? string.Empty;
            string bPath = texB?.Path ?? string.Empty;
            return string.Compare(aPath, bPath, StringComparison.Ordinal);
        }
    }
}
