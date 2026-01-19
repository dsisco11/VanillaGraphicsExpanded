using System;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct MaterialAtlasNormalDepthCpuJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    int AtlasWidth,
    int AtlasHeight,
    MaterialAtlasNormalDepthGpuJob.Kind JobKind,
    AssetLocation? TargetTexture,
    AssetLocation? OverrideTexture,
    float NormalScale,
    float DepthScale,
    string? RuleId,
    AssetLocation? RuleSource,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>
{
    public MaterialAtlasNormalDepthGpuJob Execute(CancellationToken cancellationToken)
    {
        if (DiskCache is not null && CacheKey.SchemaVersion != 0)
        {
            if (DiskCache.TryLoadNormalDepthTile(CacheKey, out float[] cached))
            {
                return new MaterialAtlasNormalDepthGpuJob(
                    GenerationId: GenerationId,
                    AtlasTextureId: AtlasTextureId,
                    Rect: Rect,
                    AtlasWidth: AtlasWidth,
                    AtlasHeight: AtlasHeight,
                    JobKind: MaterialAtlasNormalDepthGpuJob.Kind.UploadCached,
                    CachedRgbaQuads: cached,
                    TargetTexture: TargetTexture,
                    OverrideTexture: OverrideTexture,
                    NormalScale: NormalScale,
                    DepthScale: DepthScale,
                    RuleId: RuleId,
                    RuleSource: RuleSource,
                    DiskCache: DiskCache,
                    CacheKey: CacheKey,
                    Priority: Priority);
            }
        }

        return new MaterialAtlasNormalDepthGpuJob(
            GenerationId: GenerationId,
            AtlasTextureId: AtlasTextureId,
            Rect: Rect,
            AtlasWidth: AtlasWidth,
            AtlasHeight: AtlasHeight,
            JobKind: JobKind,
            CachedRgbaQuads: null,
            TargetTexture: TargetTexture,
            OverrideTexture: OverrideTexture,
            NormalScale: NormalScale,
            DepthScale: DepthScale,
            RuleId: RuleId,
            RuleSource: RuleSource,
            DiskCache: DiskCache,
            CacheKey: CacheKey,
            Priority: Priority);
    }
}
