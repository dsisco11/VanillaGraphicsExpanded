using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// Builds a cache-only warmup plan: schedules only cache hits and does not trigger procedural builds or bakes.
/// </summary>
internal sealed class MaterialAtlasCacheWarmupPlanner
{
    private readonly IMaterialAtlasDiskCache diskCache;
    private readonly MaterialAtlasCacheKeyBuilder cacheKeyBuilder;

    public MaterialAtlasCacheWarmupPlanner(IMaterialAtlasDiskCache diskCache, MaterialAtlasCacheKeyBuilder cacheKeyBuilder)
    {
        this.diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        this.cacheKeyBuilder = cacheKeyBuilder ?? throw new ArgumentNullException(nameof(cacheKeyBuilder));
    }

    public MaterialAtlasCacheWarmupPlan CreatePlan(
        int generationId,
        AtlasBuildPlan materialParamsPlan,
        MaterialAtlasNormalDepthBuildPlan? normalDepthPlan,
        MaterialAtlasCacheKeyInputs cacheInputs,
        bool enableCache)
    {
        if (!enableCache)
        {
            return new MaterialAtlasCacheWarmupPlan(
                MaterialParamsCpuJobs: Array.Empty<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>>(),
                NormalDepthCpuJobs: Array.Empty<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>>(),
                MaterialParamsPlanned: 0,
                NormalDepthPlanned: 0);
        }

        var overridesByRect = new Dictionary<(int atlasTexId, AtlasRect rect), AtlasBuildPlan.MaterialParamsOverrideJob>(capacity: materialParamsPlan.MaterialParamsOverrides.Count);
        foreach (var ov in materialParamsPlan.MaterialParamsOverrides)
        {
            overridesByRect[(ov.AtlasTextureId, ov.Rect)] = ov;
        }

        var materialJobs = new List<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>>(capacity: materialParamsPlan.MaterialParamsTiles.Count);

        foreach (var tile in materialParamsPlan.MaterialParamsTiles)
        {
            bool hasOverride = overridesByRect.TryGetValue((tile.AtlasTextureId, tile.Rect), out AtlasBuildPlan.MaterialParamsOverrideJob ov);

            if (hasOverride)
            {
                AtlasCacheKey overrideKey = cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                    cacheInputs,
                    tile.AtlasTextureId,
                    tile.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.Scale,
                    ov.RuleId,
                    ov.RuleSource);

                if (diskCache.HasMaterialParamsTile(overrideKey))
                {
                    materialJobs.Add(new MaterialAtlasParamsCpuDiskCacheOnlyJob(
                        GenerationId: generationId,
                        AtlasTextureId: tile.AtlasTextureId,
                        Rect: tile.Rect,
                        TargetTexture: tile.Texture,
                        DiskCache: diskCache,
                        CacheKey: overrideKey,
                        Priority: tile.Priority));
                }

                continue;
            }

            AtlasCacheKey baseKey = cacheKeyBuilder.BuildMaterialParamsTileKey(
                cacheInputs,
                tile.AtlasTextureId,
                tile.Rect,
                tile.Texture,
                tile.Definition,
                tile.Scale);

            if (diskCache.HasMaterialParamsTile(baseKey))
            {
                materialJobs.Add(new MaterialAtlasParamsCpuDiskCacheOnlyJob(
                    GenerationId: generationId,
                    AtlasTextureId: tile.AtlasTextureId,
                    Rect: tile.Rect,
                    TargetTexture: tile.Texture,
                    DiskCache: diskCache,
                    CacheKey: baseKey,
                    Priority: tile.Priority));
            }
        }

        // Override-only rects: schedule only if the post-override cache entry exists.
        var tileRects = new HashSet<(int atlasTexId, AtlasRect rect)>(capacity: materialParamsPlan.MaterialParamsTiles.Count);
        foreach (var tile in materialParamsPlan.MaterialParamsTiles)
        {
            tileRects.Add((tile.AtlasTextureId, tile.Rect));
        }

        foreach (var ov in materialParamsPlan.MaterialParamsOverrides)
        {
            if (tileRects.Contains((ov.AtlasTextureId, ov.Rect)))
            {
                continue;
            }

            AtlasCacheKey overrideKey = cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                cacheInputs,
                ov.AtlasTextureId,
                ov.Rect,
                ov.TargetTexture,
                ov.OverrideTexture,
                ov.Scale,
                ov.RuleId,
                ov.RuleSource);

            if (diskCache.HasMaterialParamsTile(overrideKey))
            {
                materialJobs.Add(new MaterialAtlasParamsCpuDiskCacheOnlyJob(
                    GenerationId: generationId,
                    AtlasTextureId: ov.AtlasTextureId,
                    Rect: ov.Rect,
                    TargetTexture: ov.TargetTexture,
                    DiskCache: diskCache,
                    CacheKey: overrideKey,
                    Priority: ov.Priority));
            }
        }

        var normalDepthJobs = new List<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>>(capacity: normalDepthPlan is null ? 0 : normalDepthPlan.BakeJobs.Count + normalDepthPlan.OverrideJobs.Count);

        if (normalDepthPlan is not null)
        {
            var pageSizes = new Dictionary<int, (int w, int h)>(capacity: normalDepthPlan.Pages.Count);
            foreach (var page in normalDepthPlan.Pages)
            {
                pageSizes[page.AtlasTextureId] = (page.Width, page.Height);
            }

            foreach (var job in normalDepthPlan.BakeJobs)
            {
                if (!pageSizes.TryGetValue(job.AtlasTextureId, out (int w, int h) size))
                {
                    continue;
                }

                AtlasCacheKey key = cacheKeyBuilder.BuildNormalDepthTileKey(
                    cacheInputs,
                    job.AtlasTextureId,
                    job.Rect,
                    job.SourceTexture,
                    job.NormalScale,
                    job.DepthScale);

                if (!diskCache.HasNormalDepthTile(key))
                {
                    continue;
                }

                normalDepthJobs.Add(new MaterialAtlasNormalDepthCpuDiskCacheOnlyJob(
                    GenerationId: generationId,
                    AtlasTextureId: job.AtlasTextureId,
                    Rect: job.Rect,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    TargetTexture: job.SourceTexture,
                    OverrideTexture: null,
                    NormalScale: job.NormalScale,
                    DepthScale: job.DepthScale,
                    RuleId: null,
                    RuleSource: null,
                    DiskCache: diskCache,
                    CacheKey: key,
                    Priority: job.Priority));
            }

            foreach (var ov in normalDepthPlan.OverrideJobs)
            {
                if (!pageSizes.TryGetValue(ov.AtlasTextureId, out (int w, int h) size))
                {
                    continue;
                }

                AtlasCacheKey key = cacheKeyBuilder.BuildNormalDepthOverrideTileKey(
                    cacheInputs,
                    ov.AtlasTextureId,
                    ov.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.NormalScale,
                    ov.DepthScale,
                    ov.RuleId,
                    ov.RuleSource);

                if (!diskCache.HasNormalDepthTile(key))
                {
                    continue;
                }

                normalDepthJobs.Add(new MaterialAtlasNormalDepthCpuDiskCacheOnlyJob(
                    GenerationId: generationId,
                    AtlasTextureId: ov.AtlasTextureId,
                    Rect: ov.Rect,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    TargetTexture: ov.TargetTexture,
                    OverrideTexture: ov.OverrideTexture,
                    NormalScale: ov.NormalScale,
                    DepthScale: ov.DepthScale,
                    RuleId: ov.RuleId,
                    RuleSource: ov.RuleSource,
                    DiskCache: diskCache,
                    CacheKey: key,
                    Priority: ov.Priority));
            }
        }

        return new MaterialAtlasCacheWarmupPlan(
            MaterialParamsCpuJobs: materialJobs,
            NormalDepthCpuJobs: normalDepthJobs,
            MaterialParamsPlanned: materialJobs.Count,
            NormalDepthPlanned: normalDepthJobs.Count);
    }
}
